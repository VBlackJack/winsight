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

using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>Captures and revalidates live UIA identities, not index-based guesses.</summary>
public sealed class UiaSceneDriver : ISceneDriver
{
    private const int MaximumText = 512;
    private const int DragSteps = 12;
    private readonly WindowDiscovery _discovery;

    public UiaSceneDriver(WindowDiscovery? discovery = null)
    {
        _discovery = discovery ?? new WindowDiscovery();
    }

    public SceneCapture Capture(
        int processId,
        string? title,
        int maximum,
        TimeSpan? timeBudget = null,
        TimeProvider? timeProvider = null)
    {
        using UIA3Automation automation = new();
        IReadOnlyList<WindowDescriptor> windows = _discovery.List(processId);
        List<SceneElement> nodes = new();
        TraversalBudget budget = timeBudget is TimeSpan explicitBudget
            ? new TraversalBudget(explicitBudget, UiaDefaults.MaxTraversalNodes, timeProvider)
            : TraversalBudget.FromEnvironment(UiaDefaults.MaxTraversalNodes, timeProvider);
        foreach (WindowRoot root in ElementFinder.ResolveWindows(automation, processId, title))
        {
            if (budget.IsTimeExceeded()) break;
            WindowDescriptor? descriptor = windows.FirstOrDefault(w => w.WindowHandle == root.Handle.ToInt64());
            if (descriptor is null) continue;
            WindowIdentity window = new(processId, descriptor.WindowHandle,
                new WindowBounds(descriptor.X, descriptor.Y, descriptor.Width, descriptor.Height));
            Queue<(AutomationElement Element, string? ParentIdentity)> queue = new();
            queue.Enqueue((root.Element, null));
            while (queue.Count > 0)
            {
                if (!budget.TryStep()) break;
                if (nodes.Count >= maximum)
                {
                    budget.MarkTruncated(TraversalTruncationReasons.MaxResults);
                    break;
                }
                (AutomationElement element, string? parentIdentity) = queue.Dequeue();
                try
                {
                    if (SafeUiaProperties.GetControlType(element) == ControlType.Window &&
                        SafeUiaProperties.GetNativeWindowHandle(element) != root.Handle) continue;
                    SceneElement node = Read(element, window, "e" + (nodes.Count + 1).ToString(CultureInfo.InvariantCulture), parentIdentity);
                    nodes.Add(node);
                    string nodeIdentity = !string.IsNullOrEmpty(node.Identity)
                        ? node.Identity
                        : $"{node.Window.Handle}:{node.Role}:{node.Locator.AutomationId ?? node.Name}";
                    foreach (AutomationElement child in SafeUiaProperties.GetChildren(element)) queue.Enqueue((child, nodeIdentity));
                }
                catch (COMException) { budget.MarkTruncated(TraversalTruncationReasons.ComError); }
                catch (FlaUI.Core.Exceptions.PropertyNotSupportedException) { }
            }
            if (budget.TruncationReason is TraversalTruncationReasons.TimeBudget or TraversalTruncationReasons.NodeLimit) break;
            if (budget.IsTruncated && nodes.Count >= maximum) break;
        }
        return new SceneCapture(windows, nodes, budget.IsTruncated, "uia", TruncationReason: budget.TruncationReason);
    }

    public static SceneElement Read(AutomationElement element, WindowIdentity window, string reference, string? parentIdentity = null)
    {
        string role = SafeUiaProperties.GetControlType(element).ToString();
        string name = SafeUiaProperties.GetName(element);
        bool password = SafeUiaProperties.GetIsPassword(element);
        string? value = password ? null : SafeUiaProperties.GetValue(element);
        Rectangle bounds = SafeUiaProperties.GetBoundingRectangle(element);
        bool isEnabled = SafeUiaProperties.GetIsEnabled(element, fallback: false);
        bool isOffscreen = SafeUiaProperties.GetIsOffscreen(element, fallback: false);
        bool hasKeyboardFocus = SafeUiaProperties.GetHasKeyboardFocus(element);
        bool isKeyboardFocusable = SafeUiaProperties.GetIsKeyboardFocusable(element);
        bool? isSelected = SafeUiaProperties.GetIsSelected(element);
        string? toggleState = SafeUiaProperties.GetToggleState(element);
        string? expandCollapseState = SafeUiaProperties.GetExpandCollapseState(element);
        int[] runtimeId = SafeUiaProperties.GetRuntimeId(element);
        string automationId = SafeUiaProperties.GetAutomationId(element);

        List<string> actions = new();
        if (SafeUiaProperties.Read(() => element.Patterns.Invoke.IsSupported) ||
            SafeUiaProperties.Read(() => element.Patterns.Toggle.IsSupported) ||
            role is "Button" or "MenuItem" or "Hyperlink") actions.Add("activate");
        if (SafeUiaProperties.Read(() => element.Patterns.Value.IsSupported) && !password &&
            !SafeUiaProperties.GetIsValueReadOnly(element)) actions.Add("set_value");
        if (SafeUiaProperties.Read(() => element.Patterns.SelectionItem.IsSupported)) { actions.Add("select"); actions.Add("select_many"); }
        if (SafeUiaProperties.Read(() => element.Patterns.Selection.IsSupported)) actions.Add("select_item");
        if (SafeUiaProperties.Read(() => element.Patterns.Scroll.IsSupported)) actions.Add("scroll");
        if (SafeUiaProperties.Read(() => element.Patterns.ScrollItem.IsSupported)) actions.Add(SceneActionability.ActionScrollIntoView);
        if (SafeUiaProperties.Read(() => element.Patterns.ExpandCollapse.IsSupported)) { actions.Add("expand"); actions.Add("collapse"); }
        if (isKeyboardFocusable) { actions.Add("focus"); actions.Add("key_chord"); }
        if (bounds.Width > 0 && bounds.Height > 0 && !password) { actions.Add(SceneActionability.ActionDragTo); actions.Add(SceneActionability.ActionRightClick); actions.Add(SceneActionability.ActionDoubleClick); }
        ElementLocator locator = new(AutomationId: string.IsNullOrEmpty(automationId) ? null : automationId,
            Name: string.IsNullOrEmpty(automationId) ? name : null, ControlType: role);
        return new SceneElement(reference, "uia", role, Clip(name) ?? string.Empty, Clip(value), isEnabled,
            !isOffscreen, hasKeyboardFocus, isSelected,
            new WindowBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height), actions, window, locator,
            string.Join(".", runtimeId),
            ToggleState: toggleState,
            StateHash: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name + "\0" + value))),
            ExpandCollapseState: expandCollapseState,
            ParentIdentity: parentIdentity);
    }

    /// <summary>Validation and dispatch share the desktop lease. The callback precedes any mutation.</summary>
    public OperationResult Act(SceneElement node, SceneAction action, IReadOnlyList<SceneElement> additional, Action beforeDispatch)
    {
        using DesktopInputLease lease = DesktopInputLease.Acquire();
        using UIA3Automation automation = new();
        AutomationElement? element = Resolve(automation, node);
        if (element is null) return OperationResult.Failure("stale: target identity, state, or geometry changed.");
        List<AutomationElement> extras = new();
        foreach (SceneElement other in additional)
        {
            AutomationElement? resolved = Resolve(automation, other);
            if (resolved is null) return OperationResult.Failure("stale: an additional target changed.");
            extras.Add(resolved);
        }
        if (!node.Actions.Contains(action.Action, StringComparer.Ordinal)) return OperationResult.Failure("unsupported: action is not exposed by this target.");
        if (!node.Visible && action.Action != "scroll_into_view") return OperationResult.Failure("Target is offscreen; scroll_into_view then observe again.");
        if (action.Action == "drag_to" && additional.Count != 1) return OperationResult.Failure("drag_to requires one destination reference.");
        if (action.Action == "select_many" && extras.Any(e => !e.Patterns.SelectionItem.IsSupported)) return OperationResult.Failure("SelectionItem is required on every target.");
        if (action.BackgroundOnly && action.Action == "activate" &&
            !element.Patterns.Invoke.IsSupported && !element.Patterns.Toggle.IsSupported)
        {
            return OperationResult.Failure("backgroundOnly: target does not support UIA Invoke or Toggle pattern (would require pointer click).");
        }
        if (action.Action is "drag_to" or "right_click" or "double_click")
        {
            if (!ValidatePoint(node, out string? error)) return OperationResult.Failure(error!);
            if (action.Action == "drag_to" && !ValidatePoint(additional[0], out error)) return OperationResult.Failure(error!);
        }
        FlaUI.Core.WindowsAPI.VirtualKeyShort[]? parsedChord = null;
        if (action.Action == "key_chord")
        {
            if (!KeyboardChordParser.TryParse(action.Text, out parsedChord, out string? parseError)) return OperationResult.Failure(parseError!);
            if (FocusedElementReader.WebView2EvidenceFor(automation, element) is { Count: > 0 } evidence) return OperationResult.Failure(ActionInvoker.WebView2InputRefusal("The target", evidence, canOverride: false));
        }
        beforeDispatch();

        switch (action.Action)
        {
            case "activate":
                if (element.Patterns.Invoke.PatternOrDefault is { } invoke) invoke.Invoke();
                else if (element.Patterns.Toggle.PatternOrDefault is { } toggle) toggle.Toggle();
                else { if (!ValidatePoint(node, out string? error)) return OperationResult.Failure(error!); if (PointerDispatch.Click(Center(node), ClickStyle.Primary) is { } refused) return refused; }
                break;
            case "set_value": element.Patterns.Value.Pattern.SetValue(action.Text!); break;
            case "select": element.Patterns.SelectionItem.Pattern.Select(); break;
            case "select_many":
                element.Patterns.SelectionItem.Pattern.Select();
                foreach (AutomationElement other in extras) other.Patterns.SelectionItem.Pattern.AddToSelection();
                break;
            case "select_item": return ActionInvoker.SelectPinnedItem(element, action.ItemName!);
            case "expand": element.Patterns.ExpandCollapse.Pattern.Expand(); break;
            case "collapse": element.Patterns.ExpandCollapse.Pattern.Collapse(); break;
            case "scroll_into_view": element.Patterns.ScrollItem.Pattern.ScrollIntoView(); break;
            case "scroll":
                element.Patterns.Scroll.Pattern.Scroll(ScrollAmount.NoAmount,
                action.Text == "up" ? ScrollAmount.LargeDecrement : ScrollAmount.LargeIncrement); break;
            case "focus": element.Focus(); break;
            case "key_chord":
                element.Focus();
                if (!element.Properties.HasKeyboardFocus.ValueOrDefault) return OperationResult.Failure("Focus was not acquired.");
                if (FocusedElementReader.WebView2EvidenceFor(automation, element) is { Count: > 0 } liveEvidence) return OperationResult.Failure(ActionInvoker.WebView2InputRefusal("The target", liveEvidence, canOverride: false));
                ActionInvoker.DispatchKeyboardChord(parsedChord!); break;
            case "right_click": if (PointerDispatch.Click(Center(node), ClickStyle.RightClick) is { } rightRefused) return rightRefused; break;
            case "double_click": if (PointerDispatch.Click(Center(node), ClickStyle.DoubleClick) is { } doubleRefused) return doubleRefused; break;
            case "drag_to":
                Point from = Center(node); Point to = Center(additional[0]);
                Mouse.MoveTo(from);
                Mouse.Down(MouseButton.Left);
                try
                {
                    for (int i = 1; i <= DragSteps; i++)
                    {
                        Mouse.MoveTo(new Point(from.X + (to.X - from.X) * i / DragSteps, from.Y + (to.Y - from.Y) * i / DragSteps));
                        Thread.Sleep(20);
                    }
                }
                finally { Mouse.Up(MouseButton.Left); }
                break;
            default: return OperationResult.Failure("Unsupported action.");
        }
        return OperationResult.Ok();
    }

    private static AutomationElement? Resolve(UIA3Automation automation, SceneElement expected)
    {
        if (string.IsNullOrEmpty(expected.Identity) || !WindowGeometry.TryValidate(expected.Window, out _)) return null;
        Queue<AutomationElement> queue = new();
        queue.Enqueue(automation.FromHandle(new IntPtr(expected.Window.Handle)));
        int count = 0;
        while (queue.Count > 0 && ++count <= UiaDefaults.MaxTraversalNodes)
        {
            AutomationElement current = queue.Dequeue();
            try
            {
                if (string.Join(".", SafeUiaProperties.GetRuntimeId(current)) == expected.Identity)
                {
                    SceneElement actual = Read(current, expected.Window, expected.Ref);
                    return actual.Name == expected.Name && actual.Role == expected.Role && actual.Value == expected.Value &&
                        actual.Bounds == expected.Bounds && actual.Selected == expected.Selected &&
                        actual.ToggleState == expected.ToggleState && actual.StateHash == expected.StateHash && actual.Enabled ? current : null;
                }
                foreach (AutomationElement child in SafeUiaProperties.GetChildren(current)) queue.Enqueue(child);
            }
            catch (Exception ex) when (SafeUiaProperties.IsIgnorableUiaException(ex))
            {
                // Transient element disappearance or unsupported properties during traversal should not abort resolution.
            }
        }
        return null;
    }

    private static Point Center(SceneElement node) => new(node.Bounds.Left + node.Bounds.Width / 2, node.Bounds.Top + node.Bounds.Height / 2);
    private static bool ValidatePoint(SceneElement node, out string? error) => WindowGeometry.TryValidatePointRecipient(node.Window, Center(node).X, Center(node).Y, false, out error);
    private static string? Clip(string? value) => value?.Length > MaximumText ? value[..MaximumText] : value;
}
