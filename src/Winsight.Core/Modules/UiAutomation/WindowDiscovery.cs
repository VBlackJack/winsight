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

/// <summary>
/// Enumerates visible top-level windows through Win32 (not UIA desktop
/// children), so owned surfaces — modal dialogs, MessageBoxes, dropdown menus,
/// flyouts, tooltips — are discoverable, not just reachable. Each window is
/// classified (see <see cref="WindowClassifier"/>) and tagged with its owner and
/// modality, so an agent can identify the dialog or overlay to act on. Windows
/// are returned in z-order (topmost first), so the last opened overlay leads.
/// </summary>
public sealed class WindowDiscovery
{
    private const int MaxTitleLength = 512;
    private const int MaxClassNameLength = 256;
    private const int GWL_STYLE = -16;
    private const uint GW_OWNER = 4;
    private const long WS_CAPTION = 0x00C00000L;
    private const long WS_POPUP = 0x80000000L;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>
    /// Every named top-level window on the desktop (across processes), enriched
    /// with classification. Use this to discover candidate target processes.
    /// </summary>
    public IReadOnlyList<WindowDescriptor> List() => List(null);

    /// <summary>
    /// Top-level windows, optionally restricted to one process. When
    /// <paramref name="processId"/> is set, the listing is exhaustive — including
    /// untitled dialogs, popups, menus, and overlays of that process — so an
    /// agent sees the full window stack of the app under test. When it is null,
    /// only named windows are returned (desktop-wide discovery), to avoid the
    /// noise of every helper window on the desktop.
    /// </summary>
    public IReadOnlyList<WindowDescriptor> List(int? processId)
    {
        long foreground = GetForegroundWindow().ToInt64();
        List<WindowDescriptor> result = new();

        EnumWindows((hwnd, _) =>
        {
            uint threadId = GetWindowThreadProcessId(hwnd, out uint windowProcessId);
            if (threadId == 0)
            {
                // The window vanished between enumeration and the query.
                return true;
            }
            if (processId is int target && windowProcessId != (uint)target)
            {
                return true;
            }
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            string title = ReadText(hwnd);
            // Desktop-wide discovery keeps the historical "named windows only"
            // contract; a per-process listing is exhaustive because dialogs,
            // popups, and menus frequently carry no title.
            if (processId is null && string.IsNullOrEmpty(title))
            {
                return true;
            }

            if (!GetWindowRect(hwnd, out Rect rect))
            {
                return true;
            }
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
            {
                return true;
            }

            string className = ReadClassName(hwnd);
            long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
            bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;
            bool isPopupStyle = (style & WS_POPUP) != 0;
            IntPtr owner = GetWindow(hwnd, GW_OWNER);
            bool hasOwner = owner != IntPtr.Zero;
            bool ownerEnabled = !hasOwner || IsWindowEnabled(owner);

            result.Add(new WindowDescriptor(
                ProcessId: (int)windowProcessId,
                WindowHandle: hwnd.ToInt64(),
                Title: title,
                ProcessName: ResolveProcessName((int)windowProcessId),
                IsForeground: hwnd.ToInt64() == foreground,
                X: rect.Left,
                Y: rect.Top,
                Width: width,
                Height: height,
                Kind: WindowClassifier.ClassifyKind(className, hasCaption, isPopupStyle, hasOwner),
                ClassName: className,
                OwnerHandle: owner.ToInt64(),
                IsModal: WindowClassifier.IsModal(hasOwner, ownerEnabled)));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static string ReadText(IntPtr hwnd)
    {
        char[] buffer = new char[MaxTitleLength];
        int length = GetWindowText(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, length);
    }

    private static string ReadClassName(IntPtr hwnd)
    {
        char[] buffer = new char[MaxClassNameLength];
        int length = GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static string ResolveProcessName(int processId)
    {
        if (processId <= 0)
        {
            return string.Empty;
        }
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, [Out] char[] text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
