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

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Winsight.Core.Models;
using Winsight.Core.Modules.Clipboard;
using FlaUIControlType = FlaUI.Core.Definitions.ControlType;

namespace Winsight.Core.Modules.UiAutomation;

public sealed partial class ActionInvoker
{
    /// <summary>
    /// Select an item by its visible text inside a located container
    /// (ComboBox, ListBox/List, Tab). Other containers fall back to a
    /// descendant search for an element exposing the SelectionItem pattern.
    /// </summary>
    public OperationResult SelectItem(
        int processId,
        ElementLocator locator,
        string itemName,
        string? windowTitle = null)
    {
        ArgumentNullException.ThrowIfNull(itemName);
        return SelectItem(processId, locator, ItemNameMatcher.Exact(itemName), windowTitle);
    }

    /// <summary>
    /// Select the item whose accessible name satisfies <paramref name="matcher"/>
    /// inside a located container. An exact name keeps the historical
    /// first-match behavior; a substring or pattern must match exactly one
    /// item, otherwise the call fails and lists the matching names. On success
    /// the data carries the full name of the selected item.
    /// </summary>
    public OperationResult SelectItem(
        int processId,
        ElementLocator locator,
        ItemNameMatcher matcher,
        string? windowTitle = null)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            using UIA3Automation automation = new();
            ElementSearchResult search = ElementFinder.FindSingle(automation, processId, locator, windowTitle);
            if (!search.Found)
            {
                return OperationResult.FailureWith(search.Error!, search.Candidates);
            }

            AutomationElement container = search.Element!;
            ScrollIntoViewIfOffscreen(container);
            string controlType = container.Properties.ControlType.ValueOrDefault.ToString();

            try
            {
                return SelectIn(container, matcher);
            }
            catch (COMException ex)
            {
                return OperationResult.Failure(
                    $"Selection of the item {matcher.Description} failed in {controlType} ({ex.Message}).");
            }
        }
    }


    internal static OperationResult SelectPinnedItem(AutomationElement container, string itemName) =>
        SelectIn(container, ItemNameMatcher.Exact(itemName));

    private static OperationResult SelectIn(AutomationElement container, ItemNameMatcher matcher) =>
        container.Properties.ControlType.ValueOrDefault.ToString() switch
        {
            "ComboBox" => SelectInComboBox(container, matcher),
            "List" => SelectInListBox(container, matcher),
            "Tab" => SelectTabItem(container, matcher),
            _ => SelectDescendantItem(container, matcher),
        };

    private static OperationResult SelectInComboBox(AutomationElement container, ItemNameMatcher matcher)
    {
        ComboBox comboBox = container.AsComboBox();
        if (matcher.ExactName is { } exact)
        {
            ComboBoxItem? selected = comboBox.Select(exact);
            if (selected is null)
            {
                return OperationResult.Failure(
                    $"Item \"{exact}\" not found in ComboBox. Available items: {DescribeItems(comboBox.Items.Select(i => i.Text))}");
            }
            // Collapse the dropdown so the popup does not linger over the UI.
            comboBox.Collapse();
            return OperationResult.OkWith(new SelectItemResult(exact));
        }

        comboBox.Expand();
        try
        {
            ComboBoxItem[] items = comboBox.Items;
            return SelectSingle(items, i => i.Text, matcher, "ComboBox", item => item.Select());
        }
        finally
        {
            comboBox.Collapse();
        }
    }

    private static OperationResult SelectInListBox(AutomationElement container, ItemNameMatcher matcher)
    {
        ListBox listBox = container.AsListBox();
        if (matcher.ExactName is { } exact)
        {
            ListBoxItem? selected = listBox.Select(exact);
            if (selected is null)
            {
                return OperationResult.Failure(
                    $"Item \"{exact}\" not found in ListBox. Available items: {DescribeItems(listBox.Items.Select(i => i.Text))}");
            }
            return OperationResult.OkWith(new SelectItemResult(exact));
        }
        return SelectSingle(listBox.Items, i => i.Text, matcher, "ListBox", item => item.Select());
    }

    private static OperationResult SelectTabItem(AutomationElement container, ItemNameMatcher matcher)
    {
        Tab tab = container.AsTab();
        TabItem[] tabItems = tab.TabItems;
        if (matcher.ExactName is { } exact)
        {
            TabItem? target = tabItems.FirstOrDefault(
                t => string.Equals(t.Properties.Name.ValueOrDefault, exact, StringComparison.Ordinal));
            if (target is null)
            {
                return OperationResult.Failure(
                    $"Tab item \"{exact}\" not found. Available items: {DescribeItems(tabItems.Select(t => t.Properties.Name.ValueOrDefault ?? string.Empty))}");
            }
            tab.SelectTabItem(exact);
            return OperationResult.OkWith(new SelectItemResult(exact));
        }
        return SelectSingle(
            tabItems, t => t.Properties.Name.ValueOrDefault ?? string.Empty, matcher, "Tab", item => item.Select());
    }

    private static OperationResult SelectDescendantItem(AutomationElement container, ItemNameMatcher matcher)
    {
        List<(AutomationElement Element, string Name)> matches = new();
        Queue<AutomationElement> queue = new();
        queue.Enqueue(container);
        var visited = 0;
        while (queue.Count > 0)
        {
            if (++visited > UiaDefaults.MaxTraversalNodes)
            {
                return OperationResult.Failure(
                    $"Selection search exceeded {UiaDefaults.MaxTraversalNodes} nodes. Narrow the locator.");
            }

            AutomationElement current = queue.Dequeue();
            string? name = SafeReadName(current);
            if (matcher.Matches(name) && current.Patterns.SelectionItem.PatternOrDefault is not null)
            {
                if (matcher.ExactName is not null)
                {
                    current.Patterns.SelectionItem.Pattern.Select();
                    return OperationResult.OkWith(new SelectItemResult(name!));
                }
                matches.Add((current, name!));
            }
            foreach (AutomationElement child in SafeChildren(current))
            {
                queue.Enqueue(child);
            }
        }
        if (matches.Count == 1)
        {
            matches[0].Element.Patterns.SelectionItem.Pattern.Select();
            return OperationResult.OkWith(new SelectItemResult(matches[0].Name));
        }
        return matches.Count == 0
            ? OperationResult.Failure($"No selectable descendant {matcher.Description} found in the located container.")
            : AmbiguousSelection(matcher, "the located container", matches.Select(m => m.Name));
    }

    // Substring and pattern selection is strict: the item must be unique,
    // because selecting the wrong row silently is worse than failing.
    private static OperationResult SelectSingle<TItem>(
        IReadOnlyList<TItem> items,
        Func<TItem, string> nameOf,
        ItemNameMatcher matcher,
        string containerKind,
        Action<TItem> select)
    {
        List<TItem> matches = items.Where(item => matcher.Matches(nameOf(item))).ToList();
        if (matches.Count == 1)
        {
            select(matches[0]);
            return OperationResult.OkWith(new SelectItemResult(nameOf(matches[0])));
        }
        return matches.Count == 0
            ? OperationResult.Failure(
                $"No item {matcher.Description} in {containerKind}. Available items: {DescribeItems(items.Select(nameOf))}")
            : AmbiguousSelection(matcher, containerKind, matches.Select(nameOf));
    }

    private static OperationResult AmbiguousSelection(
        ItemNameMatcher matcher,
        string containerKind,
        IEnumerable<string> names) =>
        OperationResult.Failure(
            $"Several items {matcher.Description} in {containerKind}: {DescribeItems(names)}. " +
            "Narrow itemNameContains or itemNamePattern so it matches one item.");


    private static string DescribeItems(IEnumerable<string> items)
    {
        return string.Join(", ", items.Select(i => $"\"{i}\""));
    }

}
