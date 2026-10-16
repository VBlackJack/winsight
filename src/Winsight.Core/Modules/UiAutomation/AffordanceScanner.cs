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
using System.Security.Cryptography;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Surfaces a window's actionable elements as a compact, position-ordered list,
/// the perception layer behind <c>describe_ui</c>. Instead of returning a full
/// UIA tree for the agent to parse, it keeps only interactable control types,
/// reads each one's state, and attaches the smallest locator that targets it.
/// </summary>
public sealed class AffordanceScanner
{
    public const int DefaultMaxResults = 200;

    // Control types an agent can act on. Containers (List, Tab, ComboBox) are
    // included because they are themselves targets (select_item), as are their
    // selectable children.
    private static readonly HashSet<string> InteractableControlTypes = new(StringComparer.Ordinal)
    {
        "Button",
        "SplitButton",
        "Edit",
        "CheckBox",
        "RadioButton",
        "ComboBox",
        "List",
        "ListItem",
        "Tab",
        "TabItem",
        "MenuItem",
        "Hyperlink",
        "TreeItem",
        "Slider",
    };

    /// <summary>
    /// Describe the actionable elements of the process window (or the first
    /// window whose title contains <paramref name="windowTitle"/>). The walk is
    /// breadth-first over windows so the recommended control-type indices match
    /// the locator tools, and the result is ordered top-to-bottom, left-to-right.
    /// </summary>
    public IReadOnlyList<UiInteractable> Describe(
        int processId,
        string? windowTitle = null,
        int maxResults = DefaultMaxResults)
    {
        int cap = maxResults > 0
            ? Math.Min(maxResults, UiaDefaults.MaxDescribeResults)
            : DefaultMaxResults;

        using UIA3Automation automation = new();
        IReadOnlyList<WindowRoot> windows = ElementFinder.ResolveWindows(automation, processId, windowTitle);

        List<ElementState> states = new();
        var visited = 0;
        foreach (WindowRoot window in windows)
        {
            Queue<AutomationElement> queue = new();
            queue.Enqueue(window.Element);
            while (queue.Count > 0 && states.Count < cap)
            {
                if (++visited > UiaDefaults.MaxTraversalNodes)
                {
                    throw new InvalidOperationException(
                        $"UI Automation traversal exceeded {UiaDefaults.MaxTraversalNodes} nodes while describing process {processId}. " +
                        "Pass windowTitle or a smaller maxResults.");
                }

                AutomationElement current = queue.Dequeue();
                (string AutomationId, string Name, string ClassName, string ControlType)? snapshot =
                    TryReadSnapshot(current);
                if (snapshot is null)
                {
                    continue;
                }

                (string automationId, string name, string className, string controlType) = snapshot.Value;
                if (InteractableControlTypes.Contains(controlType))
                {
                    ElementMatch match = new(
                        current,
                        new ElementCandidate(states.Count, automationId, name, controlType, className, window.Title),
                        window.Handle);
                    states.Add(ElementStateReader.BuildState(match));
                }

                foreach (AutomationElement child in SafeChildren(current))
                {
                    queue.Enqueue(child);
                }
            }
        }

        List<AffordanceRecommender.Candidate> candidates = states
            .Select(s => new AffordanceRecommender.Candidate(
                s.ControlType, NullIfEmpty(s.AutomationId), s.Name))
            .ToList();
        List<string> recommendations = AffordanceRecommender.Recommend(candidates);

        List<UiInteractable> result = new(states.Count);
        for (int i = 0; i < states.Count; i++)
        {
            ElementState s = states[i];
            string recommendedLocator = recommendations[i];
            result.Add(new UiInteractable(
                ControlType: s.ControlType,
                AutomationId: NullIfEmpty(s.AutomationId),
                Name: s.Name,
                Value: s.Value,
                ToggleState: s.ToggleState,
                IsSelected: s.IsSelected,
                IsEnabled: s.IsEnabled,
                IsVisible: s.IsVisible,
                X: s.X,
                Y: s.Y,
                Width: s.Width,
                Height: s.Height,
                WindowTitle: s.WindowTitle,
                RecommendedLocator: recommendedLocator,
                AffordanceId: BuildAffordanceId(s, recommendedLocator)));
        }

        // Read like the screen: top-to-bottom, then left-to-right.
        return result
            .OrderBy(r => r.Y)
            .ThenBy(r => r.X)
            .ToList();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static string BuildAffordanceId(ElementState state, string recommendedLocator)
    {
        string stableKey = string.Join(
            "|",
            state.WindowTitle,
            state.ControlType,
            state.AutomationId,
            state.Name,
            recommendedLocator);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(stableKey));
        return "af_" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static (string AutomationId, string Name, string ClassName, string ControlType)? TryReadSnapshot(
        AutomationElement element)
    {
        try
        {
            return (
                element.Properties.AutomationId.ValueOrDefault ?? string.Empty,
                element.Properties.Name.ValueOrDefault ?? string.Empty,
                element.Properties.ClassName.ValueOrDefault ?? string.Empty,
                element.Properties.ControlType.ValueOrDefault.ToString());
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static AutomationElement[] SafeChildren(AutomationElement element)
    {
        try
        {
            return element.FindAllChildren();
        }
        catch (COMException)
        {
            return Array.Empty<AutomationElement>();
        }
    }
}
