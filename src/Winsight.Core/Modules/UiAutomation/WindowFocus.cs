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
using System.Runtime.InteropServices;

namespace Winsight.Core.Modules.UiAutomation;

internal sealed record WindowFocusResult(
    bool Success,
    string? Error,
    IntPtr WindowHandle,
    string WindowTitle)
{
    public static WindowFocusResult Ok(IntPtr windowHandle, string windowTitle) =>
        new(true, null, windowHandle, windowTitle);

    public static WindowFocusResult Failure(string error, IntPtr windowHandle = default, string windowTitle = "") =>
        new(false, error, windowHandle, windowTitle);
}

internal static class WindowFocus
{
    private const int SW_RESTORE = 9;
    private const int FocusTimeoutMs = 1000;
    private const int FocusPollIntervalMs = 25;

    internal static WindowFocusResult FocusProcessWindow(int processId, string? windowTitle)
    {
        List<(IntPtr Handle, string Title)> windows =
            NativeWindowEnumerator.VisibleTopLevelWindows(processId);
        List<(IntPtr Handle, string Title)> candidates = string.IsNullOrEmpty(windowTitle)
            ? windows
            : windows
                .Where(w => w.Title.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (candidates.Count == 0)
        {
            if (string.IsNullOrEmpty(windowTitle) || windows.Count == 0)
            {
                return WindowFocusResult.Failure(
                    $"No top-level window found for process {processId}.");
            }

            string titles = string.Join(", ", windows.Select(w => $"\"{w.Title}\""));
            return WindowFocusResult.Failure(
                $"No window of process {processId} has a title containing \"{windowTitle}\". " +
                $"Available titles: {titles}.");
        }

        (IntPtr handle, string title) = candidates[0];
        return FocusWindow(handle, title, processId);
    }

    internal static WindowFocusResult FocusWindow(
        IntPtr windowHandle,
        string windowTitle,
        int processId)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return WindowFocusResult.Failure(
                $"Cannot focus window \"{windowTitle}\" in process {processId}: native handle is zero.",
                windowHandle,
                windowTitle);
        }

        uint currentThread = NativeMethods.GetCurrentThreadId();
        uint targetThread = NativeMethods.GetWindowThreadProcessId(windowHandle, out _);
        IntPtr initialForeground = NativeMethods.GetForegroundWindow();
        uint foregroundThread = initialForeground == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(initialForeground, out _);
        bool attachedTarget = targetThread != 0 &&
            targetThread != currentThread &&
            NativeMethods.AttachThreadInput(currentThread, targetThread, attach: true);
        bool attachedForeground = foregroundThread != 0 &&
            foregroundThread != currentThread &&
            foregroundThread != targetThread &&
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, attach: true);
        bool setForegroundRequested;
        try
        {
            NativeMethods.ShowWindow(windowHandle, SW_RESTORE);
            NativeMethods.BringWindowToTop(windowHandle);
            setForegroundRequested = NativeMethods.SetForegroundWindow(windowHandle);
            NativeMethods.SetFocus(windowHandle);
        }
        finally
        {
            if (attachedForeground)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, attach: false);
            }
            if (attachedTarget)
            {
                NativeMethods.AttachThreadInput(currentThread, targetThread, attach: false);
            }
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds <= FocusTimeoutMs)
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == windowHandle)
            {
                return WindowFocusResult.Ok(windowHandle, windowTitle);
            }
            Thread.Sleep(FocusPollIntervalMs);
        }

        IntPtr currentForeground = NativeMethods.GetForegroundWindow();
        return WindowFocusResult.Failure(
            $"Focus did not move to window 0x{windowHandle.ToInt64():x} " +
            $"(\"{windowTitle}\", process {processId}) after {FocusTimeoutMs} ms. " +
            $"SetForegroundWindow returned {setForegroundRequested}; current foreground is " +
            $"0x{currentForeground.ToInt64():x}.",
            windowHandle,
            windowTitle);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
