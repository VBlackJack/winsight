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
    public const int DefaultProgressIntervalMs = 1_000;

    private const string TimedOutCode = "timed_out";
    private const int MaxTitleLength = 512;
    private const uint CurrentSession = 0xFFFFFFFF;
    private const int WtsSessionInfoEx = 25;
    private const int WtsInfoLevelOne = 1;
    private const int SessionFlagsOffset = 12;
    private const int SessionStateLocked = 0;
    private const int SessionStateUnlocked = 1;

    private readonly IUserActivityBackend _backend;

    public UserActivityMonitor()
        : this(NativeUserActivityBackend.Instance)
    {
    }

    internal UserActivityMonitor(IUserActivityBackend backend)
    {
        _backend = backend;
    }

    /// <summary>
    /// Snapshot the desktop. The foreground title is returned only when its
    /// process is in <paramref name="revealTitleFor"/>.
    /// </summary>
    public UserActivitySnapshot Read(IReadOnlyCollection<int>? revealTitleFor = null)
    {
        (uint now, uint lastInput, InputAttribution attribution) = Sample(_backend);
        long? humanIdle = HumanIdleMs(now, attribution);
        int? yieldMs = UserActivityPolicy.YieldToUserMs;
        return new UserActivitySnapshot(
            InputAttributor.AgeMs(now, lastInput),
            attribution.LastInputIsWinsight ? SourceWinsight : SourceHuman,
            humanIdle,
            _backend.ReadForeground(revealTitleFor),
            _backend.ReadSessionLocked(),
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
        (uint now, _, InputAttribution attribution) = Sample(NativeUserActivityBackend.Instance);
        return HumanIdleMs(now, attribution);
    }

    private static (uint Now, uint LastInput, InputAttribution Attribution) Sample(IUserActivityBackend backend)
    {
        uint now = backend.TickNow();
        uint lastInput = backend.LastInputTick(now);
        InputAttribution attribution = InputAttributor.Attribute(now, lastInput, backend.ReadLedger());
        if (!attribution.LastInputIsWinsight)
        {
            backend.RecordHuman(lastInput);
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
        if (ValidateWaitBounds(idleMs, timeoutMs) is { } validationError)
        {
            return validationError;
        }
        int poll = Math.Max(1, pollMs);
        long startMs = _backend.ElapsedMs();
        while (true)
        {
            UserActivitySnapshot activity = Read(revealTitleFor);
            long waitedMs = Math.Max(0, _backend.ElapsedMs() - startMs);
            if (activity.HumanIdleMs is not long humanIdle || humanIdle >= idleMs)
            {
                return OperationResult.OkWith(new UserIdleWaitResult(true, waitedMs, idleMs, activity));
            }
            long remaining = timeoutMs - waitedMs;
            if (remaining <= 0)
            {
                return TimeoutResult(timeoutMs, idleMs, waitedMs, humanIdle, activity);
            }
            // Sleep no longer than it takes the user to reach the idle mark.
            int sleepMs = (int)Math.Min(remaining, Math.Min(poll, Math.Max(1, idleMs - humanIdle)));
            _backend.Sleep(sleepMs);
        }
    }

    /// <summary>
    /// Asynchronous counterpart to <see cref="WaitForIdle"/> with a single
    /// end-to-end timeout, periodic progress notifications and cancellation.
    /// Never injects input.
    /// </summary>
    public async Task<OperationResult> WaitForIdleAsync(
        int idleMs,
        int timeoutMs,
        int pollMs = DefaultPollMs,
        IReadOnlyCollection<int>? revealTitleFor = null,
        Action<long, int, bool?>? onProgress = null,
        int progressIntervalMs = DefaultProgressIntervalMs,
        CancellationToken cancellationToken = default)
    {
        if (ValidateWaitBounds(idleMs, timeoutMs) is { } validationError)
        {
            return validationError;
        }
        int poll = Math.Max(1, pollMs);
        int reportEvery = Math.Max(1, progressIntervalMs);
        long startMs = _backend.ElapsedMs();
        long lastReportedMs = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UserActivitySnapshot activity = Read(revealTitleFor);
            long waitedMs = Math.Max(0, _backend.ElapsedMs() - startMs);
            if (activity.HumanIdleMs is not long humanIdle || humanIdle >= idleMs)
            {
                onProgress?.Invoke(Math.Min(timeoutMs, waitedMs), timeoutMs, true);
                return OperationResult.OkWith(new UserIdleWaitResult(true, waitedMs, idleMs, activity));
            }
            long remaining = timeoutMs - waitedMs;
            if (remaining <= 0)
            {
                onProgress?.Invoke(Math.Min(timeoutMs, waitedMs), timeoutMs, false);
                return TimeoutResult(timeoutMs, idleMs, waitedMs, humanIdle, activity);
            }
            if (onProgress is not null && waitedMs - lastReportedMs >= reportEvery)
            {
                lastReportedMs = waitedMs;
                onProgress(Math.Min(timeoutMs, waitedMs), timeoutMs, null);
            }
            int sleepMs = (int)Math.Min(remaining, Math.Min(poll, Math.Max(1, idleMs - humanIdle)));
            await _backend.DelayAsync(sleepMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private static OperationResult? ValidateWaitBounds(int idleMs, int timeoutMs)
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
        return null;
    }

    private static OperationResult TimeoutResult(
        int timeoutMs,
        int idleMs,
        long waitedMs,
        long humanIdle,
        UserActivitySnapshot activity) =>
        OperationResult.FailureWith(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{TimedOutCode}: the user was still active after {timeoutMs} ms " +
                $"(last human input {humanIdle} ms ago, waiting for {idleMs} ms of idle)."),
            new UserIdleWaitResult(false, waitedMs, idleMs, activity));

    internal static uint TickNow() => unchecked((uint)Environment.TickCount);

    private sealed class NativeUserActivityBackend : IUserActivityBackend
    {
        public static readonly NativeUserActivityBackend Instance = new();

        public uint TickNow() => UserActivityMonitor.TickNow();

        public long ElapsedMs() => Environment.TickCount64;

        public uint LastInputTick(uint now) => UserActivityMonitor.LastInputTick(now);

        public LedgerState ReadLedger() => InputLedger.Read();

        public void RecordHuman(uint tick) => InputLedger.RecordHuman(tick);

        public ForegroundWindowInfo? ReadForeground(IReadOnlyCollection<int>? revealTitleFor) =>
            UserActivityMonitor.ReadForeground(revealTitleFor);

        public bool? ReadSessionLocked() => UserActivityMonitor.ReadSessionLocked();

        public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);

        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
            Task.Delay(milliseconds, cancellationToken);
    }

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
