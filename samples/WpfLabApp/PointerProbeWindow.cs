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
/// the previous pointer position. Like a GPU UI it also draws its own text: a
/// status line OCR can read, and after each left press a short animation
/// (a block moving for <see cref="AnimationFrames"/> frames) that ends with the
/// status "Clicked N". Characters arrive through WM_CHAR (Ctrl+V pastes the
/// clipboard text) and are reported as the typed text. Wheel turns are summed
/// per axis, and the frame-latched hover is reported whenever it moves. The
/// owner disposes it on close.
/// </summary>
internal sealed class PointerProbeWindow : IDisposable
{
    public const string WindowTitle = "Pointer Probe";
    private const string ClassName = "WinsightPointerProbe";
    private const int FrameIntervalMs = 50;
    private const nuint FrameTimerId = 1;
    public const string InitialStatus = "Status: ready";
    public const string ClickedStatusPrefix = "Clicked ";
    public const int AnimationFrames = 12;
    private const int TextMargin = 20;
    private const int StatusHeight = 50;
    private const int FontHeight = 28;
    private const int FontWeightBold = 700;
    private const string FontFace = "Segoe UI";
    private const int BlockTop = 90;
    private const int BlockSize = 30;
    private const int BlockStep = 24;
    private const char PasteControl = (char)0x16;
    private const char Backspace = '\b';
    private const char CarriageReturn = '\r';
    private const char FirstPrintable = ' ';

    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsVisible = 0x10000000;
    private const uint CsDoubleClicks = 0x0008;
    private const int ColorWindow = 5;
    private const int IdcArrow = 32512;
    private const uint WmDestroy = 0x0002;
    private const uint WmPaint = 0x000F;
    private const uint WmChar = 0x0102;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmMouseHorizontalWheel = 0x020E;
    private const uint CfUnicodeText = 13;
    private const int BlackBrush = 4;
    private const int Transparent = 1;
    private const uint DrawTextSingleLine = 0x0020;
    private const uint DrawTextVerticalCenter = 0x0004;
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
    private readonly Action<string> _onText;
    private readonly Action<int, int> _onWheel;
    private readonly Action<int, int> _onHover;
    private int _verticalWheel;
    private int _horizontalWheel;
    private readonly System.Text.StringBuilder _typed = new();
    private IntPtr _handle;
    private IntPtr _font;
    private NativePoint _lastMove;
    private NativePoint _hover;
    private string _status = InitialStatus;
    private int _clicks;
    private int _animationLeft;

    private PointerProbeWindow(
        Action<ProbePress> onPress, Action<string> onText, Action<int, int> onWheel, Action<int, int> onHover)
    {
        _onPress = onPress;
        _onText = onText;
        _onWheel = onWheel;
        _onHover = onHover;
    }

    public IntPtr Handle => _handle;

    /// <summary>Create and show the probe at the given screen rectangle, in the foreground.</summary>
    public static PointerProbeWindow Open(
        int left, int top, int width, int height, Action<ProbePress> onPress, Action<string> onText,
        Action<int, int> onWheel, Action<int, int> onHover)
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

        PointerProbeWindow probe = new(onPress, onText, onWheel, onHover)
        {
            _font = CreateFont(FontHeight, 0, 0, 0, FontWeightBold, 0, 0, 0, 0, 0, 0, 0, 0, FontFace),
        };
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
            probe.OnMessage(message, wParam, lParam);
            if (message == WmDestroy)
            {
                KillTimer(hwnd, FrameTimerId);
                DeleteObject(probe._font);
                s_instances.Remove(hwnd);
                probe._handle = IntPtr.Zero;
            }
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void OnMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmMouseMove:
                _lastMove = ToScreen(lParam);
                break;
            case WmTimer:
                if (_hover.X != _lastMove.X || _hover.Y != _lastMove.Y)
                {
                    _hover = _lastMove;
                    _onHover(_hover.X, _hover.Y);
                }
                AdvanceAnimation();
                break;
            case WmMouseWheel:
                _verticalWheel += WheelDelta(wParam);
                _onWheel(_verticalWheel, _horizontalWheel);
                break;
            case WmMouseHorizontalWheel:
                _horizontalWheel += WheelDelta(wParam);
                _onWheel(_verticalWheel, _horizontalWheel);
                break;
            case WmPaint:
                Paint();
                break;
            case WmChar:
                OnChar((char)wParam.ToInt64());
                break;
            case WmLeftButtonDown:
                _clicks++;
                _animationLeft = AnimationFrames;
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

    // The wheel distance is the signed high word of wParam.
    private static int WheelDelta(IntPtr wParam) => (short)((wParam.ToInt64() >> HighWordShift) & LowWordMask);

    private void AdvanceAnimation()
    {
        if (_animationLeft == 0)
        {
            return;
        }
        _animationLeft--;
        if (_animationLeft == 0)
        {
            _status = ClickedStatusPrefix + _clicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        InvalidateRect(_handle, IntPtr.Zero, true);
    }

    private void Paint()
    {
        IntPtr dc = BeginPaint(_handle, out PaintStruct paint);
        try
        {
            GetClientRect(_handle, out NativeRect client);
            NativeRect statusRect = new()
            {
                Left = TextMargin,
                Top = TextMargin,
                Right = client.Right - TextMargin,
                Bottom = TextMargin + StatusHeight,
            };
            IntPtr previousFont = SelectObject(dc, _font);
            _ = SetBkMode(dc, Transparent);
            _ = DrawText(dc, _status, -1, ref statusRect, DrawTextSingleLine | DrawTextVerticalCenter);
            SelectObject(dc, previousFont);
            if (_animationLeft > 0)
            {
                int left = TextMargin + (AnimationFrames - _animationLeft) * BlockStep;
                NativeRect block = new() { Left = left, Top = BlockTop, Right = left + BlockSize, Bottom = BlockTop + BlockSize };
                _ = FillRect(dc, ref block, GetStockObject(BlackBrush));
            }
        }
        finally
        {
            EndPaint(_handle, ref paint);
        }
    }

    private void OnChar(char character)
    {
        switch (character)
        {
            case PasteControl:
                _typed.Append(ReadClipboardText());
                break;
            case Backspace:
                if (_typed.Length > 0)
                {
                    _typed.Length--;
                }
                break;
            case CarriageReturn:
                _typed.Append('\n');
                break;
            default:
                if (character >= FirstPrintable || character == '\t')
                {
                    _typed.Append(character);
                }
                break;
        }
        _onText(_typed.ToString());
    }

    private string ReadClipboardText()
    {
        if (!OpenClipboard(_handle))
        {
            return string.Empty;
        }
        try
        {
            IntPtr data = GetClipboardData(CfUnicodeText);
            IntPtr text = data == IntPtr.Zero ? IntPtr.Zero : GlobalLock(data);
            if (text == IntPtr.Zero)
            {
                return string.Empty;
            }
            try
            {
                return Marshal.PtrToStringUni(text) ?? string.Empty;
            }
            finally
            {
                GlobalUnlock(data);
            }
        }
        finally
        {
            CloseClipboard();
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr DeviceContext;
        public int Erase;
        public NativeRect Paint;
        public int Restore;
        public int IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Reserved;
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

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, out PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr dc, ref NativeRect rect, IntPtr brush);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DrawTextW")]
    private static extern int DrawText(IntPtr dc, string text, int length, ref NativeRect rect, uint format);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int index);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFontW")]
    private static extern IntPtr CreateFont(
        int height, int width, int escapement, int orientation, int weight, uint italic, uint underline,
        uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision, uint quality,
        uint pitchAndFamily, string face);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
