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
/// How an element should be clicked.
/// </summary>
public enum ClickStyle
{
    /// <summary>Primary action: Invoke pattern, then Toggle pattern, then a left mouse click.</summary>
    Primary,

    /// <summary>Physical double left click at the element's clickable point.</summary>
    DoubleClick,

    /// <summary>Physical right click at the element's clickable point (context menu).</summary>
    RightClick,
}

/// <summary>
/// Parses wire-format click styles ("single", "double", "right") into
/// <see cref="ClickStyle"/> values, case-insensitively.
/// </summary>
public static class ClickStyleParser
{
    /// <summary>Try to parse a wire-format click style name.</summary>
    public static bool TryParse(string? raw, out ClickStyle style)
    {
        string trimmed = (raw ?? string.Empty).Trim();
        if (string.Equals(trimmed, "single", StringComparison.OrdinalIgnoreCase))
        {
            style = ClickStyle.Primary;
            return true;
        }
        if (string.Equals(trimmed, "double", StringComparison.OrdinalIgnoreCase))
        {
            style = ClickStyle.DoubleClick;
            return true;
        }
        if (string.Equals(trimmed, "right", StringComparison.OrdinalIgnoreCase))
        {
            style = ClickStyle.RightClick;
            return true;
        }
        style = ClickStyle.Primary;
        return false;
    }

    /// <summary>Comma-separated list of accepted wire-format names, for error messages.</summary>
    public static string AcceptedValues => "single, double, right";
}
