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

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Decides from UIA and Win32 facts whether keyboard focus sits inside a
/// WebView2 surface. Synthetic keyboard input sent there (SendInput or the
/// UIA keyboard fallback) has crashed ctfmon, the text services host, so the
/// keyboard tools refuse it unless the caller opts in explicitly.
/// </summary>
public static class WebView2FocusClassifier
{
    /// <summary>UIA FrameworkId reported by Chromium accessibility providers.</summary>
    public const string ChromiumFrameworkId = "Chrome";

    /// <summary>Process that renders WebView2 content.</summary>
    public const string WebView2ProcessName = "msedgewebview2";

    /// <summary>Win32 window classes Chromium creates for its widgets and render surface.</summary>
    private static readonly string[] ChromiumClassPrefixes =
    {
        "Chrome_WidgetWin_",
        "Chrome_RenderWidgetHostHWND",
    };

    /// <summary>Class name the WPF and WinForms WebView2 controls expose through UIA.</summary>
    private const string WebView2ControlClassName = "WebView2";

    /// <summary>
    /// Every signal that places the focus in WebView2; empty when none does.
    /// </summary>
    /// <param name="frameworkId">UIA FrameworkId of the focused element.</param>
    /// <param name="processName">Name of the process that owns the focused element.</param>
    /// <param name="ancestorClassNames">UIA ClassName of the focused element and its ancestors.</param>
    /// <param name="win32FocusClassName">Class of the Win32 window that owns keyboard focus.</param>
    public static IReadOnlyList<string> Evidence(
        string? frameworkId,
        string? processName,
        IEnumerable<string> ancestorClassNames,
        string? win32FocusClassName)
    {
        ArgumentNullException.ThrowIfNull(ancestorClassNames);
        List<string> evidence = new();
        if (string.Equals(frameworkId, ChromiumFrameworkId, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add($"frameworkId={frameworkId}");
        }
        if (string.Equals(processName, WebView2ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add($"process={processName}");
        }
        if (IsChromiumClass(win32FocusClassName))
        {
            evidence.Add($"win32Focus={win32FocusClassName}");
        }
        foreach (string className in ancestorClassNames)
        {
            if (IsChromiumClass(className) ||
                string.Equals(className, WebView2ControlClassName, StringComparison.Ordinal))
            {
                evidence.Add($"uiaClass={className}");
                break;
            }
        }
        return evidence;
    }

    private static bool IsChromiumClass(string? className) =>
        !string.IsNullOrEmpty(className) &&
        ChromiumClassPrefixes.Any(prefix => className.StartsWith(prefix, StringComparison.Ordinal));
}
