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
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;

namespace Winsight.Core.Modules.Capture;

[SupportedOSPlatform("windows")]
public sealed class WindowCapture
{
    // PW_RENDERFULLCONTENT (Windows 8.1+) renders accelerated surfaces correctly,
    // including DirectX-composed WPF windows. Without it the bitmap may come back black.
    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;
    private const uint GW_HWNDPREV = 3;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_LAYERED = 0x00080000L;
    private const uint LWA_ALPHA = 0x00000002;
    private const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint DWMWA_CLOAKED = 14;
    private const int MaxTitleLength = 256;
    private const int MaxZOrderWalk = 4096;

    /// <summary>
    /// Capture a process window. Without <paramref name="windowTitle"/> this is
    /// the topmost window that is not a transient overlay (an open tooltip or
    /// menu is skipped), or the process main window when
    /// <paramref name="mainWindow"/> is set.
    /// </summary>
    public ScreenshotResult Capture(
        int processId,
        string outputPath,
        string? windowTitle = null,
        bool mainWindow = false,
        CaptureSource captureSource = CaptureSource.Window,
        bool allowOccluded = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        IntPtr hwnd = ResolveWindowHandle(
            processId,
            windowTitle,
            mainWindow ? DefaultWindowSelection.MainWindow : DefaultWindowSelection.TopmostVisible);

        if (!NativeMethods.GetWindowRect(hwnd, out Rect rect))
        {
            throw new InvalidOperationException(
                $"GetWindowRect failed for window 0x{hwnd.ToInt64():x} (process {processId}).");
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                $"Window has invalid dimensions: {width}x{height}.");
        }

        if (captureSource == CaptureSource.Screen)
        {
            ValidateScreenCapture(hwnd, processId, allowOccluded);
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            if (captureSource == CaptureSource.Screen)
            {
                CaptureScreenRect(graphics, rect.Left, rect.Top, width, height);
            }
            else
            {
                CaptureWindowHwnd(graphics, hwnd);
            }
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        bitmap.Save(outputPath, ImageFormat.Png);

        return new ScreenshotResult(
            Path: outputPath,
            Width: width,
            Height: height,
            CapturedAtUtc: DateTime.UtcNow,
            Window: new WindowIdentity(processId, hwnd.ToInt64(), new WindowBounds(rect.Left, rect.Top, width, height)),
            CaptureSource: CaptureSourceParser.ToWireValue(captureSource));
    }

    private static void CaptureWindowHwnd(Graphics graphics, IntPtr hwnd)
    {
        var hdc = graphics.GetHdc();
        try
        {
            if (!NativeMethods.PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT))
            {
                var win32Error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"PrintWindow returned false (Win32 error 0x{win32Error:x8}={win32Error}). " +
                    "Window may be minimized, occluded, or use a composition surface that does not " +
                    "support PrintWindow (e.g. WinUI3 with DesktopChildSiteBridge).");
            }
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }

    private static void CaptureScreenRect(Graphics graphics, int left, int top, int width, int height)
    {
        IntPtr screenHdc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenHdc == IntPtr.Zero)
        {
            int win32Error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"GetDC(NULL) failed (Win32 error 0x{win32Error:x8}={win32Error}).");
        }

        try
        {
            IntPtr destHdc = graphics.GetHdc();
            try
            {
                if (!NativeMethods.BitBlt(
                        destHdc,
                        0,
                        0,
                        width,
                        height,
                        screenHdc,
                        left,
                        top,
                        SRCCOPY | CAPTUREBLT))
                {
                    int win32Error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(
                        $"BitBlt from screen DC failed (Win32 error 0x{win32Error:x8}={win32Error}).");
                }
            }
            finally
            {
                graphics.ReleaseHdc(destHdc);
            }
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(IntPtr.Zero, screenHdc);
        }
    }

    private static void ValidateScreenCapture(IntPtr targetHwnd, int targetProcessId, bool allowOccluded)
    {
        OcclusionWindow target = ReadOcclusionWindow(targetHwnd, targetProcessId);
        List<OcclusionWindow> above = new();

        if (!allowOccluded && target.IsVisible && !target.IsMinimized && !target.IsCloaked)
        {
            HashSet<IntPtr> visited = new() { targetHwnd };
            IntPtr current = NativeMethods.GetWindow(targetHwnd, GW_HWNDPREV);
            int steps = 0;
            while (current != IntPtr.Zero && steps < MaxZOrderWalk && visited.Add(current))
            {
                steps++;
                uint threadId = NativeMethods.GetWindowThreadProcessId(current, out uint ownerPid);
                if (threadId != 0 && NativeMethods.IsWindowVisible(current))
                {
                    above.Add(ReadOcclusionWindow(current, unchecked((int)ownerPid)));
                }
                current = NativeMethods.GetWindow(current, GW_HWNDPREV);
            }
        }

        OcclusionCheckResult check = ScreenCapturePolicy.Evaluate(target, above, allowOccluded);
        if (!check.Allowed)
        {
            throw new InvalidOperationException(check.Reason);
        }
    }

    private static OcclusionWindow ReadOcclusionWindow(IntPtr hwnd, int processId)
    {
        bool isVisible = NativeMethods.IsWindowVisible(hwnd);
        bool isMinimized = NativeMethods.IsIconic(hwnd);
        bool isCloaked = IsWindowCloaked(hwnd);
        bool isZeroAlpha = IsZeroAlphaLayered(hwnd);
        WindowBounds bounds = ReadVisibleBounds(hwnd);
        string title = ReadWindowTitle(hwnd);
        return new OcclusionWindow(
            Handle: hwnd.ToInt64(),
            ProcessId: processId,
            Title: title,
            Bounds: bounds,
            IsVisible: isVisible,
            IsMinimized: isMinimized,
            IsCloaked: isCloaked,
            IsZeroAlpha: isZeroAlpha);
    }

    private static WindowBounds ReadVisibleBounds(IntPtr hwnd)
    {
        // Use DWM extended frame bounds when available so invisible Win10/11 drop-shadow
        // borders of neighboring windows do not trigger false occlusion errors.
        if (NativeMethods.DwmGetWindowAttribute(
                hwnd,
                DWMWA_EXTENDED_FRAME_BOUNDS,
                out Rect dwmRect,
                Marshal.SizeOf<Rect>()) == 0)
        {
            return new WindowBounds(
                dwmRect.Left,
                dwmRect.Top,
                dwmRect.Right - dwmRect.Left,
                dwmRect.Bottom - dwmRect.Top);
        }

        if (NativeMethods.GetWindowRect(hwnd, out Rect rect))
        {
            return new WindowBounds(
                rect.Left,
                rect.Top,
                rect.Right - rect.Left,
                rect.Bottom - rect.Top);
        }

        return new WindowBounds(0, 0, 0, 0);
    }

    private static bool IsWindowCloaked(IntPtr hwnd)
    {
        return NativeMethods.DwmGetWindowAttribute(
                   hwnd,
                   DWMWA_CLOAKED,
                   out int cloaked,
                   sizeof(int)) == 0 &&
               cloaked != 0;
    }

    private static bool IsZeroAlphaLayered(IntPtr hwnd)
    {
        long exStyle = NativeMethods.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((exStyle & WS_EX_LAYERED) == 0)
        {
            return false;
        }

        return NativeMethods.GetLayeredWindowAttributes(hwnd, out _, out byte alpha, out uint flags) &&
               (flags & LWA_ALPHA) != 0 &&
               alpha == 0;
    }

    private static string ReadWindowTitle(IntPtr hwnd)
    {
        char[] buffer = new char[MaxTitleLength];
        int length = NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    /// <summary>
    /// Capture a process window into memory, cropped to <paramref name="region"/>
    /// (window coordinates) when given. Window selection is the same as
    /// <see cref="Capture"/>. The caller disposes the returned frame.
    /// </summary>
    public CapturedFrame CaptureFrame(int processId, string? windowTitle = null, WindowRegion? region = null)
    {
        IntPtr hwnd = ResolveWindowHandle(processId, windowTitle, DefaultWindowSelection.TopmostVisible);
        if (!NativeMethods.GetWindowRect(hwnd, out Rect rect))
        {
            throw new InvalidOperationException(
                $"GetWindowRect failed for window 0x{hwnd.ToInt64():x} (process {processId}).");
        }
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"Window has invalid dimensions: {width}x{height}.");
        }
        if (WindowRegion.Validate(region, width, height) is { } regionError)
        {
            throw new ArgumentException(regionError, nameof(region));
        }

        Bitmap window = new(width, height, PixelFormat.Format32bppArgb);
        try
        {
            using (Graphics graphics = Graphics.FromImage(window))
            {
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    if (!NativeMethods.PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT))
                    {
                        int win32Error = Marshal.GetLastWin32Error();
                        throw new InvalidOperationException(
                            $"PrintWindow returned false (Win32 error 0x{win32Error:x8}={win32Error}).");
                    }
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }
            WindowRegion used = region ?? new WindowRegion(0, 0, width, height);
            Bitmap frame = region is null
                ? window
                : window.Clone(new Rectangle(used.X, used.Y, used.Width, used.Height), PixelFormat.Format32bppArgb);
            if (!ReferenceEquals(frame, window))
            {
                window.Dispose();
            }
            return new CapturedFrame(
                frame,
                new WindowIdentity(processId, hwnd.ToInt64(), new WindowBounds(rect.Left, rect.Top, width, height)),
                used);
        }
        catch
        {
            window.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Captures the window's client area only (no title bar or borders), for
    /// deterministic visual-regression baselines: the OS chrome varies with theme,
    /// DPI, and focus, which would otherwise produce false diffs.
    /// </summary>
    public ScreenshotResult CaptureClientArea(int processId, string outputPath, string? windowTitle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        IntPtr hwnd = ResolveWindowHandle(
            processId,
            windowTitle,
            DefaultWindowSelection.MainWindow);

        if (!NativeMethods.GetWindowRect(hwnd, out Rect windowRect))
        {
            throw new InvalidOperationException(
                $"GetWindowRect failed for window 0x{hwnd.ToInt64():x} (process {processId}).");
        }
        if (!NativeMethods.GetClientRect(hwnd, out Rect clientRect))
        {
            throw new InvalidOperationException(
                $"GetClientRect failed for window 0x{hwnd.ToInt64():x} (process {processId}).");
        }

        int clientWidth = clientRect.Right - clientRect.Left;
        int clientHeight = clientRect.Bottom - clientRect.Top;
        if (clientWidth <= 0 || clientHeight <= 0)
        {
            throw new InvalidOperationException(
                $"Window client area has invalid dimensions: {clientWidth}x{clientHeight}.");
        }

        // Client-area origin in screen space, then relative to the window origin so it
        // can be cropped out of the full-window PrintWindow bitmap.
        Point clientOrigin = default;
        if (!NativeMethods.ClientToScreen(hwnd, ref clientOrigin))
        {
            throw new InvalidOperationException(
                $"ClientToScreen failed for window 0x{hwnd.ToInt64():x} (process {processId}).");
        }
        int offsetX = clientOrigin.X - windowRect.Left;
        int offsetY = clientOrigin.Y - windowRect.Top;

        int windowWidth = windowRect.Right - windowRect.Left;
        int windowHeight = windowRect.Bottom - windowRect.Top;
        using Bitmap windowBitmap = new(windowWidth, windowHeight, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(windowBitmap))
        {
            IntPtr hdc = graphics.GetHdc();
            try
            {
                if (!NativeMethods.PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT))
                {
                    int win32Error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(
                        $"PrintWindow returned false (Win32 error 0x{win32Error:x8}={win32Error}).");
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        using Bitmap clientBitmap = new(clientWidth, clientHeight, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(clientBitmap))
        {
            Rectangle source = new(offsetX, offsetY, clientWidth, clientHeight);
            graphics.DrawImage(
                windowBitmap,
                new Rectangle(0, 0, clientWidth, clientHeight),
                source,
                GraphicsUnit.Pixel);
        }

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        clientBitmap.Save(outputPath, ImageFormat.Png);

        return new ScreenshotResult(
            Path: outputPath,
            Width: clientWidth,
            Height: clientHeight,
            CapturedAtUtc: DateTime.UtcNow);
    }

    private static IntPtr ResolveWindowHandle(
        int processId,
        string? windowTitle,
        DefaultWindowSelection defaultSelection)
    {
        List<(IntPtr Handle, string Title)> windows;
        try
        {
            windows = NativeWindowEnumerator.VisibleTopLevelWindows(processId);
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            throw new InvalidOperationException(
                $"Could not enumerate top-level windows while resolving process {processId}.",
                ex);
        }

        List<(IntPtr Handle, string Title)> candidates =
            WindowGeometry.FilterByTitle(windows, windowTitle);

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(BuildNoWindowError(processId, windowTitle, windows));
        }

        // EnumWindows returns top-level windows in z-order, topmost first;
        // transient overlays (tooltips, menus) move behind the others.
        return string.IsNullOrWhiteSpace(windowTitle) &&
            defaultSelection == DefaultWindowSelection.MainWindow
            ? NativeWindowEnumerator.PreferMainWindow(processId, candidates)[0].Handle
            : NativeWindowEnumerator.PreferPrimary(candidates)[0].Handle;
    }

    private static string BuildNoWindowError(
        int processId,
        string? windowTitle,
        List<(IntPtr Handle, string Title)> windows)
    {
        if (string.IsNullOrWhiteSpace(windowTitle) || windows.Count == 0)
        {
            return $"No top-level window found for process {processId}.";
        }

        string titles = string.Join(", ", windows.Select(window => $"\"{window.Title}\""));
        return $"No window of process {processId} has a title containing \"{windowTitle}\". " +
            $"Available windows: {titles}.";
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hwnd, out Rect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hwnd, out Rect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr hwnd, ref Point lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BitBlt(
            IntPtr hdcDest,
            int xDest,
            int yDest,
            int width,
            int height,
            IntPtr hdcSrc,
            int xSrc,
            int ySrc,
            uint rop);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hwnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hwnd, [Out] char[] lpString, int nMaxCount);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int nIndex);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetLayeredWindowAttributes(
            IntPtr hwnd,
            out uint pcrKey,
            out byte pbAlpha,
            out uint pdwFlags);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(
            IntPtr hwnd,
            uint dwAttribute,
            out Rect pvAttribute,
            int cbAttribute);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(
            IntPtr hwnd,
            uint dwAttribute,
            out int pvAttribute,
            int cbAttribute);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private static bool IsFatal(Exception ex) =>
        ex is OutOfMemoryException
            or StackOverflowException
            or ThreadAbortException;

    private enum DefaultWindowSelection
    {
        TopmostVisible,
        MainWindow,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
