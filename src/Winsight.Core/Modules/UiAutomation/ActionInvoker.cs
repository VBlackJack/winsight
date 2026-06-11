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
using FlaUI.UIA3;
using Winsight.Core.Models;
using FlaUIControlType = FlaUI.Core.Definitions.ControlType;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Executes user-level actions (click, type, select) on elements located
/// through the strict <see cref="ElementFinder"/>. Elements that are
/// off-screen are scrolled into view before any physical mouse action.
/// </summary>
public sealed class ActionInvoker
{
    private const int DefaultTooltipTimeoutMs = 1500;
    private const int MaxTooltipTimeoutMs = 10_000;
    private const int TooltipPollIntervalMs = 50;
    private const int TooltipProximityPx = 400;
    private const int TooltipTextDepth = 3;
    private const int PreHoverSettleMs = 150;
    private const int FocusInputSettleMs = 100;
    private const int KeyChordStrokeDelayMs = 20;

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
        string? windowTitle = null,
        bool focusWindow = false)
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
        if (focusWindow)
        {
            WindowFocusResult focus = WindowFocus.FocusWindow(
                search.Match!.WindowHandle,
                search.Match.Candidate.WindowTitle,
                processId);
            if (!focus.Success)
            {
                return OperationResult.Failure(focus.Error!);
            }
        }

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
    /// Focus a process window, verify it became the foreground window, and
    /// dispatch a keyboard chord such as "Ctrl+Shift+N".
    /// </summary>
    public OperationResult SendKeyChord(
        int processId,
        string chord,
        string? windowTitle = null)
    {
        if (!KeyboardChordParser.TryParse(chord, out var keys, out string? parseError))
        {
            return OperationResult.Failure(parseError!);
        }

        WindowFocusResult focus = WindowFocus.FocusProcessWindow(processId, windowTitle);
        if (!focus.Success)
        {
            return OperationResult.Failure(focus.Error!);
        }

        Thread.Sleep(FocusInputSettleMs);
        DispatchKeyboardChord(keys);
        return OperationResult.OkWith(new KeyChordResult(
            chord,
            focus.WindowTitle,
            focus.WindowHandle.ToInt64()));
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

    /// <summary>
    /// Move the mouse to the center of an element and capture the newly
    /// appearing tooltip, optionally asserting its text.
    /// </summary>
    public OperationResult Hover(
        int processId,
        ElementLocator locator,
        string? expectedTooltipText = null,
        string? windowTitle = null,
        int? timeoutMs = null)
    {
        int timeout = timeoutMs ?? DefaultTooltipTimeoutMs;
        if (timeout <= 0 || timeout > MaxTooltipTimeoutMs)
        {
            return OperationResult.Failure(
                $"timeoutMs must be between 1 and {MaxTooltipTimeoutMs}.");
        }

        using UIA3Automation automation = new();
        ElementSearchResult search = ElementFinder.FindSingle(automation, processId, locator, windowTitle);
        if (!search.Found)
        {
            return OperationResult.FailureWith(search.Error!, search.Candidates);
        }

        AutomationElement element = search.Element!;
        ScrollIntoViewIfOffscreen(element);
        System.Drawing.Rectangle rect = element.BoundingRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return OperationResult.Failure(
                $"Located element has invalid bounds {rect.Width}x{rect.Height}.");
        }

        System.Drawing.Point center = new(
            rect.Left + rect.Width / 2,
            rect.Top + rect.Height / 2);
        System.Drawing.Point outsideElement = new(
            Math.Max(0, rect.Left - 24),
            Math.Max(0, rect.Top - 24));
        Mouse.MoveTo(outsideElement);
        Thread.Sleep(PreHoverSettleMs);

        HashSet<string> staleTooltips = CaptureTooltips(automation)
            .Select(t => TooltipSignature(t.Snapshot))
            .ToHashSet(StringComparer.Ordinal);

        Mouse.MoveTo(center);

        Stopwatch stopwatch = Stopwatch.StartNew();
        TooltipSnapshot? lastTooltip = null;
        while (stopwatch.ElapsedMilliseconds <= timeout)
        {
            TooltipSnapshot? tooltip = CaptureTooltips(automation)
                .Where(t => !staleTooltips.Contains(TooltipSignature(t.Snapshot)))
                .Where(t => t.DistanceTo(center) <= TooltipProximityPx)
                .OrderBy(t => t.DistanceTo(center))
                .Select(t => t.Snapshot)
                .FirstOrDefault(t => TooltipTextMatches(t, expectedTooltipText));

            if (tooltip is not null)
            {
                return OperationResult.OkWith(new HoverResult(center.X, center.Y, tooltip));
            }

            lastTooltip = CaptureTooltips(automation)
                .Where(t => !staleTooltips.Contains(TooltipSignature(t.Snapshot)))
                .Where(t => t.DistanceTo(center) <= TooltipProximityPx)
                .OrderBy(t => t.DistanceTo(center))
                .Select(t => t.Snapshot)
                .FirstOrDefault();
            Thread.Sleep(TooltipPollIntervalMs);
        }

        HoverResult result = new(center.X, center.Y, lastTooltip);
        if (expectedTooltipText is not null)
        {
            return OperationResult.FailureWith(
                $"Tooltip containing \"{expectedTooltipText}\" did not appear within {timeout} ms " +
                $"near hover point ({center.X}, {center.Y}).",
                result);
        }

        return OperationResult.OkWith(result);
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

    private static void DispatchKeyboardChord(FlaUI.Core.WindowsAPI.VirtualKeyShort[] keys)
    {
        FlaUI.Core.WindowsAPI.VirtualKeyShort[] modifiers = keys
            .Where(KeyboardChordParser.IsModifier)
            .ToArray();
        FlaUI.Core.WindowsAPI.VirtualKeyShort[] strokes = keys
            .Where(k => !KeyboardChordParser.IsModifier(k))
            .ToArray();

        if (modifiers.Length == 0)
        {
            Keyboard.Type(strokes);
            return;
        }

        using IDisposable pressed = Keyboard.Pressing(modifiers);
        Thread.Sleep(KeyChordStrokeDelayMs);
        Keyboard.Type(strokes);
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

    private sealed record TooltipCandidate(TooltipSnapshot Snapshot)
    {
        internal int DistanceTo(System.Drawing.Point point)
        {
            int dx = point.X < Snapshot.X
                ? Snapshot.X - point.X
                : point.X > Snapshot.X + Snapshot.Width
                    ? point.X - (Snapshot.X + Snapshot.Width)
                    : 0;
            int dy = point.Y < Snapshot.Y
                ? Snapshot.Y - point.Y
                : point.Y > Snapshot.Y + Snapshot.Height
                    ? point.Y - (Snapshot.Y + Snapshot.Height)
                    : 0;
            return (int)Math.Round(Math.Sqrt(dx * dx + dy * dy));
        }
    }

    private static IReadOnlyList<TooltipCandidate> CaptureTooltips(UIA3Automation automation)
    {
        try
        {
            AutomationElement[] elements = automation
                .GetDesktop()
                .FindAllDescendants(cf => cf.ByControlType(FlaUIControlType.ToolTip));
            return elements
                .Select(TryBuildTooltipCandidate)
                .Where(t => t is not null)
                .Cast<TooltipCandidate>()
                .ToList();
        }
        catch (COMException)
        {
            return Array.Empty<TooltipCandidate>();
        }
    }

    private static TooltipCandidate? TryBuildTooltipCandidate(AutomationElement tooltip)
    {
        try
        {
            System.Drawing.Rectangle rect = tooltip.BoundingRectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return null;
            }

            string text = ReadTooltipText(tooltip);
            return new TooltipCandidate(new TooltipSnapshot(
                text,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height));
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string ReadTooltipText(AutomationElement tooltip)
    {
        List<string> texts = new();
        AddDistinctText(texts, SafeReadName(tooltip));
        AddDistinctText(texts, SafeReadValue(tooltip));

        Queue<(AutomationElement Element, int Depth)> queue = new();
        foreach (AutomationElement child in SafeChildren(tooltip))
        {
            queue.Enqueue((child, 1));
        }
        while (queue.Count > 0)
        {
            (AutomationElement current, int depth) = queue.Dequeue();
            AddDistinctText(texts, SafeReadName(current));
            AddDistinctText(texts, SafeReadValue(current));
            if (depth >= TooltipTextDepth)
            {
                continue;
            }
            foreach (AutomationElement child in SafeChildren(current))
            {
                queue.Enqueue((child, depth + 1));
            }
        }

        return string.Join(" ", texts);
    }

    private static bool TooltipTextMatches(TooltipSnapshot tooltip, string? expectedTooltipText)
    {
        return expectedTooltipText is null ||
            tooltip.Text.Contains(expectedTooltipText, StringComparison.OrdinalIgnoreCase);
    }

    private static string TooltipSignature(TooltipSnapshot tooltip)
    {
        return string.Join(
            "|",
            tooltip.Text,
            tooltip.X.ToString(CultureInfo.InvariantCulture),
            tooltip.Y.ToString(CultureInfo.InvariantCulture),
            tooltip.Width.ToString(CultureInfo.InvariantCulture),
            tooltip.Height.ToString(CultureInfo.InvariantCulture));
    }

    private static string? SafeReadName(AutomationElement element)
    {
        try
        {
            return element.Properties.Name.ValueOrDefault;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string? SafeReadValue(AutomationElement element)
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

    private static void AddDistinctText(List<string> texts, string? text)
    {
        if (!string.IsNullOrEmpty(text) &&
            !texts.Contains(text, StringComparer.Ordinal))
        {
            texts.Add(text);
        }
    }

    private static string DescribeItems(IEnumerable<string> items)
    {
        return string.Join(", ", items.Select(i => $"\"{i}\""));
    }
}
