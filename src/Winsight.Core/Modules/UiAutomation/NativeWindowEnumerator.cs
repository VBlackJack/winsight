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

/// <summary>
/// Enumerates visible top-level Win32 windows of a process. UIA's desktop
/// child enumeration reparents owned windows (modal dialogs) under their
/// owner and then omits them from child listings, making them unreachable
/// through tree walks alone. Rooting searches at Win32 handles instead
/// makes every window of the process addressable.
/// </summary>
internal static class NativeWindowEnumerator
{
    /// <summary>Buffer size for window titles.</summary>
    private const int MaxTitleLength = 512;

    /// <summary>Buffer size for window class names.</summary>
    private const int MaxClassNameLength = 256;

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const uint GwOwner = 4;
    private const long WsCaption = 0x00C00000L;
    private const long WsPopup = 0x80000000L;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, [Out] char[] text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    /// <summary>
    /// Handles and titles of the visible top-level windows of the process,
    /// in z-order (topmost first).
    /// </summary>
    internal static List<(IntPtr Handle, string Title)> VisibleTopLevelWindows(int processId)
    {
        List<(IntPtr Handle, string Title)> result = new();
        EnumWindows((hwnd, lParam) =>
        {
            uint threadId = GetWindowThreadProcessId(hwnd, out uint windowProcessId);
            if (threadId == 0)
            {
                // The window vanished between enumeration and the query.
                return true;
            }
            if (windowProcessId == (uint)processId && IsWindowVisible(hwnd))
            {
                char[] buffer = new char[MaxTitleLength];
                int length = GetWindowText(hwnd, buffer, buffer.Length);
                result.Add((hwnd, new string(buffer, 0, length)));
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>
    /// The same windows reordered so that the ones an agent means when it names
    /// no window come first: z-order is kept within each group, and transient
    /// overlays (tooltips, menus, dropdowns, see
    /// <see cref="WindowClassifier.IsTransient"/>) move behind every other
    /// window. When every window is transient the order is unchanged.
    /// </summary>
    internal static List<(IntPtr Handle, string Title)> PreferPrimary(List<(IntPtr Handle, string Title)> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        List<(IntPtr Handle, string Title)> primary = new(windows.Count);
        List<(IntPtr Handle, string Title)> transient = new();
        foreach ((IntPtr Handle, string Title) window in windows)
        {
            (IsTransient(window.Handle) ? transient : primary).Add(window);
        }
        primary.AddRange(transient);
        return primary;
    }

    /// <summary>
    /// The window <see cref="Process.MainWindowHandle"/> designates, when it is
    /// among <paramref name="windows"/>; otherwise the first window that
    /// <see cref="PreferPrimary"/> puts in front.
    /// </summary>
    internal static List<(IntPtr Handle, string Title)> PreferMainWindow(
        int processId,
        List<(IntPtr Handle, string Title)> windows)
    {
        List<(IntPtr Handle, string Title)> ordered = PreferPrimary(windows);
        IntPtr main = TryGetMainWindowHandle(processId);
        int index = ordered.FindIndex(w => w.Handle == main);
        if (index > 0)
        {
            (IntPtr Handle, string Title) mainWindow = ordered[index];
            ordered.RemoveAt(index);
            ordered.Insert(0, mainWindow);
        }
        return ordered;
    }

    /// <summary>Whether the window is a transient overlay; see <see cref="WindowClassifier.IsTransient"/>.</summary>
    internal static bool IsTransient(IntPtr hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        long extendedStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        bool hasOwner = GetWindow(hwnd, GwOwner) != IntPtr.Zero;
        string kind = WindowClassifier.ClassifyKind(
            ReadClassName(hwnd),
            hasCaption: (style & WsCaption) == WsCaption,
            isPopupStyle: (style & WsPopup) != 0,
            hasOwner);
        return WindowClassifier.IsTransient(kind, extendedStyle, hasOwner);
    }

    private static string ReadClassName(IntPtr hwnd)
    {
        char[] buffer = new char[MaxClassNameLength];
        int length = GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static IntPtr TryGetMainWindowHandle(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            process.Refresh();
            return process.MainWindowHandle;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return IntPtr.Zero;
        }
    }
}
