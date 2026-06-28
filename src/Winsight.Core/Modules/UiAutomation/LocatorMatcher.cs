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

/// <summary>
/// Pure matching logic between an <see cref="ElementLocator"/> and an
/// element's property snapshot. Kept free of any live UIA dependency so the
/// matching semantics are unit-testable.
/// </summary>
public static class LocatorMatcher
{
    private const int ExactAutomationIdScore = 50;
    private const int ExactNameScore = 35;
    private const int ExactClassNameScore = 15;
    private const int ExactControlTypeScore = 15;
    private const int NameContainsScore = 20;
    private const int DescendantTextScore = 20;
    private const int PartialTextScore = 8;

    /// <summary>
    /// True when every criterion set on the locator matches the snapshot
    /// (logical AND). <see cref="ElementLocator.Index"/> is ignored here:
    /// it applies to the match list, not to a single element.
    /// </summary>
    public static bool Matches(
        ElementLocator locator,
        string? automationId,
        string? name,
        string? className,
        string? controlType,
        IEnumerable<string?>? descendantTexts = null)
    {
        if (!string.IsNullOrEmpty(locator.AutomationId) &&
            !string.Equals(automationId, locator.AutomationId, StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.IsNullOrEmpty(locator.Name) &&
            !string.Equals(name, locator.Name, StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.IsNullOrEmpty(locator.ClassName) &&
            !string.Equals(className, locator.ClassName, StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.IsNullOrEmpty(locator.ControlType) &&
            !string.Equals(controlType, locator.ControlType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!string.IsNullOrEmpty(locator.NameContains) &&
            (name is null || !name.Contains(locator.NameContains, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        if (!string.IsNullOrEmpty(locator.DescendantText) &&
            !AnyTextContains(descendantTexts, locator.DescendantText))
        {
            return false;
        }
        return true;
    }

    public static LocatorScore Score(
        ElementLocator locator,
        string? automationId,
        string? name,
        string? className,
        string? controlType,
        IEnumerable<string?>? descendantTexts = null)
    {
        var score = 0;
        List<string> reasons = new();

        AddExact(
            reasons,
            ref score,
            "automationId",
            locator.AutomationId,
            automationId,
            ExactAutomationIdScore,
            StringComparison.Ordinal);
        AddExact(
            reasons,
            ref score,
            "name",
            locator.Name,
            name,
            ExactNameScore,
            StringComparison.Ordinal);
        AddExact(
            reasons,
            ref score,
            "className",
            locator.ClassName,
            className,
            ExactClassNameScore,
            StringComparison.Ordinal);
        AddExact(
            reasons,
            ref score,
            "controlType",
            locator.ControlType,
            controlType,
            ExactControlTypeScore,
            StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(locator.NameContains) &&
            ContainsTerm(name, locator.NameContains))
        {
            score += NameContainsScore;
            reasons.Add("nameContains");
        }

        if (!string.IsNullOrEmpty(locator.DescendantText) &&
            AnyTextContains(descendantTexts, locator.DescendantText))
        {
            score += DescendantTextScore;
            reasons.Add("descendantText");
        }

        if (score == 0 && IsNearMiss(locator, automationId, name))
        {
            score += PartialTextScore;
            reasons.Add("partialText");
        }

        return new LocatorScore(score, string.Join(", ", reasons));
    }

    /// <summary>
    /// Loose relevance check used to build "near miss" suggestions when a
    /// strict lookup finds nothing: the snapshot's id or name contains any
    /// textual term of the locator, case-insensitively.
    /// </summary>
    public static bool IsNearMiss(
        ElementLocator locator,
        string? automationId,
        string? name)
    {
        return ContainsTerm(automationId, locator.AutomationId)
            || ContainsTerm(name, locator.AutomationId)
            || ContainsTerm(automationId, locator.Name)
            || ContainsTerm(name, locator.Name)
            || ContainsTerm(name, locator.NameContains)
            || ContainsTerm(automationId, locator.NameContains)
            || ContainsTerm(name, locator.DescendantText)
            || ContainsTerm(automationId, locator.DescendantText);
    }

    private static bool AnyTextContains(IEnumerable<string?>? haystacks, string term)
    {
        if (haystacks is null)
        {
            return false;
        }
        foreach (string? haystack in haystacks)
        {
            if (haystack is not null &&
                haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsTerm(string? haystack, string? term)
    {
        return !string.IsNullOrEmpty(haystack)
            && !string.IsNullOrEmpty(term)
            && haystack.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddExact(
        List<string> reasons,
        ref int score,
        string reason,
        string? expected,
        string? actual,
        int weight,
        StringComparison comparison)
    {
        if (string.IsNullOrEmpty(expected))
        {
            return;
        }

        if (string.Equals(actual, expected, comparison))
        {
            score += weight;
            reasons.Add(reason);
        }
    }
}
