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
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Executes user-level actions (click, type, select) on elements located
/// through the strict <see cref="ElementFinder"/>. Elements that are
/// off-screen are scrolled into view before any physical mouse action.
/// </summary>
public sealed class ActionInvoker
{
    /// <summary>
    /// Click the located element. <see cref="ClickStyle.Primary"/> prefers the
    /// Invoke pattern, then Toggle, then a physical left click; DoubleClick
    /// and RightClick are always physical mouse actions at the element's location.
    /// </summary>
    public OperationResult Click(
        int processId,
        ElementLocator locator,
        ClickStyle style = ClickStyle.Primary,
        string? windowTitle = null)
    {
        using UIA3Automation automation = new();
        ElementSearchResult search = ElementFinder.FindSingle(automation, processId, locator, windowTitle);
        if (!search.Found)
        {
            return OperationResult.FailureWith(search.Error!, search.Candidates);
        }

        AutomationElement element = search.Element!;
        ScrollIntoViewIfOffscreen(element);

        switch (style)
        {
            case ClickStyle.DoubleClick:
                element.DoubleClick();
                return OperationResult.Ok();

            case ClickStyle.RightClick:
                element.RightClick();
                return OperationResult.Ok();

            default:
                return InvokePrimaryAction(element);
        }
    }

    /// <summary>
    /// Set the located element's value via the Value pattern, falling back
    /// to focusing the element and typing through the keyboard.
    /// </summary>
    public OperationResult Type(
        int processId,
        ElementLocator locator,
        string text,
        string? windowTitle = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        using UIA3Automation automation = new();
        ElementSearchResult search = ElementFinder.FindSingle(automation, processId, locator, windowTitle);
        if (!search.Found)
        {
            return OperationResult.FailureWith(search.Error!, search.Candidates);
        }

        AutomationElement element = search.Element!;
        ScrollIntoViewIfOffscreen(element);

        FlaUI.Core.Patterns.IValuePattern? valuePattern = element.Patterns.Value.PatternOrDefault;
        if (valuePattern is not null && !valuePattern.IsReadOnly.ValueOrDefault)
        {
            valuePattern.SetValue(text);
            return OperationResult.Ok();
        }

        element.Focus();
        Keyboard.Type(text);
        return OperationResult.Ok();
    }

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
            return controlType switch
            {
                "ComboBox" => SelectInComboBox(container, itemName),
                "List" => SelectInListBox(container, itemName),
                "Tab" => SelectTabItem(container, itemName),
                _ => SelectDescendantItem(container, itemName),
            };
        }
        catch (COMException ex)
        {
            return OperationResult.Failure(
                $"Selection of \"{itemName}\" failed in {controlType} ({ex.Message}).");
        }
    }

    private static OperationResult InvokePrimaryAction(AutomationElement element)
    {
        FlaUI.Core.Patterns.IInvokePattern? invokePattern = element.Patterns.Invoke.PatternOrDefault;
        if (invokePattern is not null)
        {
            invokePattern.Invoke();
            return OperationResult.Ok();
        }

        FlaUI.Core.Patterns.ITogglePattern? togglePattern = element.Patterns.Toggle.PatternOrDefault;
        if (togglePattern is not null)
        {
            togglePattern.Toggle();
            return OperationResult.Ok();
        }

        element.Click();
        return OperationResult.Ok();
    }

    private static OperationResult SelectInComboBox(AutomationElement container, string itemName)
    {
        ComboBox comboBox = container.AsComboBox();
        ComboBoxItem? selected = comboBox.Select(itemName);
        if (selected is null)
        {
            return OperationResult.Failure(
                $"Item \"{itemName}\" not found in ComboBox. Available items: {DescribeItems(comboBox.Items.Select(i => i.Text))}");
        }
        // Collapse the dropdown so the popup does not linger over the UI.
        comboBox.Collapse();
        return OperationResult.Ok();
    }

    private static OperationResult SelectInListBox(AutomationElement container, string itemName)
    {
        ListBox listBox = container.AsListBox();
        ListBoxItem? selected = listBox.Select(itemName);
        if (selected is null)
        {
            return OperationResult.Failure(
                $"Item \"{itemName}\" not found in ListBox. Available items: {DescribeItems(listBox.Items.Select(i => i.Text))}");
        }
        return OperationResult.Ok();
    }

    private static OperationResult SelectTabItem(AutomationElement container, string itemName)
    {
        Tab tab = container.AsTab();
        TabItem? target = tab.TabItems.FirstOrDefault(
            t => string.Equals(t.Properties.Name.ValueOrDefault, itemName, StringComparison.Ordinal));
        if (target is null)
        {
            return OperationResult.Failure(
                $"Tab item \"{itemName}\" not found. Available items: {DescribeItems(tab.TabItems.Select(t => t.Properties.Name.ValueOrDefault ?? string.Empty))}");
        }
        tab.SelectTabItem(itemName);
        return OperationResult.Ok();
    }

    private static OperationResult SelectDescendantItem(AutomationElement container, string itemName)
    {
        Queue<AutomationElement> queue = new();
        queue.Enqueue(container);
        while (queue.Count > 0)
        {
            AutomationElement current = queue.Dequeue();
            FlaUI.Core.Patterns.ISelectionItemPattern? selectionItem =
                current.Patterns.SelectionItem.PatternOrDefault;
            if (selectionItem is not null &&
                string.Equals(current.Properties.Name.ValueOrDefault, itemName, StringComparison.Ordinal))
            {
                selectionItem.Select();
                return OperationResult.Ok();
            }
            foreach (AutomationElement child in current.FindAllChildren())
            {
                queue.Enqueue(child);
            }
        }
        return OperationResult.Failure(
            $"No selectable descendant named \"{itemName}\" found in the located container.");
    }

    private static void ScrollIntoViewIfOffscreen(AutomationElement element)
    {
        try
        {
            if (element.Properties.IsOffscreen.ValueOrDefault)
            {
                element.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
            }
        }
        catch (COMException)
        {
            // Scrolling is best-effort; the action itself will surface a
            // real failure if the element stays unreachable.
        }
    }

    private static string DescribeItems(IEnumerable<string> items)
    {
        return string.Join(", ", items.Select(i => $"\"{i}\""));
    }
}
