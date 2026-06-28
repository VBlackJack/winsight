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
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Resolves the screen-space bounds of a process window. The window selection
/// mirrors <c>WindowCapture.Capture</c> (topmost visible window, optionally
/// scoped by a title substring), so a point grounded against that capture maps
/// back through the same window origin.
/// </summary>
public static class WindowGeometry
{
    /// <summary>
    /// Resolve the bounds of the topmost visible window of the process, or the
    /// topmost window whose title contains <paramref name="windowTitle"/>.
    /// Returns false with a descriptive <paramref name="error"/> when no
    /// matching window exists or its rectangle cannot be read.
    /// </summary>
    public static bool TryResolveTopmostBounds(
        int processId,
        string? windowTitle,
        out WindowBounds bounds,
        out string? error)
    {
        bounds = default!;
        error = null;

        List<(IntPtr Handle, string Title)> windows =
            NativeWindowEnumerator.VisibleTopLevelWindows(processId);
        List<(IntPtr Handle, string Title)> candidates = FilterByTitle(windows, windowTitle);

        if (candidates.Count == 0)
        {
            error = string.IsNullOrWhiteSpace(windowTitle) || windows.Count == 0
                ? $"No top-level window found for process {processId}."
                : $"No window of process {processId} has a title containing \"{windowTitle}\". " +
                  $"Available windows: {string.Join(", ", windows.Select(w => $"\"{w.Title}\""))}.";
            return false;
        }

        // EnumWindows returns top-level windows in z-order, topmost first.
        IntPtr hwnd = candidates[0].Handle;
        if (!TryReadBounds(hwnd, out bounds))
        {
            error = $"GetWindowRect failed for window 0x{hwnd.ToInt64():x} (process {processId}).";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolve the visible process window that contains an absolute screen point.
    /// For screen-space input this is safer than using the topmost process window:
    /// transient popups can be topmost while the clicked point still belongs to
    /// the main target window.
    /// </summary>
    public static bool TryResolveBoundsContainingPoint(
        int processId,
        string? windowTitle,
        int screenX,
        int screenY,
        out WindowBounds bounds,
        out string? error)
    {
        bounds = default!;
        error = null;

        List<(IntPtr Handle, string Title)> windows =
            NativeWindowEnumerator.VisibleTopLevelWindows(processId);
        List<(IntPtr Handle, string Title)> candidates = FilterByTitle(windows, windowTitle);

        if (candidates.Count == 0)
        {
            error = string.IsNullOrWhiteSpace(windowTitle) || windows.Count == 0
                ? $"No top-level window found for process {processId}."
                : $"No window of process {processId} has a title containing \"{windowTitle}\". " +
                  $"Available windows: {string.Join(", ", windows.Select(w => $"\"{w.Title}\""))}.";
            return false;
        }

        List<WindowBounds> candidateBounds = new(candidates.Count);
        foreach ((IntPtr handle, _) in candidates)
        {
            if (!TryReadBounds(handle, out WindowBounds candidate))
            {
                continue;
            }

            candidateBounds.Add(candidate);
            if (ContainsScreenPoint(candidate, screenX, screenY))
            {
                bounds = candidate;
                return true;
            }
        }

        error = candidateBounds.Count == 0
            ? $"Could not read any window bounds for process {processId}."
            : $"Screen-space point ({screenX}, {screenY}) is outside every visible target window. " +
              $"Available bounds: {string.Join(", ", candidateBounds.Select(FormatBounds))}. " +
              "Use window-relative coordinates, pass windowTitle for the intended window, or set " +
              "allowOutOfWindow=true intentionally.";
        return false;
    }

    /// <summary>
    /// Filters visible top-level windows by a case-insensitive title substring.
    /// Single-sourced so window-scoped capture and coordinate mapping resolve the
    /// same window — the screen coordinates returned for a visually grounded point
    /// must map against the window that was captured.
    /// </summary>
    internal static List<(IntPtr Handle, string Title)> FilterByTitle(
        List<(IntPtr Handle, string Title)> windows,
        string? windowTitle)
    {
        return string.IsNullOrWhiteSpace(windowTitle)
            ? windows
            : windows
                .Where(window => window.Title.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    private static bool TryReadBounds(IntPtr hwnd, out WindowBounds bounds)
    {
        bounds = default!;
        if (!GetWindowRect(hwnd, out Rect rect))
        {
            return false;
        }

        bounds = new WindowBounds(
            Left: rect.Left,
            Top: rect.Top,
            Width: rect.Right - rect.Left,
            Height: rect.Bottom - rect.Top);
        return true;
    }

    private static bool ContainsScreenPoint(WindowBounds bounds, int screenX, int screenY) =>
        WindowPointMapper.Contains(
            bounds.Width,
            bounds.Height,
            screenX - bounds.Left,
            screenY - bounds.Top);

    private static string FormatBounds(WindowBounds bounds) =>
        $"left={bounds.Left}, top={bounds.Top}, width={bounds.Width}, height={bounds.Height}";

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
