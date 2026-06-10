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
using System.Text;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Enumerates visible top-level Win32 windows of a process. UIA's desktop
/// child enumeration reparents owned windows (modal dialogs) under their
/// owner and then omits them from child listings, making them unreachable
/// through tree walks alone — rooting searches at Win32 handles instead
/// makes every window of the process addressable.
/// </summary>
internal static class NativeWindowEnumerator
{
    /// <summary>Buffer size for window titles.</summary>
    private const int MaxTitleLength = 512;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, [Out] char[] text, int maxCount);

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
}
