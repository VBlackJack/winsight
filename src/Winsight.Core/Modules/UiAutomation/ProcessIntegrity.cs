// Copyright 2026 Julien Bombled
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Globalization;
using System.Runtime.InteropServices;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>Integrity level read from a Windows process token via TokenIntegrityLevel.</summary>
public sealed record ProcessIntegrityInfo(
    uint? Rid,
    string Name,
    bool AccessDenied,
    int? Win32Error);

/// <summary>Result of comparing Winsight's token integrity level against a target process.</summary>
public sealed record IntegrityComparison(
    ProcessIntegrityInfo Current,
    ProcessIntegrityInfo Target,
    bool IsUipiBlocked,
    string? Warning);

/// <summary>
/// Queries Windows Mandatory Integrity Control (MIC) levels for the current
/// process and a target process so Winsight can detect UIPI mismatches before
/// synthetic input is silently dropped.
/// </summary>
public static class ProcessIntegrity
{
    public const string UipiBlockedCode = "uipi_blocked";

    public const uint RidUntrusted = 0x0000;
    public const uint RidLow = 0x1000;
    public const uint RidMedium = 0x2000;
    public const uint RidMediumPlus = 0x2100;
    public const uint RidHigh = 0x3000;
    public const uint RidSystem = 0x4000;
    public const uint RidProtectedProcess = 0x5000;

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevelClass = 25;
    private const int ErrorAccessDenied = 5;
    private const int ErrorInsufficientBuffer = 122;

    private static readonly AsyncLocal<Func<int, IntegrityComparison>?> OverrideCheckSlot = new();
    private static readonly Lazy<ProcessIntegrityInfo> CachedCurrent = new(QueryCurrentUncached);

    // Test hook (async-local so parallel unit tests never interfere with one another).
    internal static Func<int, IntegrityComparison>? OverrideCheck
    {
        get => OverrideCheckSlot.Value;
        set => OverrideCheckSlot.Value = value;
    }

    /// <summary>Compare the current process integrity level against <paramref name="targetProcessId"/>.</summary>
    public static IntegrityComparison CheckTarget(int targetProcessId)
    {
        if (OverrideCheck is { } overrideCheck)
        {
            return overrideCheck(targetProcessId);
        }

        ProcessIntegrityInfo current = QueryCurrent();
        ProcessIntegrityInfo target = QueryProcess(targetProcessId);
        return Compare(current, target);
    }

    /// <summary>Query the integrity level of the current Winsight process (cached for process lifetime).</summary>
    public static ProcessIntegrityInfo QueryCurrent() => CachedCurrent.Value;

    private static ProcessIntegrityInfo QueryCurrentUncached()
    {
        IntPtr currentProcess = NativeMethods.GetCurrentProcess();
        return QueryProcessHandle(currentProcess);
    }

    /// <summary>Query the integrity level of another process by PID.</summary>
    public static ProcessIntegrityInfo QueryProcess(int processId)
    {
        if (processId == Environment.ProcessId)
        {
            return QueryCurrent();
        }

        IntPtr handle = NativeMethods.OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            return error == ErrorAccessDenied
                ? new ProcessIntegrityInfo(null, "unknown", AccessDenied: true, error)
                : new ProcessIntegrityInfo(null, "unknown", AccessDenied: false, error);
        }

        try
        {
            return QueryProcessHandle(handle);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>
    /// Pure comparison of two queried integrity levels. UIPI is reported as
    /// blocked only when both integrity RIDs are known and the target RID is
    /// strictly higher than Winsight's RID. When the target token cannot be
    /// queried (including ERROR_ACCESS_DENIED), the level is reported as
    /// "unknown" and input is allowed so move_not_applied / SendInput
    /// diagnostics remain the safety net.
    /// </summary>
    public static IntegrityComparison Compare(ProcessIntegrityInfo current, ProcessIntegrityInfo target)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);

        bool blocked = current.Rid is uint currentRid &&
                       target.Rid is uint targetRid &&
                       targetRid > currentRid;

        if (!blocked)
        {
            return new IntegrityComparison(current, target, IsUipiBlocked: false, Warning: null);
        }

        string currentDesc = FormatIntegrity(current);
        string targetDesc = FormatIntegrity(target);
        string warning = string.Create(
            CultureInfo.InvariantCulture,
            $"{UipiBlockedCode}: target process integrity ({targetDesc}) is higher than Winsight ({currentDesc}). Windows UIPI will silently drop synthetic mouse and keyboard input (SendInput/SetCursorPos). Run Winsight at the same or higher integrity level to send input to this target.");

        return new IntegrityComparison(current, target, IsUipiBlocked: true, Warning: warning);
    }

    /// <summary>Map a Mandatory Integrity Control RID to a stable human-readable name.</summary>
    public static string ClassifyRid(uint rid) =>
        rid switch
        {
            < RidLow => "untrusted",
            < RidMedium => "low",
            < RidMediumPlus => "medium",
            < RidHigh => "medium_plus",
            < RidSystem => "high",
            < RidProtectedProcess => "system",
            _ => "protected_process",
        };

    internal static string FormatIntegrity(ProcessIntegrityInfo info)
    {
        if (info.Rid is uint rid)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{info.Name} (0x{rid:x4})");
        }

        return info.Win32Error is int error
            ? string.Create(CultureInfo.InvariantCulture, $"{info.Name} (win32={error})")
            : info.Name;
    }

    private static ProcessIntegrityInfo QueryProcessHandle(IntPtr processHandle)
    {
        if (!NativeMethods.OpenProcessToken(processHandle, TokenQuery, out IntPtr tokenHandle))
        {
            int error = Marshal.GetLastWin32Error();
            return new ProcessIntegrityInfo(null, "unknown", AccessDenied: error == ErrorAccessDenied, error);
        }

        try
        {
            return QueryTokenIntegrity(tokenHandle);
        }
        finally
        {
            NativeMethods.CloseHandle(tokenHandle);
        }
    }

    private static ProcessIntegrityInfo QueryTokenIntegrity(IntPtr tokenHandle)
    {
        NativeMethods.GetTokenInformation(tokenHandle, TokenIntegrityLevelClass, IntPtr.Zero, 0, out uint needed);
        int probeError = Marshal.GetLastWin32Error();
        if (needed == 0 || (probeError != ErrorInsufficientBuffer && probeError != 0))
        {
            return new ProcessIntegrityInfo(
                null,
                "unknown",
                AccessDenied: probeError == ErrorAccessDenied,
                probeError);
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!NativeMethods.GetTokenInformation(tokenHandle, TokenIntegrityLevelClass, buffer, needed, out _))
            {
                int error = Marshal.GetLastWin32Error();
                return new ProcessIntegrityInfo(
                    null,
                    "unknown",
                    AccessDenied: error == ErrorAccessDenied,
                    error);
            }

            TokenMandatoryLabel label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
            if (label.Sid == IntPtr.Zero)
            {
                return new ProcessIntegrityInfo(null, "missing_sid", AccessDenied: false, null);
            }

            IntPtr countPtr = NativeMethods.GetSidSubAuthorityCount(label.Sid);
            if (countPtr == IntPtr.Zero)
            {
                return new ProcessIntegrityInfo(null, "invalid_sid", AccessDenied: false, null);
            }

            byte subAuthorityCount = Marshal.ReadByte(countPtr);
            if (subAuthorityCount == 0)
            {
                return new ProcessIntegrityInfo(null, "empty_sid", AccessDenied: false, null);
            }

            IntPtr ridPtr = NativeMethods.GetSidSubAuthority(label.Sid, (uint)(subAuthorityCount - 1));
            if (ridPtr == IntPtr.Zero)
            {
                return new ProcessIntegrityInfo(null, "invalid_rid", AccessDenied: false, null);
            }

            uint rid = unchecked((uint)Marshal.ReadInt32(ridPtr));
            return new ProcessIntegrityInfo(rid, ClassifyRid(rid), AccessDenied: false, null);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetTokenInformation(
            IntPtr tokenHandle,
            int tokenInformationClass,
            IntPtr tokenInformation,
            uint tokenInformationLength,
            out uint returnLength);

        [DllImport("advapi32.dll")]
        public static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

        [DllImport("advapi32.dll")]
        public static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthorityIndex);
    }
}
