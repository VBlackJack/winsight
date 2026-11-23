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

using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>Decides whether a scene element is worth an agent's attention as an action target.</summary>
public static class SceneActionability
{
    public const string ActionScrollIntoView = "scroll_into_view";
    public const string ActionDragTo = "drag_to";
    public const string ActionRightClick = "right_click";
    public const string ActionDoubleClick = "double_click";

    /// <summary>
    /// Actions granted to every element with non-empty bounds. They say nothing about whether the
    /// element is a real control, so they never make an element actionable on their own.
    /// </summary>
    private static readonly HashSet<string> GenericPointerActions = new(StringComparer.Ordinal)
    {
        ActionDragTo,
        ActionRightClick,
        ActionDoubleClick,
    };

    /// <summary>
    /// True when the element has at least one action beyond the generic pointer set and is either
    /// visible or can be scrolled into view (the only action accepted on an offscreen node).
    /// </summary>
    public static bool IsActionable(SceneElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        bool hasSpecificAction = false;
        bool canScrollIntoView = false;
        foreach (string action in element.Actions)
        {
            if (!GenericPointerActions.Contains(action)) hasSpecificAction = true;
            if (string.Equals(action, ActionScrollIntoView, StringComparison.Ordinal)) canScrollIntoView = true;
        }
        return hasSpecificAction && (element.Visible || canScrollIntoView);
    }
}
