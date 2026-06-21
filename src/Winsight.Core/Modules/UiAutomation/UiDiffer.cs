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

using System.Globalization;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Computes the delta between two <see cref="UiSnapshot"/>s. Pure, so the
/// feedback sense behind <c>diff_ui</c> is unit-tested without live UI: windows
/// are matched by handle, actionable elements by a stable key (window + unique
/// AutomationId, else control type + name, with duplicate occurrences
/// disambiguated in order), and persisting elements are compared field by field.
/// </summary>
public static class UiDiffer
{
    public const string VerdictChanged = "changed";
    public const string VerdictNoChange = "no_change";

    public static UiChange Diff(UiSnapshot before, UiSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        HashSet<long> beforeHandles = before.Windows.Select(w => w.WindowHandle).ToHashSet();
        HashSet<long> afterHandles = after.Windows.Select(w => w.WindowHandle).ToHashSet();
        List<WindowDescriptor> windowsOpened = after.Windows
            .Where(w => !beforeHandles.Contains(w.WindowHandle)).ToList();
        List<WindowDescriptor> windowsClosed = before.Windows
            .Where(w => !afterHandles.Contains(w.WindowHandle)).ToList();

        Dictionary<string, UiInteractable> beforeMap = KeyMap(before.Interactables);
        Dictionary<string, UiInteractable> afterMap = KeyMap(after.Interactables);

        List<UiInteractable> added = afterMap
            .Where(kv => !beforeMap.ContainsKey(kv.Key))
            .Select(kv => kv.Value).ToList();
        List<UiInteractable> removed = beforeMap
            .Where(kv => !afterMap.ContainsKey(kv.Key))
            .Select(kv => kv.Value).ToList();

        List<ElementChange> changed = new();
        foreach (KeyValuePair<string, UiInteractable> entry in afterMap)
        {
            if (!beforeMap.TryGetValue(entry.Key, out UiInteractable? old))
            {
                continue;
            }
            List<FieldChange> fields = CompareFields(old, entry.Value);
            if (fields.Count > 0)
            {
                UiInteractable e = entry.Value;
                changed.Add(new ElementChange(
                    e.ControlType, e.AutomationId, e.Name, e.WindowTitle, e.RecommendedLocator, fields));
            }
        }

        bool anyChange = windowsOpened.Count > 0 || windowsClosed.Count > 0 ||
            added.Count > 0 || removed.Count > 0 || changed.Count > 0;
        return new UiChange(
            anyChange ? VerdictChanged : VerdictNoChange,
            windowsOpened,
            windowsClosed,
            added,
            removed,
            changed);
    }

    private static List<FieldChange> CompareFields(UiInteractable before, UiInteractable after)
    {
        List<FieldChange> changes = new();
        AddIfChanged(changes, "name", before.Name, after.Name);
        AddIfChanged(changes, "value", before.Value, after.Value);
        AddIfChanged(changes, "toggleState", before.ToggleState, after.ToggleState);
        AddIfChanged(changes, "isSelected", Format(before.IsSelected), Format(after.IsSelected));
        AddIfChanged(changes, "isEnabled", Format(before.IsEnabled), Format(after.IsEnabled));
        AddIfChanged(changes, "isVisible", Format(before.IsVisible), Format(after.IsVisible));
        return changes;
    }

    private static void AddIfChanged(List<FieldChange> changes, string field, string? before, string? after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add(new FieldChange(field, before, after));
        }
    }

    private static string Format(bool value) => value ? "true" : "false";

    private static string? Format(bool? value) =>
        value is bool b ? Format(b) : null;

    private static Dictionary<string, UiInteractable> KeyMap(IReadOnlyList<UiInteractable> items)
    {
        Dictionary<string, UiInteractable> map = new(StringComparer.Ordinal);
        Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
        foreach (UiInteractable item in items)
        {
            string baseKey = BaseKey(item);
            int occurrence = occurrences.TryGetValue(baseKey, out int count) ? count : 0;
            occurrences[baseKey] = occurrence + 1;
            map[baseKey + "#" + occurrence.ToString(CultureInfo.InvariantCulture)] = item;
        }
        return map;
    }

    private static string BaseKey(UiInteractable item)
    {
        string identity = string.IsNullOrEmpty(item.AutomationId)
            ? "tn:" + item.ControlType + ":" + item.Name
            : "id:" + item.AutomationId;
        return item.WindowTitle + "::" + identity;
    }
}
