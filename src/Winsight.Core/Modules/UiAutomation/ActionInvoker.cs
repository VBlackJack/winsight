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
using FlaUIControlType = FlaUI.Core.Definitions.ControlType;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Executes user-level actions (click, type, select) on elements located
/// through the strict <see cref="ElementFinder"/>. Elements that are
/// off-screen are scrolled into view before any physical mouse action.
/// </summary>
public sealed class ActionInvoker
{
    private static readonly object DesktopInputGate = new();
    private const int DefaultTooltipTimeoutMs = 1500;
    private const int MaxTooltipTimeoutMs = 10_000;
    private const int TooltipPollIntervalMs = 50;
    private const int TooltipProximityPx = 400;
    private const int TooltipTextDepth = 3;
    private const int PreHoverSettleMs = 150;
    private const int FocusInputSettleMs = 100;
    private const int SystemMetricDragWidth = 68;
    private const int SystemMetricDragHeight = 69;

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
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
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
    }

    /// <summary>
    /// Click at a coordinate without locating an element through UI Automation.
    /// This is the escape hatch for surfaces UIA cannot see (custom Canvas,
    /// SkiaSharp, DirectX), and the action partner of <c>find_element_visually</c>.
    /// When <paramref name="windowRelative"/> is true the point is interpreted in
    /// the target window's coordinate space (same origin as a full-window
    /// screenshot) and translated to screen space; otherwise it is treated as an
    /// absolute screen coordinate.
    /// </summary>
    public OperationResult ClickPoint(
        int processId,
        int x,
        int y,
        ClickStyle style = ClickStyle.Primary,
        string? windowTitle = null,
        bool windowRelative = true,
        bool allowOutOfWindow = false,
        WindowIdentity? expectedWindow = null)
    {
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            return ClickPointCore(processId, x, y, style, windowTitle, windowRelative, allowOutOfWindow, expectedWindow);
        }
    }

    private static OperationResult ClickPointCore(
        int processId, int x, int y, ClickStyle style, string? windowTitle,
        bool windowRelative, bool allowOutOfWindow, WindowIdentity? expectedWindow)
    {
        WindowIdentity window;
        if (expectedWindow is not null)
        {
            if (expectedWindow.ProcessId != processId)
            {
                return OperationResult.Failure("The captured window belongs to a different process.");
            }
            window = expectedWindow;
        }
        else if (!WindowGeometry.TryResolveWindow(
                     processId, windowTitle, out window, out string? resolveError,
                     screenPoint: !windowRelative && !allowOutOfWindow ? new System.Drawing.Point(x, y) : null))
        {
            return OperationResult.Failure(resolveError!);
        }
        int screenX;
        int screenY;
        if (windowRelative)
        {
            WindowBounds bounds = window.Bounds;
            if (!WindowPointMapper.Contains(bounds.Width, bounds.Height, x, y))
            {
                return OperationResult.Failure(
                    $"Window-relative point ({x}, {y}) is outside the target window bounds " +
                    $"{bounds.Width}x{bounds.Height}. Pass coordinates within the window, or use " +
                    "screen-space coordinates.");
            }
            (screenX, screenY) = WindowPointMapper.ToScreen(bounds.Left, bounds.Top, x, y);
        }
        else
        {
            screenX = x;
            screenY = y;
            if (!allowOutOfWindow)
            {
                if (!WindowGeometry.TryResolveBoundsContainingPoint(
                        processId,
                        windowTitle,
                        screenX,
                        screenY,
                        out _,
                        out string? error))
                {
                    return OperationResult.Failure(error!);
                }
            }
        }

        System.Drawing.Point point = new(screenX, screenY);
        // Recheck immediately before dispatch, after all coordinate validation.
        if (!WindowGeometry.TryValidatePointRecipient(
                window, screenX, screenY, allowOutOfWindow && expectedWindow is null, out string? recipientError))
        {
            return OperationResult.Failure(recipientError!);
        }
        switch (style)
        {
            case ClickStyle.DoubleClick:
                Mouse.DoubleClick(point, MouseButton.Left);
                break;
            case ClickStyle.RightClick:
                Mouse.Click(point, MouseButton.Right);
                break;
            default:
                Mouse.Click(point, MouseButton.Left);
                break;
        }

        return OperationResult.OkWith(new ClickPointResult(screenX, screenY));
    }

    /// <summary>
    /// Press the left button on <paramref name="source"/>, hold it, move in
    /// paced steps to <paramref name="target"/> and release, so OLE drag and
    /// drop (WPF <c>DragDrop.DoDragDrop</c>) starts, tracks and drops. Each
    /// endpoint is an element (center plus offset) or a point; points are
    /// window-relative when <paramref name="windowRelative"/> is true. Both
    /// points must reach a window of the target process unless
    /// <paramref name="allowOutOfWindow"/> releases the target check.
    /// </summary>
    public OperationResult Drag(
        int processId,
        DragEndpoint source,
        DragEndpoint target,
        DragOptions? options = null,
        string? windowTitle = null,
        bool windowRelative = true,
        bool allowOutOfWindow = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        DragOptions pacing = options ?? new DragOptions();
        string? invalid = source.Validate("source") ?? target.Validate("target") ?? pacing.Validate();
        if (invalid is not null)
        {
            return OperationResult.Failure(invalid);
        }

        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            using UIA3Automation automation = new();
            if (!TryResolveDragPoint(automation, processId, source, "source", windowTitle, windowRelative,
                    out System.Drawing.Point from, out OperationResult? failure) ||
                !TryResolveDragPoint(automation, processId, target, "target", windowTitle, windowRelative,
                    out System.Drawing.Point to, out failure))
            {
                return failure!;
            }
            if (!TryValidateDragRecipient(processId, from, "source", out string? recipientError) ||
                (!allowOutOfWindow && !TryValidateDragRecipient(processId, to, "target", out recipientError)))
            {
                return OperationResult.Failure(recipientError!);
            }

            int threshold = Math.Max(
                GetSystemMetrics(SystemMetricDragWidth),
                GetSystemMetrics(SystemMetricDragHeight));
            IReadOnlyList<System.Drawing.Point> moves = DragPath.Plan(from, to, pacing.Steps, threshold);

            Mouse.Position = from;
            Thread.Sleep(pacing.StepDelayMs);
            Mouse.Down(MouseButton.Left);
            try
            {
                Thread.Sleep(pacing.HoldMs);
                foreach (System.Drawing.Point move in moves)
                {
                    Mouse.Position = move;
                    Thread.Sleep(pacing.StepDelayMs);
                }
                Thread.Sleep(pacing.DropDelayMs);
            }
            finally
            {
                Mouse.Up(MouseButton.Left);
            }
            return OperationResult.OkWith(new DragResult(from.X, from.Y, to.X, to.Y, moves.Count));
        }
    }

    private static bool TryResolveDragPoint(
        UIA3Automation automation,
        int processId,
        DragEndpoint endpoint,
        string role,
        string? windowTitle,
        bool windowRelative,
        out System.Drawing.Point point,
        out OperationResult? failure)
    {
        point = default;
        failure = null;
        if (!endpoint.IsPoint)
        {
            ElementSearchResult search = ElementFinder.FindSingle(automation, processId, endpoint.Locator!, windowTitle);
            if (!search.Found)
            {
                failure = OperationResult.FailureWith($"Drag {role}: {search.Error}", search.Candidates);
                return false;
            }
            AutomationElement element = search.Element!;
            ScrollIntoViewIfOffscreen(element);
            System.Drawing.Rectangle rect = element.BoundingRectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                failure = OperationResult.Failure(
                    $"Drag {role} element has invalid bounds {rect.Width}x{rect.Height}.");
                return false;
            }
            point = new System.Drawing.Point(
                rect.Left + rect.Width / 2 + endpoint.OffsetX,
                rect.Top + rect.Height / 2 + endpoint.OffsetY);
            return true;
        }

        if (!windowRelative)
        {
            point = new System.Drawing.Point(endpoint.X!.Value, endpoint.Y!.Value);
            return true;
        }
        if (!WindowGeometry.TryResolveWindow(processId, windowTitle, out WindowIdentity window, out string? error))
        {
            failure = OperationResult.Failure($"Drag {role}: {error}");
            return false;
        }
        WindowBounds bounds = window.Bounds;
        if (!WindowPointMapper.Contains(bounds.Width, bounds.Height, endpoint.X!.Value, endpoint.Y!.Value))
        {
            failure = OperationResult.Failure(
                $"Drag {role} point ({endpoint.X}, {endpoint.Y}) is outside the target window bounds " +
                $"{bounds.Width}x{bounds.Height}.");
            return false;
        }
        (int screenX, int screenY) = WindowPointMapper.ToScreen(
            bounds.Left, bounds.Top, endpoint.X.Value, endpoint.Y.Value);
        point = new System.Drawing.Point(screenX, screenY);
        return true;
    }

    private static bool TryValidateDragRecipient(
        int processId,
        System.Drawing.Point point,
        string role,
        out string? error)
    {
        if (!WindowGeometry.TryResolveWindow(processId, null, out WindowIdentity window, out error, point) ||
            !WindowGeometry.TryValidatePointRecipient(window, point.X, point.Y, allowOtherWindow: false, out error))
        {
            error = $"Drag {role} ({point.X}, {point.Y}): {error}";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Set the located element's value via the Value pattern, falling back
    /// to focusing the element and typing through the keyboard. An element
    /// inside a WebView2 surface is refused unless
    /// <paramref name="allowWebView2Input"/> is set (see <see cref="WebView2FocusClassifier"/>).
    /// </summary>
    public OperationResult Type(
        int processId,
        ElementLocator locator,
        string text,
        string? windowTitle = null,
        bool focusWindow = false,
        bool allowWebView2Input = false)
    {
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            ArgumentNullException.ThrowIfNull(text);

            using UIA3Automation automation = new();
            ElementSearchResult search = ElementFinder.FindSingle(automation, processId, locator, windowTitle);
            if (!search.Found)
            {
                return OperationResult.FailureWith(search.Error!, search.Candidates);
            }

            AutomationElement element = search.Element!;
            if (!allowWebView2Input &&
                FocusedElementReader.WebView2EvidenceFor(automation, element) is { Count: > 0 } evidence)
            {
                return OperationResult.Failure(WebView2InputRefusal("The located element", evidence));
            }
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
    }

    /// <summary>
    /// Focus a process window, verify it became the foreground window, and
    /// dispatch a keyboard chord such as "Ctrl+Shift+N". The chord is refused
    /// when keyboard focus then sits inside a WebView2 surface, unless
    /// <paramref name="allowWebView2Input"/> is set. The result reports the
    /// element that received the chord.
    /// </summary>
    public OperationResult SendKeyChord(
        int processId,
        string chord,
        string? windowTitle = null,
        bool allowWebView2Input = false)
    {
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            if (!KeyboardChordParser.TryParse(chord, out VirtualKeyShort[]? keys, out string? parseError))
            {
                return OperationResult.Failure(parseError!);
            }

            WindowFocusResult focus = WindowFocus.FocusProcessWindow(processId, windowTitle);
            if (!focus.Success)
            {
                return OperationResult.Failure(focus.Error!);
            }

            Thread.Sleep(FocusInputSettleMs);
            FocusedElementInfo? focused;
            using (UIA3Automation automation = new())
            {
                focused = FocusedElementReader.ReadFocus(automation, processId);
            }
            if (!allowWebView2Input && focused is { IsWebView2: true })
            {
                return OperationResult.FailureWith(
                    WebView2InputRefusal("Keyboard focus", focused.WebView2Evidence),
                    focused);
            }
            DispatchKeyboardChord(keys);
            return OperationResult.OkWith(new KeyChordResult(
                chord,
                focus.WindowTitle,
                focus.WindowHandle.ToInt64(),
                focused));
        }
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

    /// <summary>
    /// Move the mouse to the center of an element and capture the newly
    /// appearing tooltip, optionally asserting its text. The tooltip popup
    /// window is awaited for the whole timeout; the element's HelpText is only
    /// a fallback when no popup opens, and the snapshot says which one it is.
    /// </summary>
    public OperationResult Hover(
        int processId,
        ElementLocator locator,
        string? expectedTooltipText = null,
        string? windowTitle = null,
        int? timeoutMs = null)
    {
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
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

            HashSet<string> staleTooltips = CaptureTooltips(automation, processId)
                .Select(t => TooltipSignature(t.Snapshot))
                .ToHashSet(StringComparer.Ordinal);

            Mouse.MoveTo(center);

            string? helpText = SafeReadHelpText(element);
            Stopwatch stopwatch = Stopwatch.StartNew();
            TooltipSnapshot? lastTooltip = null;
            while (stopwatch.ElapsedMilliseconds <= timeout)
            {
                List<TooltipSnapshot> fresh = CaptureTooltips(automation, processId)
                    .Where(t => !staleTooltips.Contains(TooltipSignature(t.Snapshot)))
                    .Where(t => t.DistanceTo(center) <= TooltipProximityPx)
                    .OrderBy(t => t.DistanceTo(center))
                    .Select(t => t.Snapshot)
                    .ToList();
                TooltipSnapshot? tooltip = fresh.FirstOrDefault(t => TooltipTextMatches(t, expectedTooltipText));
                if (tooltip is not null)
                {
                    return OperationResult.OkWith(new HoverResult(center.X, center.Y, tooltip));
                }

                lastTooltip = fresh.FirstOrDefault() ?? lastTooltip;
                Thread.Sleep(TooltipPollIntervalMs);
            }

            // No popup carried the text: fall back to the element's HelpText,
            // which assistive technology reads but which is not what a user sees.
            if (lastTooltip is null && TooltipTextMatches(helpText, expectedTooltipText))
            {
                TooltipSnapshot fromHelpText = new(
                    helpText!,
                    rect.X,
                    rect.Y,
                    rect.Width,
                    rect.Height,
                    TooltipSnapshot.SourceHelpText);
                return OperationResult.OkWith(new HoverResult(center.X, center.Y, fromHelpText));
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

    internal static string WebView2InputRefusal(
        string subject,
        IReadOnlyList<string> evidence,
        bool canOverride = true) =>
        $"{subject} is inside a WebView2 surface ({string.Join(", ", evidence)}). Synthetic keyboard input " +
        "there has crashed the text input host (ctfmon), so it was not sent. Use type_into_webview (CDP) " +
        "for web content" + (canOverride ? ", or pass allowWebView2Input=true to send it anyway." : ".");

    internal static void DispatchKeyboardChord(FlaUI.Core.WindowsAPI.VirtualKeyShort[] keys) =>
        KeyChordDispatcher.Dispatch(keys);

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

    // Tooltips of the target live in its own transient top-level windows (a
    // WPF Popup hosting a ToolTip, or a tooltips_class32 window); searching
    // only those keeps each poll cheap and ignores other applications.
    private static List<TooltipCandidate> CaptureTooltips(UIA3Automation automation, int processId)
    {
        List<TooltipCandidate> candidates = new();
        foreach ((IntPtr handle, _) in NativeWindowEnumerator.VisibleTopLevelWindows(processId))
        {
            if (!NativeWindowEnumerator.IsTransient(handle))
            {
                continue;
            }
            try
            {
                AutomationElement root = automation.FromHandle(handle);
                IEnumerable<AutomationElement> tooltips = root.Properties.ControlType.ValueOrDefault == FlaUIControlType.ToolTip
                    ? new[] { root }
                    : root.FindAllDescendants(cf => cf.ByControlType(FlaUIControlType.ToolTip));
                candidates.AddRange(tooltips
                    .Select(t => TryBuildTooltipCandidate(t, handle))
                    .Where(t => t is not null)
                    .Cast<TooltipCandidate>());
            }
            catch (COMException)
            {
                // The popup closed between enumeration and attachment.
            }
        }
        return candidates;
    }

    private static TooltipCandidate? TryBuildTooltipCandidate(AutomationElement tooltip, IntPtr windowHandle)
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
                rect.Height,
                TooltipSnapshot.SourcePopup,
                windowHandle.ToInt64()));
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

    private static bool TooltipTextMatches(string? tooltipText, string? expectedTooltipText)
    {
        return !string.IsNullOrEmpty(tooltipText) &&
            (expectedTooltipText is null ||
                tooltipText.Contains(expectedTooltipText, StringComparison.OrdinalIgnoreCase));
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

    private static string? SafeReadHelpText(AutomationElement element)
    {
        try
        {
            return element.HelpText;
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

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
