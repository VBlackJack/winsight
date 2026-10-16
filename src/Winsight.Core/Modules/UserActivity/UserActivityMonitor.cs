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

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UserActivity;

/// <summary>
/// Reads who is using the shared desktop: time since the last human input
/// (Winsight's own injections excluded through <see cref="InputLedger"/>), the
/// foreground window and whether the session is locked. Every read refreshes
/// the session-wide record of the last human input.
/// </summary>
public sealed class UserActivityMonitor
{
    public const string SourceHuman = "human";
    public const string SourceWinsight = "winsight";

    /// <summary>Bounds for <see cref="WaitForIdle"/>, in milliseconds.</summary>
    public const int MaxIdleMs = 600_000;
    public const int MaxWaitTimeoutMs = 600_000;
    public const int DefaultPollMs = 100;

    private const string TimedOutCode = "timed_out";
    private const int MaxTitleLength = 512;
    private const uint CurrentSession = 0xFFFFFFFF;
    private const int WtsSessionInfoEx = 25;
    private const int WtsInfoLevelOne = 1;
    private const int SessionFlagsOffset = 12;
    private const int SessionStateLocked = 0;
    private const int SessionStateUnlocked = 1;

    /// <summary>
    /// Snapshot the desktop. The foreground title is returned only when its
    /// process is in <paramref name="revealTitleFor"/>.
    /// </summary>
    public UserActivitySnapshot Read(IReadOnlyCollection<int>? revealTitleFor = null)
    {
        (uint now, uint lastInput, InputAttribution attribution) = Sample();
        long? humanIdle = HumanIdleMs(now, attribution);
        int? yieldMs = UserActivityPolicy.YieldToUserMs;
        return new UserActivitySnapshot(
            InputAttributor.AgeMs(now, lastInput),
            attribution.LastInputIsWinsight ? SourceWinsight : SourceHuman,
            humanIdle,
            ReadForeground(revealTitleFor),
            ReadSessionLocked(),
            yieldMs,
            UserActivityPolicy.IsActive(humanIdle, yieldMs));
    }

    /// <summary>
    /// Milliseconds since the last human input, or null when none was seen. Also
    /// refreshes the session-wide record, so an injection about to start does
    /// not hide the human input that preceded it.
    /// </summary>
    internal static long? ReadHumanIdleMs()
    {
        (uint now, _, InputAttribution attribution) = Sample();
        return HumanIdleMs(now, attribution);
    }

    private static (uint Now, uint LastInput, InputAttribution Attribution) Sample()
    {
        uint now = TickNow();
        uint lastInput = LastInputTick(now);
        InputAttribution attribution = InputAttributor.Attribute(now, lastInput, InputLedger.Read());
        if (!attribution.LastInputIsWinsight)
        {
            InputLedger.RecordHuman(lastInput);
        }
        return (now, lastInput, attribution);
    }

    private static long? HumanIdleMs(uint now, InputAttribution attribution) =>
        attribution.HumanTick is uint human ? InputAttributor.AgeMs(now, human) : null;

    /// <summary>
    /// Wait until no human input happened for <paramref name="idleMs"/>, or
    /// <paramref name="timeoutMs"/> elapses. Never injects input.
    /// </summary>
    public OperationResult WaitForIdle(
        int idleMs,
        int timeoutMs,
        int pollMs = DefaultPollMs,
        IReadOnlyCollection<int>? revealTitleFor = null)
    {
        if (idleMs is <= 0 or > MaxIdleMs)
        {
            return OperationResult.Failure(string.Create(
                CultureInfo.InvariantCulture, $"idleMs must be between 1 and {MaxIdleMs} (got {idleMs})."));
        }
        if (timeoutMs is < 0 or > MaxWaitTimeoutMs)
        {
            return OperationResult.Failure(string.Create(
                CultureInfo.InvariantCulture, $"timeoutMs must be between 0 and {MaxWaitTimeoutMs} (got {timeoutMs})."));
        }
        int poll = Math.Max(1, pollMs);
        Stopwatch waited = Stopwatch.StartNew();
        while (true)
        {
            UserActivitySnapshot activity = Read(revealTitleFor);
            if (activity.HumanIdleMs is not long humanIdle || humanIdle >= idleMs)
            {
                return OperationResult.OkWith(new UserIdleWaitResult(true, waited.ElapsedMilliseconds, idleMs, activity));
            }
            long remaining = timeoutMs - waited.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                return OperationResult.FailureWith(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{TimedOutCode}: the user was still active after {timeoutMs} ms " +
                        $"(last human input {humanIdle} ms ago, waiting for {idleMs} ms of idle)."),
                    new UserIdleWaitResult(false, waited.ElapsedMilliseconds, idleMs, activity));
            }
            // Sleep no longer than it takes the user to reach the idle mark.
            Thread.Sleep((int)Math.Min(remaining, Math.Min(poll, idleMs - humanIdle)));
        }
    }

    internal static uint TickNow() => unchecked((uint)Environment.TickCount);

    private static uint LastInputTick(uint now)
    {
        LastInputInfo info = new() { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return NativeMethods.GetLastInputInfo(ref info) ? info.Time : now;
    }

    private static ForegroundWindowInfo? ReadForeground(IReadOnlyCollection<int>? revealTitleFor)
    {
        IntPtr handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return null;
        }
        if (NativeMethods.GetWindowThreadProcessId(handle, out uint owner) == 0)
        {
            return null;
        }
        int processId = (int)owner;
        bool reveal = revealTitleFor?.Contains(processId) == true;
        return new ForegroundWindowInfo(
            handle.ToInt64(), processId, ProcessName(processId), reveal ? Title(handle) : null, !reveal);
    }

    internal static string? ProcessName(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Title(IntPtr handle)
    {
        char[] title = new char[MaxTitleLength];
        int length = NativeMethods.GetWindowText(handle, title, title.Length);
        return new string(title, 0, Math.Max(0, length));
    }

    private static bool? ReadSessionLocked()
    {
        if (!NativeMethods.WTSQuerySessionInformation(
                IntPtr.Zero, CurrentSession, WtsSessionInfoEx, out IntPtr buffer, out uint bytes))
        {
            return null;
        }
        try
        {
            if (bytes < SessionFlagsOffset + sizeof(int) || Marshal.ReadInt32(buffer) != WtsInfoLevelOne)
            {
                return null;
            }
            return Marshal.ReadInt32(buffer, SessionFlagsOffset) switch
            {
                SessionStateLocked => true,
                SessionStateUnlocked => false,
                _ => null,
            };
        }
        finally
        {
            NativeMethods.WTSFreeMemory(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetLastInputInfo(ref LastInputInfo info);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
        public static extern int GetWindowText(IntPtr hWnd, [Out] char[] text, int maxCount);

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "WTSQuerySessionInformationW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WTSQuerySessionInformation(
            IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out uint bytesReturned);

        [DllImport("wtsapi32.dll")]
        public static extern void WTSFreeMemory(IntPtr memory);
    }
}
