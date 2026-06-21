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
/// Computes the smallest locator that uniquely targets each element of a set:
/// a unique AutomationId wins, then a unique Name, otherwise a control-type plus
/// index (the index click_element would use, since the input order mirrors that
/// tool's match order). Pure, so it is unit-tested without live UI Automation.
/// </summary>
public static class AffordanceRecommender
{
    public readonly record struct Candidate(string ControlType, string? AutomationId, string Name);

    /// <summary>
    /// One recommendation string per input item, aligned by index. Items must be
    /// supplied in the same order the locator tools enumerate elements (BFS over
    /// windows) so the control-type index is valid.
    /// </summary>
    public static List<string> Recommend(IReadOnlyList<Candidate> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Dictionary<string, int> automationIdCounts = new(StringComparer.Ordinal);
        Dictionary<string, int> nameCounts = new(StringComparer.Ordinal);
        foreach (Candidate item in items)
        {
            if (!string.IsNullOrEmpty(item.AutomationId))
            {
                automationIdCounts[item.AutomationId] = Count(automationIdCounts, item.AutomationId) + 1;
            }
            if (!string.IsNullOrEmpty(item.Name))
            {
                nameCounts[item.Name] = Count(nameCounts, item.Name) + 1;
            }
        }

        Dictionary<string, int> typeIndex = new(StringComparer.Ordinal);
        List<string> result = new(items.Count);
        foreach (Candidate item in items)
        {
            int index = Count(typeIndex, item.ControlType);
            typeIndex[item.ControlType] = index + 1;

            if (!string.IsNullOrEmpty(item.AutomationId) && automationIdCounts[item.AutomationId] == 1)
            {
                result.Add($"automationId=\"{item.AutomationId}\"");
            }
            else if (!string.IsNullOrEmpty(item.Name) && nameCounts[item.Name] == 1)
            {
                result.Add($"name=\"{item.Name}\"");
            }
            else
            {
                result.Add($"controlType={item.ControlType} index={index}");
            }
        }
        return result;
    }

    private static int Count(Dictionary<string, int> counts, string key) =>
        counts.TryGetValue(key, out int value) ? value : 0;
}
