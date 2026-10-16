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

namespace Winsight.Samples.WpfLab;

/// <summary>Which button press the pointer probe observed.</summary>
internal enum ProbeButton
{
    Left,
    Right,
    Double,
}

/// <summary>
/// One press seen by the pointer probe, in screen pixels: where the button
/// message says it happened, and where the frame-latched hover was when it did.
/// </summary>
internal sealed record ProbePress(ProbeButton Kind, int PressX, int PressY, int HoverX, int HoverY);

/// <summary>
/// A raw Win32 top-level window that behaves like an immediate-mode GPU UI
/// (iced on winit): it exposes no UI Automation children, it remembers only the
/// position of the last WM_MOUSEMOVE, and it resolves hover once per frame. A
/// press acts on the frame-latched hover, not on the coordinates carried by the
/// button message, so a click whose press lands before the next frame acts on
/// the previous pointer position. The owner disposes it on close.
/// </summary>
internal sealed class PointerProbeWindow : IDisposable
{
    public const string WindowTitle = "Pointer Probe";
    private const string ClassName = "WinsightPointerProbe";
    private const int FrameIntervalMs = 50;
    private const nuint FrameTimerId = 1;

    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsVisible = 0x10000000;
    private const uint CsDoubleClicks = 0x0008;
    private const int ColorWindow = 5;
    private const int IdcArrow = 32512;
    private const uint WmDestroy = 0x0002;
    private const uint WmTimer = 0x0113;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmLeftButtonDoubleClick = 0x0203;
    private const uint WmRightButtonDown = 0x0204;
    private const int LowWordMask = 0xFFFF;
    private const int HighWordShift = 16;

    private static readonly WndProc s_windowProc = StaticWindowProc;
    private static readonly Dictionary<IntPtr, PointerProbeWindow> s_instances = new();
    private static bool s_classRegistered;

    private readonly Action<ProbePress> _onPress;
    private IntPtr _handle;
    private NativePoint _lastMove;
    private NativePoint _hover;

    private PointerProbeWindow(Action<ProbePress> onPress)
    {
        _onPress = onPress;
    }

    public IntPtr Handle => _handle;

    /// <summary>Create and show the probe at the given screen rectangle, in the foreground.</summary>
    public static PointerProbeWindow Open(int left, int top, int width, int height, Action<ProbePress> onPress)
    {
        IntPtr module = GetModuleHandle(null);
        if (!s_classRegistered)
        {
            WindowClassEx windowClass = new()
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                Style = CsDoubleClicks,
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(s_windowProc),
                Instance = module,
                Cursor = LoadCursor(IntPtr.Zero, IdcArrow),
                Background = new IntPtr(ColorWindow + 1),
                ClassName = ClassName,
            };
            if (RegisterClassEx(ref windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            s_classRegistered = true;
        }

        PointerProbeWindow probe = new(onPress);
        IntPtr handle = CreateWindowEx(
            0, ClassName, WindowTitle, WsOverlappedWindow | WsVisible,
            left, top, width, height, IntPtr.Zero, IntPtr.Zero, module, IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        probe._handle = handle;
        s_instances[handle] = probe;
        SetTimer(handle, FrameTimerId, FrameIntervalMs, IntPtr.Zero);
        SetForegroundWindow(handle);
        return probe;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            DestroyWindow(_handle);
        }
    }

    private static IntPtr StaticWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (s_instances.TryGetValue(hwnd, out PointerProbeWindow? probe))
        {
            probe.OnMessage(message, lParam);
            if (message == WmDestroy)
            {
                KillTimer(hwnd, FrameTimerId);
                s_instances.Remove(hwnd);
                probe._handle = IntPtr.Zero;
            }
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void OnMessage(uint message, IntPtr lParam)
    {
        switch (message)
        {
            case WmMouseMove:
                _lastMove = ToScreen(lParam);
                break;
            case WmTimer:
                _hover = _lastMove;
                break;
            case WmLeftButtonDown:
                Report(ProbeButton.Left, lParam);
                break;
            case WmLeftButtonDoubleClick:
                Report(ProbeButton.Double, lParam);
                break;
            case WmRightButtonDown:
                Report(ProbeButton.Right, lParam);
                break;
        }
    }

    private void Report(ProbeButton kind, IntPtr lParam)
    {
        NativePoint press = ToScreen(lParam);
        _onPress(new ProbePress(kind, press.X, press.Y, _hover.X, _hover.Y));
    }

    private NativePoint ToScreen(IntPtr lParam)
    {
        long data = lParam.ToInt64();
        NativePoint point = new()
        {
            X = (short)(data & LowWordMask),
            Y = (short)((data >> HighWordShift) & LowWordMask),
        };
        ClientToScreen(_handle, ref point);
        return point;
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern nuint SetTimer(IntPtr hwnd, nuint id, uint elapseMs, IntPtr timerProc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(IntPtr hwnd, nuint id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

    [DllImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static extern IntPtr LoadCursor(IntPtr instance, int cursorName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
