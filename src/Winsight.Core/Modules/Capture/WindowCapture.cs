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

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
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

    private static IntPtr ResolveWindowHandle(int processId)
    {
        using var automation = new UIA3Automation();
        var desktop = automation.GetDesktop();
        var window = desktop.FindAllChildren()
            .FirstOrDefault(e => e.Properties.ProcessId.ValueOrDefault == processId);
        if (window is null)
        {
            return IntPtr.Zero;
        }
        return new IntPtr(window.Properties.NativeWindowHandle.ValueOrDefault.ToInt64());
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hwnd, out Rect lpRect);
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
