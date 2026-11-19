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

using System.Runtime.InteropServices;

namespace Winsight.Core.Modules.UserActivity;

/// <summary>
/// Snapshot of the latest input seen by the optional low-level input hook.
/// Contains only timestamps, broad input kind (mouse or keyboard) and whether
/// Windows flagged the event as injected; key codes and text are never stored.
/// </summary>
internal readonly record struct HookInputState(
    bool Active,
    bool HasEvent,
    uint LastEventTick,
    string LastInputKind,
    bool LastEventInjected,
    bool HasHardwareEvent,
    uint LastHardwareTick,
    string LastHardwareKind);

/// <summary>
/// Opt-in WH_MOUSE_LL / WH_KEYBOARD_LL observer controlled by
/// <see cref="InputHookVariable"/>. Off by default because endpoint security
/// products may flag global low-level hooks. When enabled by host configuration,
/// distinguishes real hardware mouse/keyboard input from events injected by
/// other processes via <c>LLMHF_INJECTED</c> / <c>LLKHF_INJECTED</c>.
/// </summary>
internal sealed class LowLevelInputHook : IDisposable
{
    public const string InputHookVariable = "WINSIGHT_INPUT_HOOK";

    internal const uint LlmhfInjected = 0x00000001;
    internal const uint LlmhfLowerIlInjected = 0x00000002;
    internal const uint LlkhfInjected = 0x00000010;
    internal const uint LlkhfLowerIlInjected = 0x00000002;

    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const uint WmQuit = 0x0012;
    private const int HookStartWaitMs = 1_000;
    private const int HookStopWaitMs = 1_000;

    private static readonly Lock SingletonGate = new();
    private static LowLevelInputHook? s_shared;

    private readonly Lock _stateGate = new();
    private readonly ManualResetEventSlim _started = new(false);
    private readonly LowLevelHookProc _mouseProc;
    private readonly LowLevelHookProc _keyboardProc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private bool _active;
    private bool _hasEvent;
    private uint _lastEventTick;
    private string _lastInputKind = UserActivityMonitor.KindUnknown;
    private bool _lastEventInjected;
    private bool _hasHardwareEvent;
    private uint _lastHardwareTick;
    private string _lastHardwareKind = UserActivityMonitor.KindUnknown;
    private bool _disposed;

    public static bool IsEnabledByEnvironment =>
        IsTruthy(Environment.GetEnvironmentVariable(InputHookVariable));

    public static HookInputState ReadShared()
    {
        if (!IsEnabledByEnvironment)
        {
            return default;
        }
        lock (SingletonGate)
        {
            if (s_shared is null)
            {
                s_shared = new LowLevelInputHook();
                s_shared.Start();
                AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeShared();
            }
            return s_shared.Read();
        }
    }

    internal static void DisposeShared()
    {
        lock (SingletonGate)
        {
            s_shared?.Dispose();
            s_shared = null;
        }
    }

    internal static bool IsMouseInjected(uint flags) =>
        (flags & (LlmhfInjected | LlmhfLowerIlInjected)) != 0;

    internal static bool IsKeyboardInjected(uint flags) =>
        (flags & (LlkhfInjected | LlkhfLowerIlInjected)) != 0;

    internal static bool IsTruthy(string? raw) =>
        string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(raw, "on", StringComparison.OrdinalIgnoreCase);

    private LowLevelInputHook()
    {
        _mouseProc = OnMouseEvent;
        _keyboardProc = OnKeyboardEvent;
    }

    public HookInputState Read()
    {
        lock (_stateGate)
        {
            return new HookInputState(
                _active,
                _hasEvent,
                _lastEventTick,
                _lastInputKind,
                _lastEventInjected,
                _hasHardwareEvent,
                _lastHardwareTick,
                _lastHardwareKind);
        }
    }

    private void Start()
    {
        _thread = new Thread(PumpLoop)
        {
            IsBackground = true,
            Name = "Winsight.InputHook",
        };
        _thread.Start();
        _started.Wait(HookStartWaitMs);
    }

    private void PumpLoop()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        IntPtr module = NativeMethods.GetModuleHandle(null);
        _mouseHook = NativeMethods.SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);
        _keyboardHook = NativeMethods.SetWindowsHookEx(WhKeyboardLl, _keyboardProc, module, 0);

        bool installed = _mouseHook != IntPtr.Zero || _keyboardHook != IntPtr.Zero;
        lock (_stateGate)
        {
            _active = installed;
        }
        _started.Set();

        if (!installed)
        {
            return;
        }

        try
        {
            while (NativeMethods.GetMessage(out Msg _, IntPtr.Zero, 0, 0) > 0)
            {
                // Low-level hooks are dispatched during GetMessage; no window translation needed.
            }
        }
        finally
        {
            lock (_stateGate)
            {
                _active = false;
            }
            if (_mouseHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            if (_keyboardHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
        }
    }

    private IntPtr OnMouseEvent(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && lParam != IntPtr.Zero)
        {
            MsllHookStruct info = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            uint tick = info.Time != 0 ? info.Time : UserActivityMonitor.TickNow();
            RecordEvent(tick, UserActivityMonitor.KindMouse, IsMouseInjected(info.Flags));
        }
        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr OnKeyboardEvent(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && lParam != IntPtr.Zero)
        {
            KbdllHookStruct info = Marshal.PtrToStructure<KbdllHookStruct>(lParam);
            uint tick = info.Time != 0 ? info.Time : UserActivityMonitor.TickNow();
            RecordEvent(tick, UserActivityMonitor.KindKeyboard, IsKeyboardInjected(info.Flags));
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void RecordEvent(uint tick, string kind, bool injected)
    {
        lock (_stateGate)
        {
            _hasEvent = true;
            _lastEventTick = tick;
            _lastInputKind = kind;
            _lastEventInjected = injected;
            if (!injected)
            {
                _hasHardwareEvent = true;
                _lastHardwareTick = tick;
                _lastHardwareKind = kind;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_threadId != 0)
        {
            NativeMethods.PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }
        _thread?.Join(HookStopWaitMs);
        _started.Dispose();
    }

    private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public PointStruct Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdllHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public PointStruct Pt;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
        public static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetMessageW")]
        public static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
