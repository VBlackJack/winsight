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

namespace Winsight.Core.Models;

/// <summary>
/// Selects how a window screenshot is captured.
/// </summary>
public enum CaptureSource
{
    /// <summary>
    /// Capture only the single target HWND via PrintWindow (PW_RENDERFULLCONTENT).
    /// Separate top-level popups (menus, dropdowns, tooltips) are not included.
    /// </summary>
    Window,

    /// <summary>
    /// Capture the DWM-composited desktop rectangle matching the target window bounds
    /// so floating same-process popups, menus, dropdowns, and tooltips appear while
    /// keeping window-relative coordinates.
    /// </summary>
    Screen,
}

public static class CaptureSourceParser
{
    public const string WindowName = "window";
    public const string ScreenName = "screen";

    public static bool TryParse(string? value, out CaptureSource source, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value.Trim(), WindowName, StringComparison.OrdinalIgnoreCase))
        {
            source = CaptureSource.Window;
            error = null;
            return true;
        }

        if (string.Equals(value.Trim(), ScreenName, StringComparison.OrdinalIgnoreCase))
        {
            source = CaptureSource.Screen;
            error = null;
            return true;
        }

        source = CaptureSource.Window;
        error = $"Invalid captureSource '{value}'. Expected '{WindowName}' or '{ScreenName}'.";
        return false;
    }

    public static string ToWireValue(CaptureSource source) => source switch
    {
        CaptureSource.Screen => ScreenName,
        _ => WindowName,
    };
}
