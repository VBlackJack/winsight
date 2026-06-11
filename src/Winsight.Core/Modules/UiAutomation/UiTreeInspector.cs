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
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Walks the UIA tree of a process window into a serializable node tree.
/// Optional filters prune the result to matching nodes and their ancestors,
/// which keeps the payload small on large applications.
/// </summary>
public sealed class UiTreeInspector
{
    public const int DefaultMaxDepth = 5;
    public const int MaxAllowedDepth = 20;

    /// <summary>
    /// Inspect the first window of the process — or the first window whose
    /// title contains <paramref name="windowTitle"/> — down to
    /// <paramref name="maxDepth"/>. When <paramref name="controlTypeFilter"/>
    /// or <paramref name="nameContainsFilter"/> is set, branches without any
    /// matching node are pruned (the root is always kept).
    /// </summary>
    public UiElementNode Inspect(
        int processId,
        int maxDepth = DefaultMaxDepth,
        string? windowTitle = null,
        string? controlTypeFilter = null,
        string? nameContainsFilter = null)
    {
        if (maxDepth < 0 || maxDepth > MaxAllowedDepth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDepth),
                $"maxDepth must be between 0 and {MaxAllowedDepth}.");
        }

        using UIA3Automation automation = new();
        IReadOnlyList<WindowRoot> windows =
            ElementFinder.ResolveWindows(automation, processId, windowTitle);

        if (windows.Count == 0)
        {
            string scope = string.IsNullOrEmpty(windowTitle)
                ? string.Empty
                : $" with a title containing \"{windowTitle}\"";
            throw new InvalidOperationException(
                $"No top-level window found for process {processId}{scope}.");
        }

        UiElementNode root = BuildNode(windows[0].Element, depth: 0, maxDepth);

        bool hasFilter = !string.IsNullOrEmpty(controlTypeFilter)
            || !string.IsNullOrEmpty(nameContainsFilter);
        return hasFilter
            ? Prune(root, controlTypeFilter, nameContainsFilter) ?? root with { Children = Array.Empty<UiElementNode>() }
            : root;
    }

    private static UiElementNode BuildNode(AutomationElement element, int depth, int maxDepth)
    {
        IReadOnlyList<UiElementNode> children = depth >= maxDepth
            ? Array.Empty<UiElementNode>()
            : element.FindAllChildren()
                .Select(child => BuildNode(child, depth + 1, maxDepth))
                .ToArray();

        System.Drawing.Rectangle rect = element.BoundingRectangle;
        return new UiElementNode(
            AutomationId: element.Properties.AutomationId.ValueOrDefault ?? string.Empty,
            Name: element.Properties.Name.ValueOrDefault ?? string.Empty,
            ControlType: element.Properties.ControlType.ValueOrDefault.ToString(),
            ClassName: element.Properties.ClassName.ValueOrDefault ?? string.Empty,
            IsEnabled: element.Properties.IsEnabled.ValueOrDefault,
            IsVisible: !element.Properties.IsOffscreen.ValueOrDefault,
            Value: ReadValue(element),
            X: rect.X,
            Y: rect.Y,
            Width: rect.Width,
            Height: rect.Height,
            Children: children);
    }

    private static string? ReadValue(AutomationElement element)
    {
        try
        {
            return element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Keep a node when it matches the filters or when any descendant does;
    /// returns null for fully pruned branches.
    /// </summary>
    private static UiElementNode? Prune(
        UiElementNode node,
        string? controlTypeFilter,
        string? nameContainsFilter)
    {
        List<UiElementNode> keptChildren = node.Children
            .Select(child => Prune(child, controlTypeFilter, nameContainsFilter))
            .Where(child => child is not null)
            .Select(child => child!)
            .ToList();

        bool selfMatches = MatchesFilters(node, controlTypeFilter, nameContainsFilter);
        if (!selfMatches && keptChildren.Count == 0)
        {
            return null;
        }
        return node with { Children = keptChildren };
    }

    private static bool MatchesFilters(
        UiElementNode node,
        string? controlTypeFilter,
        string? nameContainsFilter)
    {
        if (!string.IsNullOrEmpty(controlTypeFilter) &&
            !string.Equals(node.ControlType, controlTypeFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!string.IsNullOrEmpty(nameContainsFilter) &&
            !node.Name.Contains(nameContainsFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }
}
