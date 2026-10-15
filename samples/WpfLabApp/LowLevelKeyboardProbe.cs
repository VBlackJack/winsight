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

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Winsight.Samples.WpfLab;

/// <summary>
/// One keyboard event observed by the WH_KEYBOARD_LL hook, with the modifier
/// state read through GetAsyncKeyState at the time the hook ran.
/// </summary>
internal sealed record HookKeyEvent(
    bool IsKeyDown,
    int VirtualKey,
    int ScanCode,
    bool IsExtended,
    bool IsInjected,
    ModifierKeys AsyncModifiers);

/// <summary>
/// WH_KEYBOARD_LL probe that reads modifiers exactly the way a production
/// shell does: GetAsyncKeyState inside the hook callback, filtered to the
/// moments when this process owns the foreground window. It never absorbs a
/// key, so the WPF and window-message paths observe the same stroke. The
/// owner window removes it with <see cref="Uninstall"/> when it closes.
/// </summary>
internal sealed class LowLevelKeyboardProbe
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;
    private const int KeyDownStateMask = 0x8000;
    private const uint LlkhfExtended = 0x01;
    private const uint LlkhfInjected = 0x10;

    private readonly Action<HookKeyEvent> _onKey;
    private readonly HookProc _hookProc;
    private readonly uint _ownProcessId;
    private IntPtr _hookHandle;

    public LowLevelKeyboardProbe(Action<HookKeyEvent> onKey)
    {
        _onKey = onKey;
        _hookProc = HookCallback;
        _ownProcessId = (uint)Environment.ProcessId;
        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install the low-level keyboard probe.");
        }
    }

    public void Uninstall()
    {
        IntPtr hookHandle = _hookHandle;
        _hookHandle = IntPtr.Zero;
        if (hookHandle != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(hookHandle);
        }
    }

    internal static ModifierKeys ReadAsyncModifiers()
    {
        ModifierKeys modifiers = ModifierKeys.None;
        if (IsAsyncKeyDown(VkControl))
        {
            modifiers |= ModifierKeys.Control;
        }
        if (IsAsyncKeyDown(VkShift))
        {
            modifiers |= ModifierKeys.Shift;
        }
        if (IsAsyncKeyDown(VkMenu))
        {
            modifiers |= ModifierKeys.Alt;
        }
        if (IsAsyncKeyDown(VkLwin) || IsAsyncKeyDown(VkRwin))
        {
            modifiers |= ModifierKeys.Windows;
        }
        return modifiers;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        int message = wParam.ToInt32();
        bool isKeyMessage = message is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp;
        if (nCode >= 0 && isKeyMessage && IsOwnProcessForeground())
        {
            KbdLlHookStruct info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            _onKey(new HookKeyEvent(
                IsKeyDown: message is WmKeyDown or WmSysKeyDown,
                VirtualKey: (int)info.VkCode,
                ScanCode: (int)info.ScanCode,
                IsExtended: (info.Flags & LlkhfExtended) != 0,
                IsInjected: (info.Flags & LlkhfInjected) != 0,
                AsyncModifiers: ReadAsyncModifiers()));
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private bool IsOwnProcessForeground()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }
        _ = GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
        return foregroundProcessId == _ownProcessId;
    }

    private static bool IsAsyncKeyDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & KeyDownStateMask) != 0;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KbdLlHookStruct
    {
        public readonly uint VkCode;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
