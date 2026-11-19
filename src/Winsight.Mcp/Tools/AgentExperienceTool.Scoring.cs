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

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Orchestration;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

public static partial class AgentExperienceTool
{
    private static List<object> SuggestActions(
        IReadOnlyList<UiInteractable> elements,
        int processId,
        string? windowTitle)
    {
        return elements
            .Where(element => element.IsEnabled && element.IsVisible)
            .Take(20)
            .Select(element => new
            {
                element.AffordanceId,
                action = ActionFor(element),
                label = string.IsNullOrEmpty(element.Name) ? element.AutomationId : element.Name,
                element.ControlType,
                tool = SuggestedToolFor(element),
                requiredInput = RequiredInputFor(element),
                arguments = SuggestedArgsFor(element, processId, windowTitle),
                risk = RiskFor(element),
                expectedFeedback = ExpectedFeedbackFor(element),
                element.RecommendedLocator,
                locatorArgs = LocatorArgs(element),
            })
            .Cast<object>()
            .ToList();
    }

    private static List<RankedInteractable> RankElements(
        string query,
        IReadOnlyList<UiInteractable> elements,
        int maxResults)
    {
        string[] terms = query.Split(
            TermSeparators,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return elements
            .Select(element => Score(query, terms, element))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .Take(maxResults)
            .ToList();
    }

    private static RankedInteractable Score(string query, IReadOnlyList<string> terms, UiInteractable element)
    {
        var score = 0;
        List<string> reasons = new();
        if (string.Equals(element.Name, query, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
            reasons.Add("exactName");
        }
        if (string.Equals(element.AutomationId, query, StringComparison.OrdinalIgnoreCase))
        {
            score += 90;
            reasons.Add("exactAutomationId");
        }
        if (element.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
            reasons.Add("nameContains");
        }
        if (element.AutomationId?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
        {
            score += 50;
            reasons.Add("automationIdContains");
        }
        foreach (string term in terms)
        {
            if (element.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                element.RecommendedLocator.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                element.ControlType.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }
        }
        if (!element.IsEnabled || !element.IsVisible)
        {
            score -= 25;
            reasons.Add("notReady");
        }

        return new RankedInteractable(Math.Max(0, score), string.Join(", ", reasons.Distinct()), element);
    }

    private static BatchStep BuildStep(
        string action,
        UiInteractable element,
        string? text,
        int processId,
        string? windowTitle)
    {
        LocatorParts locator = LocatorArgs(element);
        return new BatchStep(
            action,
            Label: $"planned:{element.Name}",
            ProcessId: processId,
            AutomationId: locator.AutomationId,
            Name: locator.Name,
            ControlType: locator.ControlType,
            Index: locator.Index,
            WindowTitle: windowTitle ?? element.WindowTitle,
            Text: action == "type" ? text ?? string.Empty : null);
    }

    private static BatchStep BuildAssertionStep(
        UiInteractable element,
        string expectedValue,
        int processId,
        string? windowTitle)
    {
        LocatorParts locator = LocatorArgs(element);
        return new BatchStep(
            "assert",
            Label: $"assert:{element.Name}",
            ProcessId: processId,
            AutomationId: locator.AutomationId,
            Name: locator.Name,
            ControlType: locator.ControlType,
            Index: locator.Index,
            WindowTitle: windowTitle ?? element.WindowTitle,
            Operator: "contains",
            ExpectedValue: expectedValue);
    }

    private static string ActionFor(UiInteractable element)
    {
        return element.ControlType switch
        {
            "Edit" => "type",
            _ => "click",
        };
    }

    private static string SuggestedToolFor(UiInteractable element) =>
        element.ControlType == "Edit" ? "type_and_assert" : "act_and_observe";

    private static string[] RequiredInputFor(UiInteractable element) =>
        element.ControlType == "Edit" ? new[] { "text" } : Array.Empty<string>();

    private static object SuggestedArgsFor(UiInteractable element, int processId, string? windowTitle)
    {
        LocatorParts locator = LocatorArgs(element);
        if (element.ControlType == "Edit")
        {
            return new
            {
                processId,
                text = "<text>",
                @operator = "contains",
                expected = "<expected text>",
                automationId = locator.AutomationId,
                name = locator.Name,
                controlType = locator.ControlType,
                nameContains = (string?)null,
                windowTitle = windowTitle ?? element.WindowTitle,
            };
        }

        BatchStep step = BuildStep(ActionFor(element), element, null, processId, windowTitle);
        return new
        {
            processId,
            dryRun = true,
            step,
        };
    }

    private static string RiskFor(UiInteractable element)
    {
        if (!element.IsEnabled || !element.IsVisible)
        {
            return "not_ready";
        }
        return element.ControlType switch
        {
            "Button" or "SplitButton" or "MenuItem" or "Hyperlink" => "mutates_or_navigates",
            "Edit" => "writes_text",
            "ComboBox" or "List" or "ListItem" or "Tab" or "TabItem" => "changes_selection",
            _ => "low",
        };
    }

    private static string ExpectedFeedbackFor(UiInteractable element)
    {
        return element.ControlType switch
        {
            "Edit" => "type_and_assert should confirm value, or get_element_state should expose the new value.",
            "Button" or "SplitButton" or "MenuItem" or "Hyperlink" =>
                "act_and_observe should report changed UI, opened windows, or a follow-up assertion target.",
            "ComboBox" or "List" or "ListItem" or "Tab" or "TabItem" =>
                "observe or get_element_state should show selection/value changed.",
            _ => "act_and_observe should report whether the UI changed.",
        };
    }


    private static LocatorParts LocatorArgs(UiInteractable element)
    {
        if (!string.IsNullOrEmpty(element.AutomationId) &&
            element.RecommendedLocator.StartsWith("automationId=", StringComparison.Ordinal))
        {
            return new LocatorParts(element.AutomationId, null, null, null);
        }
        if (!string.IsNullOrEmpty(element.Name) &&
            element.RecommendedLocator.StartsWith("name=", StringComparison.Ordinal))
        {
            return new LocatorParts(null, element.Name, null, null);
        }

        return new LocatorParts(null, null, element.ControlType, ParseIndex(element.RecommendedLocator));
    }

    private static int? ParseIndex(string recommendedLocator)
    {
        const string marker = "index=";
        int index = recommendedLocator.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }
        string raw = recommendedLocator[(index + marker.Length)..].Trim();
        return int.TryParse(raw, System.Globalization.NumberStyles.Integer, null, out int parsed)
            ? parsed
            : null;
    }


    private sealed record RankedInteractable(int Score, string Reason, UiInteractable Element);

    private sealed record LocatorParts(
        string? AutomationId,
        string? Name,
        string? ControlType,
        int? Index);
}
