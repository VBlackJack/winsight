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
        string? controlType)
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
        return true;
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
            || ContainsTerm(automationId, locator.NameContains);
    }

    private static bool ContainsTerm(string? haystack, string? term)
    {
        return !string.IsNullOrEmpty(haystack)
            && !string.IsNullOrEmpty(term)
            && haystack.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
