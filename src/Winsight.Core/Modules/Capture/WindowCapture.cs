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

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.Capture;

[SupportedOSPlatform("windows")]
public sealed class WindowCapture
{
    // PW_RENDERFULLCONTENT (Windows 8.1+) renders accelerated surfaces correctly,
    // including DirectX-composed WPF windows. Without it the bitmap may come back black.
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    public ScreenshotResult Capture(int processId, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var hwnd = ResolveWindowHandle(processId);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"No top-level window found for process {processId}.");
        }

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

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
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
            CapturedAtUtc: DateTime.UtcNow);
    }

    /// <summary>
    /// Captures the window's client area only (no title bar or borders), for
    /// deterministic visual-regression baselines: the OS chrome varies with theme,
    /// DPI, and focus, which would otherwise produce false diffs.
    /// </summary>
    public ScreenshotResult CaptureClientArea(int processId, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        IntPtr hwnd = ResolveWindowHandle(processId);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"No top-level window found for process {processId}.");
        }

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

    private static IntPtr ResolveWindowHandle(int processId)
    {
        try
        {
            using var automation = new UIA3Automation();
            AutomationElement desktop = automation.GetDesktop();
            AutomationElement? window = desktop.FindAllChildren()
                .FirstOrDefault(e => e.Properties.ProcessId.ValueOrDefault == processId);
            if (window is null)
            {
                return IntPtr.Zero;
            }

            return new IntPtr(window.Properties.NativeWindowHandle.ValueOrDefault.ToInt64());
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            throw new InvalidOperationException(
                $"Could not enumerate top-level windows while resolving process {processId}.",
                ex);
        }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
