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
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>Window operations accepted by <see cref="WindowManager"/>.</summary>
public enum WindowAction
{
    Move,
    Resize,
    SetBounds,
    Maximize,
    Minimize,
    Restore,
}

/// <summary>Parses wire-format window action names.</summary>
public static class WindowActionParser
{
    private static readonly Dictionary<string, WindowAction> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["move"] = WindowAction.Move,
        ["resize"] = WindowAction.Resize,
        ["setbounds"] = WindowAction.SetBounds,
        ["maximize"] = WindowAction.Maximize,
        ["minimize"] = WindowAction.Minimize,
        ["restore"] = WindowAction.Restore,
    };

    public const string AcceptedValues = "move, resize, set_bounds, maximize, minimize, restore";

    public static bool TryParse(string? raw, out WindowAction action) =>
        Names.TryGetValue((raw ?? string.Empty).Trim().Replace("_", string.Empty, StringComparison.Ordinal), out action);

    /// <summary>Null when the action has the coordinates it needs, otherwise what is missing.</summary>
    public static string? ValidateArguments(WindowAction action, int? x, int? y, int? width, int? height)
    {
        bool needsPosition = action is WindowAction.Move or WindowAction.SetBounds;
        bool needsSize = action is WindowAction.Resize or WindowAction.SetBounds;
        if (needsPosition && (x is null || y is null))
        {
            return "x and y are required for move and set_bounds.";
        }
        if (needsSize && (width is null || height is null))
        {
            return "width and height are required for resize and set_bounds.";
        }
        if (needsSize && (width <= 0 || height <= 0))
        {
            return "width and height must be positive.";
        }
        return null;
    }
}

/// <summary>
/// Moves, resizes, maximizes, minimizes and restores a top-level window of
/// the target process, then reports the state the window settled in.
/// Requests are posted asynchronously (ShowWindowAsync, SWP_ASYNCWINDOWPOS),
/// so a busy UI thread cannot block the caller; the result is read once the
/// window reaches the requested state or the settle timeout elapses.
/// </summary>
public sealed class WindowManager
{
    public const string StateNormal = "normal";
    public const string StateMaximized = "maximized";
    public const string StateMinimized = "minimized";

    private const int SettleTimeoutMs = 2000;
    private const int SettlePollMs = 50;
    private const int ShowMaximize = 3;
    private const int ShowMinimize = 6;
    private const int ShowRestore = 9;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpAsyncWindowPos = 0x4000;

    /// <summary>
    /// Apply <paramref name="action"/> to the process main window, or to the
    /// first window whose title contains <paramref name="windowTitle"/>.
    /// Moving or resizing a maximized or minimized window restores it first.
    /// </summary>
    public OperationResult Apply(
        int processId,
        WindowAction action,
        int? x = null,
        int? y = null,
        int? width = null,
        int? height = null,
        string? windowTitle = null)
    {
        string? invalid = WindowActionParser.ValidateArguments(action, x, y, width, height);
        if (invalid is not null)
        {
            return OperationResult.Failure(invalid);
        }

        List<(IntPtr Handle, string Title)> candidates = WindowGeometry.FilterByTitle(
            NativeWindowEnumerator.VisibleTopLevelWindows(processId), windowTitle);
        if (candidates.Count == 0)
        {
            return OperationResult.Failure(string.IsNullOrWhiteSpace(windowTitle)
                ? $"No top-level window found for process {processId}."
                : $"No window of process {processId} has a title containing \"{windowTitle}\".");
        }
        (IntPtr handle, string title) = string.IsNullOrWhiteSpace(windowTitle)
            ? NativeWindowEnumerator.PreferMainWindow(processId, candidates)[0]
            : NativeWindowEnumerator.PreferPrimary(candidates)[0];

        switch (action)
        {
            case WindowAction.Maximize:
                ShowWindowAsync(handle, ShowMaximize);
                break;
            case WindowAction.Minimize:
                ShowWindowAsync(handle, ShowMinimize);
                break;
            case WindowAction.Restore:
                // SW_RESTORE on a window minimized from the maximized state brings
                // it back maximized; a second restore returns it to normal bounds.
                ShowWindowAsync(handle, ShowRestore);
                WaitUntil(() => ReadState(handle) != StateMinimized);
                if (ReadState(handle) == StateMaximized)
                {
                    ShowWindowAsync(handle, ShowRestore);
                }
                break;
            default:
                if (ReadState(handle) != StateNormal)
                {
                    ShowWindowAsync(handle, ShowRestore);
                    WaitUntil(() => ReadState(handle) == StateNormal);
                }
                uint flags = SwpNoZOrder | SwpNoActivate | SwpAsyncWindowPos;
                if (action == WindowAction.Move)
                {
                    flags |= SwpNoSize;
                }
                if (action == WindowAction.Resize)
                {
                    flags |= SwpNoMove;
                }
                if (!SetWindowPos(handle, IntPtr.Zero, x ?? 0, y ?? 0, width ?? 0, height ?? 0, flags))
                {
                    return OperationResult.Failure(
                        $"SetWindowPos failed for window 0x{handle.ToInt64():x} (Win32 error {Marshal.GetLastWin32Error()}).");
                }
                break;
        }

        bool settled = WaitUntil(() => Matches(handle, action, x, y, width, height));
        // The show state flips before the frame is laid out (a restore from
        // maximized reads "normal" with the maximized bounds for a moment), so
        // report the bounds only once two consecutive reads agree.
        WaitForStableBounds(handle);
        if (!GetWindowRect(handle, out NativeRect rect))
        {
            return OperationResult.Failure($"The window 0x{handle.ToInt64():x} closed during {action}.");
        }
        return OperationResult.OkWith(new WindowStateResult(
            handle.ToInt64(),
            title,
            action.ToString(),
            ReadState(handle),
            rect.Left,
            rect.Top,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top,
            settled));
    }

    private static bool Matches(IntPtr handle, WindowAction action, int? x, int? y, int? width, int? height)
    {
        string state = ReadState(handle);
        if (!GetWindowRect(handle, out NativeRect rect))
        {
            return false;
        }
        bool position = x is null || (rect.Left == x && rect.Top == y);
        bool size = width is null || (rect.Right - rect.Left == width && rect.Bottom - rect.Top == height);
        return action switch
        {
            WindowAction.Maximize => state == StateMaximized,
            WindowAction.Minimize => state == StateMinimized,
            WindowAction.Restore => state == StateNormal,
            WindowAction.Move => state == StateNormal && position,
            WindowAction.Resize => state == StateNormal && size,
            _ => state == StateNormal && position && size,
        };
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds <= SettleTimeoutMs)
        {
            if (condition())
            {
                return true;
            }
            Thread.Sleep(SettlePollMs);
        }
        return condition();
    }

    private static void WaitForStableBounds(IntPtr handle)
    {
        GetWindowRect(handle, out NativeRect previous);
        WaitUntil(() =>
        {
            Thread.Sleep(SettlePollMs);
            bool read = GetWindowRect(handle, out NativeRect current);
            bool stable = read && current.Equals(previous);
            previous = current;
            return stable || !read;
        });
    }

    private static string ReadState(IntPtr handle) =>
        IsIconic(handle) ? StateMinimized : IsZoomed(handle) ? StateMaximized : StateNormal;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);
}
