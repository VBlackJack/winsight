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
using System.Drawing;
using System.Runtime.InteropServices;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Implements the four Windows synthetic pointer move methods and the
/// <c>diagnose_pointer</c> probe that tests each method in order while
/// restoring the original cursor position after each attempt.
/// </summary>
internal static class PointerDiagnostics
{
    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventVirtualDesk = 0x4000;
    private const uint MouseEventAbsolute = 0x8000;

    private const int SystemMetricCxScreen = 0;
    private const int SystemMetricCyScreen = 1;

    private const uint CursorShowingFlag = 0x00000001;
    private const uint CursorSuppressedFlag = 0x00000002;

    private const uint SpiGetMouse = 0x0003;
    private const uint SpiGetMouseSpeed = 0x0070;

    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMButton = 0x04;
    private const int VkXButton1 = 0x05;
    private const int VkXButton2 = 0x06;

    internal static PointerMoveResult MoveWithMethod(Point target, Point current, string method) =>
        method switch
        {
            PointerMoveMethods.SendInputPrimaryAbsolute => MoveSendInputPrimaryAbsolute(target),
            PointerMoveMethods.SendInputRelative => MoveSendInputRelative(target, current),
            PointerMoveMethods.SetCursorPos => MoveSetCursorPos(target),
            _ => MoveSendInputVirtualDesk(target),
        };

    internal static PointerMoveResult MoveSendInputVirtualDesk(Point target)
    {
        VirtualDesktopBounds desktop = VirtualDesktopCoordinates.QueryCurrent();
        NormalizedAbsolutePoint normalized = VirtualDesktopCoordinates.Normalize(target.X, target.Y, desktop);
        return SendMouseInput(
            normalized.Dx,
            normalized.Dy,
            MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk,
            desktop,
            PointerMoveMethods.SendInputVirtualDesk);
    }

    internal static PointerMoveResult MoveSendInputPrimaryAbsolute(Point target)
    {
        VirtualDesktopBounds primary = QueryPrimaryMonitorBounds();
        NormalizedAbsolutePoint normalized = VirtualDesktopCoordinates.Normalize(target.X, target.Y, primary);
        return SendMouseInput(
            normalized.Dx,
            normalized.Dy,
            MouseEventMove | MouseEventAbsolute,
            primary,
            PointerMoveMethods.SendInputPrimaryAbsolute);
    }

    internal static PointerMoveResult MoveSendInputRelative(Point target, Point current)
    {
        VirtualDesktopBounds desktop = VirtualDesktopCoordinates.QueryCurrent();
        int dx = target.X - current.X;
        int dy = target.Y - current.Y;
        return SendMouseInput(
            dx,
            dy,
            MouseEventMove,
            desktop,
            PointerMoveMethods.SendInputRelative);
    }

    internal static PointerMoveResult MoveSetCursorPos(Point target)
    {
        VirtualDesktopBounds desktop = VirtualDesktopCoordinates.QueryCurrent();
        bool ok = NativeMethods.SetCursorPos(target.X, target.Y);
        int lastError = ok ? 0 : Marshal.GetLastWin32Error();
        return new PointerMoveResult(ok ? 1u : 0u, lastError, target.X, target.Y, desktop, PointerMoveMethods.SetCursorPos);
    }

    internal static CursorPoint? ReadCursorPos() =>
        NativeMethods.GetCursorPos(out NativePoint pt) ? new CursorPoint(pt.X, pt.Y) : null;

    internal static CursorPoint? ReadPhysicalCursorPos() =>
        NativeMethods.GetPhysicalCursorPos(out NativePoint pt) ? new CursorPoint(pt.X, pt.Y) : null;

    internal static VirtualDesktopBounds QueryPrimaryMonitorBounds()
    {
        int width = NativeMethods.GetSystemMetrics(SystemMetricCxScreen);
        int height = NativeMethods.GetSystemMetrics(SystemMetricCyScreen);
        return new VirtualDesktopBounds(0, 0, Math.Max(1, width), Math.Max(1, height));
    }

    internal static PointerClipRect? ReadClipCursor()
    {
        if (!NativeMethods.GetClipCursor(out NativeRect rect))
        {
            return null;
        }
        return new PointerClipRect(
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top);
    }

    internal static CursorStateInfo? ReadCursorInfo()
    {
        NativeCursorInfo info = new() { Size = (uint)Marshal.SizeOf<NativeCursorInfo>() };
        if (!NativeMethods.GetCursorInfo(ref info))
        {
            return null;
        }
        return DecodeCursorInfo(info.Flags, info.ScreenPos.X, info.ScreenPos.Y);
    }

    internal static CursorStateInfo DecodeCursorInfo(uint flags, int x, int y) =>
        new(
            flags,
            Showing: (flags & CursorShowingFlag) != 0,
            Suppressed: (flags & CursorSuppressedFlag) != 0,
            x,
            y);

    internal static IReadOnlyList<string> ReadMouseButtonsDown()
    {
        List<string> down = new();
        if ((NativeMethods.GetAsyncKeyState(VkLButton) & 0x8000) != 0) down.Add("LBUTTON");
        if ((NativeMethods.GetAsyncKeyState(VkRButton) & 0x8000) != 0) down.Add("RBUTTON");
        if ((NativeMethods.GetAsyncKeyState(VkMButton) & 0x8000) != 0) down.Add("MBUTTON");
        if ((NativeMethods.GetAsyncKeyState(VkXButton1) & 0x8000) != 0) down.Add("XBUTTON1");
        if ((NativeMethods.GetAsyncKeyState(VkXButton2) & 0x8000) != 0) down.Add("XBUTTON2");
        return down;
    }

    internal static MouseAccelerationInfo? ReadMouseAcceleration()
    {
        int[] mouseParams = new int[3];
        bool gotMouse = NativeMethods.SystemParametersInfo(SpiGetMouse, 0, mouseParams, 0);
        int speed = 0;
        bool gotSpeed = NativeMethods.SystemParametersInfo(SpiGetMouseSpeed, 0, ref speed, 0);
        if (!gotMouse && !gotSpeed)
        {
            return null;
        }
        return new MouseAccelerationInfo(mouseParams[0], mouseParams[1], mouseParams[2], speed);
    }

    internal static ForegroundProcessInfo? ReadForegroundProcess()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }
        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        string name = string.Empty;
        if (pid > 0)
        {
            try
            {
                using Process proc = Process.GetProcessById((int)pid);
                name = proc.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                name = string.Empty;
            }
        }
        return new ForegroundProcessInfo(hwnd.ToInt64(), (int)pid, name);
    }

    /// <summary>
    /// Try all four pointer movement methods against <paramref name="target"/>,
    /// recording immediate, 50 ms, and 150 ms positions and restoring the
    /// original cursor position after each attempt.
    /// </summary>
    internal static PointerDiagnosticReport RunDiagnostic(Point target)
    {
        InputContextSnapshot context = InputContext.Capture();
        if (!context.DesktopMatchesInput)
        {
            DesktopSwitchAttempt attempt = InputContext.TryAttachToInputDesktop(context);
            if (attempt.Succeeded)
            {
                context = InputContext.Capture();
            }
        }

        CursorPoint? startLogical = ReadCursorPos();
        CursorPoint? startPhysical = ReadPhysicalCursorPos();
        PointerClipRect? clip = ReadClipCursor();
        CursorStateInfo? cursorInfo = ReadCursorInfo();
        IReadOnlyList<string> buttonsDown = ReadMouseButtonsDown();
        MouseAccelerationInfo? accel = ReadMouseAcceleration();
        ForegroundProcessInfo? foreground = ReadForegroundProcess();
        VirtualDesktopBounds virtualDesktop = VirtualDesktopCoordinates.QueryCurrent();
        VirtualDesktopBounds primaryMonitor = QueryPrimaryMonitorBounds();
        string dpiAwareness = ProcessDpiAwareness.DescribeCurrent();

        List<PointerMethodAttempt> attempts = new(PointerMoveMethods.AllOrdered.Count);
        string? firstWorking = null;

        foreach (string method in PointerMoveMethods.AllOrdered)
        {
            CursorPoint? preAttempt = ReadCursorPos() ?? startLogical;
            Point currentPoint = preAttempt is not null ? new Point(preAttempt.X, preAttempt.Y) : Point.Empty;

            PointerMoveResult move = MoveWithMethod(target, currentPoint, method);
            CursorPoint? immediate = ReadCursorPos();
            CursorPoint? physImmediate = ReadPhysicalCursorPos();

            Thread.Sleep(50);
            CursorPoint? after50 = ReadCursorPos();
            CursorPoint? physAfter50 = ReadPhysicalCursorPos();

            Thread.Sleep(100);
            CursorPoint? after150 = ReadCursorPos();
            CursorPoint? physAfter150 = ReadPhysicalCursorPos();

            bool reached = after150 is not null &&
                Math.Abs(after150.X - target.X) <= PointerDispatch.CursorTolerancePx &&
                Math.Abs(after150.Y - target.Y) <= PointerDispatch.CursorTolerancePx;
            if (reached && firstWorking is null)
            {
                firstWorking = method;
            }

            bool restored = RestoreCursor(startLogical);
            attempts.Add(new PointerMethodAttempt(
                method,
                move.SendInputCount,
                move.LastWin32Error,
                immediate,
                physImmediate,
                after50,
                physAfter50,
                after150,
                physAfter150,
                reached,
                restored));
        }

        return new PointerDiagnosticReport(
            target.X,
            target.Y,
            startLogical,
            startPhysical,
            clip,
            cursorInfo,
            buttonsDown,
            accel,
            foreground,
            new WindowBounds(virtualDesktop.Left, virtualDesktop.Top, virtualDesktop.Width, virtualDesktop.Height),
            new WindowBounds(primaryMonitor.Left, primaryMonitor.Top, primaryMonitor.Width, primaryMonitor.Height),
            dpiAwareness,
            context,
            attempts,
            firstWorking);
    }

    private static bool RestoreCursor(CursorPoint? origin)
    {
        if (origin is null)
        {
            return false;
        }
        Point target = new(origin.X, origin.Y);
        _ = MoveSetCursorPos(target);
        CursorPoint? now = ReadCursorPos();
        if (now is not null &&
            Math.Abs(now.X - origin.X) <= PointerDispatch.CursorTolerancePx &&
            Math.Abs(now.Y - origin.Y) <= PointerDispatch.CursorTolerancePx)
        {
            return true;
        }
        _ = MoveSendInputVirtualDesk(target);
        now = ReadCursorPos();
        return now is not null &&
            Math.Abs(now.X - origin.X) <= PointerDispatch.CursorTolerancePx &&
            Math.Abs(now.Y - origin.Y) <= PointerDispatch.CursorTolerancePx;
    }

    private static PointerMoveResult SendMouseInput(
        int dx,
        int dy,
        uint flags,
        VirtualDesktopBounds bounds,
        string method)
    {
        NativeInput[] inputs =
        {
            new()
            {
                Type = InputMouse,
                Data = new NativeInputUnion
                {
                    Mouse = new NativeMouseInput
                    {
                        Dx = dx,
                        Dy = dy,
                        Flags = flags,
                    },
                },
            },
        };
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        int lastError = sent == inputs.Length ? 0 : Marshal.GetLastWin32Error();
        return new PointerMoveResult(sent, lastError, dx, dy, bounds, method);
    }

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
    private struct NativeCursorInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr CursorHandle;
        public NativePoint ScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        public NativeMouseInput Mouse;

        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, NativeInput[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPhysicalCursorPos(out NativePoint point);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClipCursor(out NativeRect rect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorInfo(ref NativeCursorInfo info);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint action, uint param, int[] pvParam, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint action, uint param, ref int pvParam, uint winIni);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
