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
using System.Text;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>Hard input-context restriction that prevents synthetic desktop input.</summary>
public sealed record InputContextBlock(
    string Code,
    IReadOnlyList<string> Restrictions,
    string Message,
    InputContextSnapshot Context);

/// <summary>
/// Inspects the Windows execution context of the current Winsight process
/// (session, window station, thread vs input desktop, job UI restrictions,
/// restricted/AppContainer token flags, parent process chain) and can attempt
/// to attach the input thread to the active input desktop.
/// </summary>
public static class InputContext
{
    public const string InputContextBlockedCode = "input_context_blocked";
    public const string InteractiveWindowStationName = "WinSta0";

    public const uint JobUiLimitHandles = 0x00000001;
    public const uint JobUiLimitReadClipboard = 0x00000002;
    public const uint JobUiLimitWriteClipboard = 0x00000004;
    public const uint JobUiLimitSystemParameters = 0x00000008;
    public const uint JobUiLimitDisplaySettings = 0x00000010;
    public const uint JobUiLimitGlobalAtoms = 0x00000020;
    public const uint JobUiLimitDesktop = 0x00000040;
    public const uint JobUiLimitExitWindows = 0x00000080;

    private const uint InteractiveWindowStationFlag = 0x0001;
    private const int UserObjectFlagsIndex = 1;
    private const int UserObjectNameIndex = 2;
    private const uint DesktopReadObjects = 0x0001;
    private const int JobObjectBasicUiRestrictionsClass = 4;
    private const uint TokenQuery = 0x0008;
    private const int TokenHasRestrictionsClass = 21;
    private const int TokenIsAppContainerClass = 29;
    private const uint SnapProcessFlag = 0x00000002;
    private const int MaxParentChainDepth = 8;

    private static readonly (uint Flag, string Name)[] KnownJobUiLimits =
    {
        (JobUiLimitHandles, "JOB_OBJECT_UILIMIT_HANDLES"),
        (JobUiLimitReadClipboard, "JOB_OBJECT_UILIMIT_READCLIPBOARD"),
        (JobUiLimitWriteClipboard, "JOB_OBJECT_UILIMIT_WRITECLIPBOARD"),
        (JobUiLimitSystemParameters, "JOB_OBJECT_UILIMIT_SYSTEMPARAMETERS"),
        (JobUiLimitDisplaySettings, "JOB_OBJECT_UILIMIT_DISPLAYSETTINGS"),
        (JobUiLimitGlobalAtoms, "JOB_OBJECT_UILIMIT_GLOBALATOMS"),
        (JobUiLimitDesktop, "JOB_OBJECT_UILIMIT_DESKTOP"),
        (JobUiLimitExitWindows, "JOB_OBJECT_UILIMIT_EXITWINDOWS"),
    };

    private static readonly Lazy<IReadOnlyList<ProcessChainEntry>> CachedParentChain =
        new(() => QueryParentChain(Environment.ProcessId));

    private static readonly AsyncLocal<Func<InputContextSnapshot>?> OverrideCaptureSlot = new();

    // Test hook (async-local so parallel unit tests never interfere with one another).
    internal static Func<InputContextSnapshot>? OverrideCapture
    {
        get => OverrideCaptureSlot.Value;
        set => OverrideCaptureSlot.Value = value;
    }

    /// <summary>Capture the current process and thread input-context diagnostics.</summary>
    public static InputContextSnapshot Capture()
    {
        if (OverrideCapture is { } overrideCapture)
        {
            return overrideCapture();
        }

        uint? sessionId = NativeMethods.ProcessIdToSessionId((uint)Environment.ProcessId, out uint sid)
            ? sid
            : null;
        uint activeConsoleSessionId = NativeMethods.WTSGetActiveConsoleSessionId();
        bool sessionMatchesConsole = sessionId is uint currentSid && currentSid == activeConsoleSessionId;

        IntPtr winSta = NativeMethods.GetProcessWindowStation();
        string? windowStation = winSta != IntPtr.Zero ? ReadUserObjectName(winSta) : null;
        bool? windowStationVisible = winSta != IntPtr.Zero ? ReadWindowStationVisible(winSta) : null;

        uint threadId = NativeMethods.GetCurrentThreadId();
        IntPtr threadDesktopHandle = NativeMethods.GetThreadDesktop(threadId);
        string? threadDesktop = threadDesktopHandle != IntPtr.Zero ? ReadUserObjectName(threadDesktopHandle) : null;

        string? inputDesktop = null;
        int? inputDesktopError = null;
        IntPtr inputDesktopHandle = NativeMethods.OpenInputDesktop(0, false, DesktopReadObjects);
        if (inputDesktopHandle != IntPtr.Zero)
        {
            try
            {
                inputDesktop = ReadUserObjectName(inputDesktopHandle);
            }
            finally
            {
                NativeMethods.CloseDesktop(inputDesktopHandle);
            }
        }
        else
        {
            inputDesktopError = Marshal.GetLastWin32Error();
        }

        bool desktopMatchesInput =
            !string.IsNullOrEmpty(threadDesktop) &&
            !string.IsNullOrEmpty(inputDesktop) &&
            string.Equals(threadDesktop, inputDesktop, StringComparison.OrdinalIgnoreCase);

        bool inJob = NativeMethods.IsProcessInJob(NativeMethods.GetCurrentProcess(), IntPtr.Zero, out bool jobFlag) && jobFlag;
        uint uiRestrictionsMask = 0;
        int? jobQueryError = null;
        if (inJob)
        {
            JobObjectBasicUiRestrictions info = default;
            int size = Marshal.SizeOf<JobObjectBasicUiRestrictions>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (NativeMethods.QueryInformationJobObject(
                        IntPtr.Zero,
                        JobObjectBasicUiRestrictionsClass,
                        buffer,
                        (uint)size,
                        out _))
                {
                    info = Marshal.PtrToStructure<JobObjectBasicUiRestrictions>(buffer);
                    uiRestrictionsMask = info.UiRestrictionsClass;
                }
                else
                {
                    jobQueryError = Marshal.GetLastWin32Error();
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        IReadOnlyList<string> uiRestrictions = DecodeJobUiRestrictions(uiRestrictionsMask);
        (bool? isRestricted, bool? isAppContainer) = QueryCurrentTokenFlags();
        string integrityLevel = ProcessIntegrity.FormatIntegrity(ProcessIntegrity.QueryCurrent());
        IReadOnlyList<ProcessChainEntry> parentChain = CachedParentChain.Value;

        return new InputContextSnapshot(
            sessionId,
            activeConsoleSessionId,
            sessionMatchesConsole,
            windowStation,
            windowStationVisible,
            threadDesktop,
            inputDesktop,
            inputDesktopError,
            desktopMatchesInput,
            inJob,
            uiRestrictionsMask,
            uiRestrictions,
            jobQueryError,
            isRestricted,
            isAppContainer,
            integrityLevel,
            parentChain,
            threadId);
    }

    /// <summary>Decode a JOBOBJECT_BASIC_UI_RESTRICTIONS bitmask into flag names.</summary>
    public static IReadOnlyList<string> DecodeJobUiRestrictions(uint mask)
    {
        if (mask == 0)
        {
            return Array.Empty<string>();
        }

        List<string> names = new();
        uint remaining = mask;
        foreach ((uint flag, string name) in KnownJobUiLimits)
        {
            if ((mask & flag) != 0)
            {
                names.Add(name);
                remaining &= ~flag;
            }
        }

        if (remaining != 0)
        {
            names.Add(string.Create(CultureInfo.InvariantCulture, $"0x{remaining:x8}"));
        }

        return names;
    }

    /// <summary>
    /// Evaluate whether the execution context has a known hard blocker for
    /// synthetic desktop input (non-interactive window station or job UI
    /// restrictions). Returns null when no hard blocker is detected.
    /// </summary>
    public static InputContextBlock? EvaluateBlock(InputContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<string> restrictions = new();
        if (!string.IsNullOrEmpty(context.WindowStation) &&
            !string.Equals(context.WindowStation, InteractiveWindowStationName, StringComparison.OrdinalIgnoreCase))
        {
            restrictions.Add($"non_interactive_window_station:{context.WindowStation}");
        }
        else if (context.WindowStationVisible == false)
        {
            restrictions.Add($"non_interactive_window_station:{context.WindowStation ?? "unknown"}(invisible)");
        }

        if (context.JobUiRestrictionsMask != 0)
        {
            IReadOnlyList<string> flags = context.JobUiRestrictions.Count > 0
                ? context.JobUiRestrictions
                : DecodeJobUiRestrictions(context.JobUiRestrictionsMask);
            foreach (string flag in flags)
            {
                restrictions.Add(flag);
            }
        }

        if (restrictions.Count == 0)
        {
            return null;
        }

        string joined = string.Join(", ", restrictions);
        string message = string.Create(
            CultureInfo.InvariantCulture,
            $"{InputContextBlockedCode}: synthetic input is blocked by the Winsight process context ({joined}). Launch winsight-mcp in an interactive desktop session without job UI restrictions.");

        return new InputContextBlock(InputContextBlockedCode, restrictions, message, context);
    }

    /// <summary>One-line summary for embedding in move_not_applied error messages.</summary>
    internal static string FormatSummary(InputContextSnapshot context)
    {
        string jobSummary = !context.InJob
            ? "inJob=false"
            : context.JobUiRestrictions.Count > 0
                ? $"inJob=true(uiLimits={string.Join("|", context.JobUiRestrictions)})"
                : context.JobQueryWin32Error is int jErr
                    ? string.Create(CultureInfo.InvariantCulture, $"inJob=true(queryErr={jErr})")
                    : "inJob=true(uiLimits=none)";

        string parents = context.ParentChain.Count > 0
            ? string.Join("->", context.ParentChain.Select(p => $"{p.Name}({p.ProcessId})"))
            : "none";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"session={context.SessionId?.ToString(CultureInfo.InvariantCulture) ?? "?"}/console={context.ActiveConsoleSessionId}, winSta={context.WindowStation ?? "?"}(vis={context.WindowStationVisible?.ToString() ?? "?"}), desktop={context.ThreadDesktop ?? "?"}/input={context.InputDesktop ?? "?"}, {jobSummary}, restricted={context.IsRestrictedToken?.ToString() ?? "?"}, appContainer={context.IsAppContainer?.ToString() ?? "?"}, parents={parents}");
    }

    private static string? ReadUserObjectName(IntPtr handle)
    {
        NativeMethods.GetUserObjectInformationW(handle, UserObjectNameIndex, IntPtr.Zero, 0, out uint needed);
        if (needed == 0)
        {
            return null;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!NativeMethods.GetUserObjectInformationW(handle, UserObjectNameIndex, buffer, needed, out _))
            {
                return null;
            }
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool? ReadWindowStationVisible(IntPtr winSta)
    {
        int size = Marshal.SizeOf<UserObjectFlags>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!NativeMethods.GetUserObjectInformationW(winSta, UserObjectFlagsIndex, buffer, (uint)size, out _))
            {
                return null;
            }
            UserObjectFlags flags = Marshal.PtrToStructure<UserObjectFlags>(buffer);
            return (flags.Flags & InteractiveWindowStationFlag) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (bool? IsRestricted, bool? IsAppContainer) QueryCurrentTokenFlags()
    {
        if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), TokenQuery, out IntPtr token))
        {
            return (null, null);
        }

        try
        {
            bool? hasRestrictions = QueryTokenDwordBool(token, TokenHasRestrictionsClass);
            if (hasRestrictions is null)
            {
                hasRestrictions = NativeMethods.IsTokenRestricted(token);
            }
            bool? isAppContainer = QueryTokenDwordBool(token, TokenIsAppContainerClass);
            return (hasRestrictions, isAppContainer);
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    private static bool? QueryTokenDwordBool(IntPtr tokenHandle, int infoClass)
    {
        IntPtr buffer = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            if (!NativeMethods.GetTokenInformation(tokenHandle, infoClass, buffer, sizeof(uint), out _))
            {
                return null;
            }
            return Marshal.ReadInt32(buffer) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<ProcessChainEntry> QueryParentChain(int startPid)
    {
        IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(SnapProcessFlag, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return Array.Empty<ProcessChainEntry>();
        }

        Dictionary<int, (int ParentPid, string Name)> map = new();
        try
        {
            ProcessEntry32W entry = new() { Size = (uint)Marshal.SizeOf<ProcessEntry32W>() };
            if (NativeMethods.Process32FirstW(snapshot, ref entry))
            {
                do
                {
                    map[(int)entry.ProcessId] = ((int)entry.ParentProcessId, entry.ExeFile ?? string.Empty);
                }
                while (NativeMethods.Process32NextW(snapshot, ref entry));
            }
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }

        List<ProcessChainEntry> chain = new();
        HashSet<int> visited = new() { startPid };
        int current = startPid;
        for (int depth = 0; depth < MaxParentChainDepth; depth++)
        {
            if (!map.TryGetValue(current, out (int ParentPid, string Name) info))
            {
                break;
            }
            int parentPid = info.ParentPid;
            if (parentPid <= 0 || !visited.Add(parentPid))
            {
                break;
            }
            if (!map.TryGetValue(parentPid, out (int ParentPid, string Name) parentInfo))
            {
                chain.Add(new ProcessChainEntry(parentPid, "exited"));
                break;
            }
            chain.Add(new ProcessChainEntry(parentPid, parentInfo.Name));
            current = parentPid;
        }

        return chain;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UserObjectFlags
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool Inherit;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Reserved;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicUiRestrictions
    {
        public uint UiRestrictionsClass;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32W
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

        [DllImport("kernel32.dll")]
        public static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetProcessWindowStation();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetThreadDesktop(uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint desiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseDesktop(IntPtr desktop);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetUserObjectInformationW(
            IntPtr obj,
            int index,
            IntPtr info,
            uint length,
            out uint lengthNeeded);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsProcessInJob(IntPtr processHandle, IntPtr jobHandle, [MarshalAs(UnmanagedType.Bool)] out bool result);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryInformationJobObject(
            IntPtr jobHandle,
            int jobObjectInformationClass,
            IntPtr jobObjectInformation,
            uint jobObjectInformationLength,
            out uint returnLength);

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

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsTokenRestricted(IntPtr tokenHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32W entry);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32W entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
