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
    public const string SourceOtherInjected = "other_injected";

    public const string KindMouse = "mouse";
    public const string KindKeyboard = "keyboard";
    public const string KindOther = "other";
    public const string KindUnknown = "unknown";

    /// <summary>Maximum number of recent distinct input timestamps retained for diagnostics.</summary>
    public const int RecentInputSampleCapacity = 8;

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
    private const int SessionStateLocked = 0;
    private const int SessionStateUnlocked = 1;
    private const uint DesktopReadObjects = 0x0001;
    private const int UoiName = 2;
    private const int ErrorAccessDenied = 5;
    private const string DefaultDesktopName = "Default";
    private const string WinlogonDesktopName = "Winlogon";
    private const int MaxDesktopNameChars = 128;

    // WTSINFOEXW starts with DWORD Level (4 bytes), followed by WTSINFOEX_LEVEL1_W
    // which contains 8-byte LARGE_INTEGER fields and therefore aligns to offset 8.
    // Inside WTSINFOEX_LEVEL1_W: SessionId (+0), SessionState (+4), SessionFlags (+8) => offset 16.
    internal static readonly int WtsSessionFlagsOffset =
        Marshal.OffsetOf<WtsInfoEx>(nameof(WtsInfoEx.Level1)).ToInt32() +
        Marshal.OffsetOf<WtsInfoExLevel1>(nameof(WtsInfoExLevel1.SessionFlags)).ToInt32();

    private readonly IUserActivityBackend _backend;
    private readonly Lock _historyGate = new();
    private readonly List<RecordedInputTick> _recentInputs = new(RecentInputSampleCapacity);
    private CursorPoint? _lastCursor;

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
        SampleResult sample = Sample(_backend);
        CursorPoint? cursor = _backend.ReadCursorPosition();
        UserActivityDiagnostics diagnostics = RecordDiagnostics(sample, cursor);
        long? humanIdle = sample.HumanIdleMs;
        int? yieldMs = UserActivityPolicy.YieldToUserMs;
        return new UserActivitySnapshot(
            InputAttributor.AgeMs(sample.Now, sample.LastInput),
            sample.Source,
            humanIdle,
            _backend.ReadForeground(revealTitleFor),
            _backend.ReadSessionLocked(),
            yieldMs,
            UserActivityPolicy.IsActive(humanIdle, yieldMs),
            diagnostics);
    }

    /// <summary>
    /// Milliseconds since the last human input, or null when none was seen. Also
    /// refreshes the session-wide record, so an injection about to start does
    /// not hide the human input that preceded it.
    /// </summary>
    internal static long? ReadHumanIdleMs() =>
        Sample(NativeUserActivityBackend.Instance).HumanIdleMs;

    private static SampleResult Sample(IUserActivityBackend backend)
    {
        uint now = backend.TickNow();
        uint lastInput = backend.LastInputTick(now);
        LedgerState ledger = backend.ReadLedger();
        InputAttribution attribution = InputAttributor.Attribute(now, lastInput, ledger);
        HookInputState hook = backend.ReadHookState();

        string source;
        string inputKind;
        bool? injectedByOther;
        uint? humanTick;

        if (hook.Active)
        {
            bool hookMatchesLastInput = hook.HasEvent &&
                Math.Abs((long)InputAttributor.AgeMs(now, lastInput) - InputAttributor.AgeMs(now, hook.LastEventTick)) <=
                InputAttributor.InjectionMarginMs;

            if (attribution.LastInputIsWinsight)
            {
                source = SourceWinsight;
                inputKind = hookMatchesLastInput ? hook.LastInputKind : KindUnknown;
                injectedByOther = false;
                humanTick = ResolveHardwareHumanTick(now, hook, ledger);
            }
            else if (hookMatchesLastInput && hook.LastEventInjected)
            {
                // Input was flagged LLMHF_INJECTED / LLKHF_INJECTED and did not come from Winsight.
                source = SourceOtherInjected;
                inputKind = hook.LastInputKind;
                injectedByOther = true;
                humanTick = ResolveHardwareHumanTick(now, hook, ledger);
            }
            else if (hookMatchesLastInput)
            {
                source = SourceHuman;
                inputKind = hook.LastInputKind;
                injectedByOther = false;
                humanTick = lastInput;
                backend.RecordHuman(lastInput);
            }
            else
            {
                // GetLastInputInfo advanced without a matching mouse/keyboard LL hook event
                // (for example a gamepad, pen, touch or other HID device).
                source = SourceHuman;
                inputKind = KindOther;
                injectedByOther = false;
                humanTick = lastInput;
                backend.RecordHuman(lastInput);
            }
        }
        else
        {
            injectedByOther = null;
            inputKind = KindUnknown;
            if (!attribution.LastInputIsWinsight)
            {
                source = SourceHuman;
                humanTick = lastInput;
                backend.RecordHuman(lastInput);
            }
            else
            {
                source = SourceWinsight;
                humanTick = attribution.HumanTick;
            }
        }

        long? humanIdleMs = humanTick is uint tick ? InputAttributor.AgeMs(now, tick) : null;
        return new SampleResult(now, lastInput, source, inputKind, hook.Active, injectedByOther, humanIdleMs);
    }

    private static uint? ResolveHardwareHumanTick(uint now, HookInputState hook, LedgerState ledger)
    {
        if (hook.HasHardwareEvent && ledger.HasHuman)
        {
            return InputAttributor.AgeMs(now, hook.LastHardwareTick) <= InputAttributor.AgeMs(now, ledger.HumanTick)
                ? hook.LastHardwareTick
                : ledger.HumanTick;
        }
        if (hook.HasHardwareEvent)
        {
            return hook.LastHardwareTick;
        }
        return ledger.HasHuman ? ledger.HumanTick : null;
    }

    private UserActivityDiagnostics RecordDiagnostics(SampleResult sample, CursorPoint? cursor)
    {
        lock (_historyGate)
        {
            bool cursorMoved = _lastCursor is not null && cursor is not null && _lastCursor != cursor;
            if (cursor is not null)
            {
                _lastCursor = cursor;
            }

            string resolvedKind = sample.InputKind == KindUnknown && cursorMoved
                ? KindMouse
                : sample.InputKind;

            if (_recentInputs.Count == 0 || _recentInputs[^1].Tick != sample.LastInput)
            {
                long? deltaMs = _recentInputs.Count > 0
                    ? InputAttributor.AgeMs(sample.LastInput, _recentInputs[^1].Tick)
                    : null;
                if (_recentInputs.Count >= RecentInputSampleCapacity)
                {
                    _recentInputs.RemoveAt(0);
                }
                _recentInputs.Add(new RecordedInputTick(
                    sample.LastInput,
                    deltaMs,
                    sample.Source,
                    resolvedKind,
                    cursorMoved,
                    sample.InjectedByOther));
            }
            else if (cursorMoved && !_recentInputs[^1].CursorMoved)
            {
                RecordedInputTick latest = _recentInputs[^1];
                _recentInputs[^1] = latest with
                {
                    CursorMoved = true,
                    InputKind = latest.InputKind == KindUnknown ? KindMouse : latest.InputKind,
                };
            }

            List<InputSample> snapshot = new(_recentInputs.Count);
            foreach (RecordedInputTick entry in _recentInputs)
            {
                snapshot.Add(new InputSample(
                    InputAttributor.AgeMs(sample.Now, entry.Tick),
                    entry.DeltaFromPreviousMs,
                    entry.Source,
                    entry.InputKind,
                    entry.CursorMoved,
                    entry.Injected));
            }

            return new UserActivityDiagnostics(
                resolvedKind,
                cursorMoved,
                cursor,
                sample.HookActive,
                sample.InjectedByOther,
                snapshot);
        }
    }

    private readonly record struct SampleResult(
        uint Now,
        uint LastInput,
        string Source,
        string InputKind,
        bool HookActive,
        bool? InjectedByOther,
        long? HumanIdleMs);

    private readonly record struct RecordedInputTick(
        uint Tick,
        long? DeltaFromPreviousMs,
        string Source,
        string InputKind,
        bool CursorMoved,
        bool? Injected);

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

        public CursorPoint? ReadCursorPosition() => UserActivityMonitor.ReadCursorPosition();

        public HookInputState ReadHookState() => LowLevelInputHook.ReadShared();

        public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);

        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
            Task.Delay(milliseconds, cancellationToken);
    }

    private static uint LastInputTick(uint now)
    {
        LastInputInfo info = new() { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return NativeMethods.GetLastInputInfo(ref info) ? info.Time : now;
    }

    private static CursorPoint? ReadCursorPosition() =>
        NativeMethods.GetCursorPos(out PointStruct pt) ? new CursorPoint(pt.X, pt.Y) : null;

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

    internal static bool? ReadSessionLocked()
    {
        if (NativeMethods.WTSQuerySessionInformation(
                IntPtr.Zero, CurrentSession, WtsSessionInfoEx, out IntPtr buffer, out uint bytes))
        {
            try
            {
                bool? fromWts = ParseWtsSessionLocked(buffer, bytes);
                if (fromWts.HasValue)
                {
                    return fromWts;
                }
            }
            finally
            {
                NativeMethods.WTSFreeMemory(buffer);
            }
        }

        return ReadSessionLockedFromInputDesktop();
    }

    internal static bool? ParseWtsSessionLocked(IntPtr buffer, uint bytes)
    {
        if (buffer == IntPtr.Zero || bytes < (uint)(WtsSessionFlagsOffset + sizeof(int)))
        {
            return null;
        }
        int level = Marshal.ReadInt32(buffer);
        int flags = Marshal.ReadInt32(buffer, WtsSessionFlagsOffset);
        return InterpretWtsSessionFlags(level, flags);
    }

    internal static bool? InterpretWtsSessionFlags(int level, int sessionFlags)
    {
        if (level != WtsInfoLevelOne)
        {
            return null;
        }
        return sessionFlags switch
        {
            SessionStateLocked => true,
            SessionStateUnlocked => false,
            _ => null,
        };
    }

    private static bool? ReadSessionLockedFromInputDesktop()
    {
        IntPtr desktop = NativeMethods.OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktop == IntPtr.Zero)
        {
            return Marshal.GetLastWin32Error() == ErrorAccessDenied ? true : null;
        }

        try
        {
            char[] nameBuffer = new char[MaxDesktopNameChars];
            if (!NativeMethods.GetUserObjectInformation(
                    desktop,
                    UoiName,
                    nameBuffer,
                    (uint)(nameBuffer.Length * sizeof(char)),
                    out uint bytesNeeded) ||
                bytesNeeded < sizeof(char))
            {
                return null;
            }
            int charCount = (int)Math.Min((uint)nameBuffer.Length, bytesNeeded / sizeof(char));
            string name = new string(nameBuffer, 0, charCount).TrimEnd('\0');
            if (string.Equals(name, DefaultDesktopName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (string.Equals(name, WinlogonDesktopName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return null;
        }
        finally
        {
            NativeMethods.CloseDesktop(desktop);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsInfoEx
    {
        public uint Level;
        public WtsInfoExLevel1 Level1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsInfoExLevel1
    {
        public uint SessionId;
        public int SessionState;
        public int SessionFlags;
        // Included solely so natural struct alignment matches the native
        // WTSINFOEX_LEVEL1_W layout (which contains 8-byte LARGE_INTEGER fields).
        public long AlignmentAnchor;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetLastInputInfo(ref LastInputInfo info);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out PointStruct point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
        public static extern int GetWindowText(IntPtr hWnd, [Out] char[] text, int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint desiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseDesktop(IntPtr desktop);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetUserObjectInformation(
            IntPtr handle, int index, [Out] char[] info, uint length, out uint lengthNeeded);

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "WTSQuerySessionInformationW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WTSQuerySessionInformation(
            IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out uint bytesReturned);

        [DllImport("wtsapi32.dll")]
        public static extern void WTSFreeMemory(IntPtr memory);
    }
}
