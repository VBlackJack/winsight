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
public sealed class UiaSceneDriver
{
    private const int MaximumText = 512;
    private const int DragSteps = 12;
    private readonly WindowDiscovery _discovery = new();

    public SceneCapture Capture(int processId, string? title, int maximum)
    {
        using UIA3Automation automation = new();
        IReadOnlyList<WindowDescriptor> windows = _discovery.List(processId);
        List<SceneElement> nodes = new();
        int visited = 0;
        bool truncated = false;
        foreach (WindowRoot root in ElementFinder.ResolveWindows(automation, processId, title))
        {
            WindowDescriptor? descriptor = windows.FirstOrDefault(w => w.WindowHandle == root.Handle.ToInt64());
            if (descriptor is null) continue;
            WindowIdentity window = new(processId, descriptor.WindowHandle,
                new WindowBounds(descriptor.X, descriptor.Y, descriptor.Width, descriptor.Height));
            Queue<AutomationElement> queue = new();
            queue.Enqueue(root.Element);
            while (queue.Count > 0)
            {
                if (++visited > UiaDefaults.MaxTraversalNodes || nodes.Count >= maximum) { truncated = true; break; }
                AutomationElement element = queue.Dequeue();
                try
                {
                    if (element.Properties.ControlType.ValueOrDefault == ControlType.Window && element.Properties.NativeWindowHandle.ValueOrDefault != root.Handle) continue;
                    SceneElement node = Read(element, window, "e" + (nodes.Count + 1).ToString(CultureInfo.InvariantCulture));
                    nodes.Add(node);
                    foreach (AutomationElement child in element.FindAllChildren()) queue.Enqueue(child);
                }
                catch (COMException) { truncated = true; }
            }
            if (truncated && nodes.Count >= maximum) break;
        }
        return new SceneCapture(windows, nodes, truncated, "uia");
    }

    public static SceneElement Read(AutomationElement element, WindowIdentity window, string reference)
    {
        string role = element.Properties.ControlType.ValueOrDefault.ToString();
        string name = element.Properties.Name.ValueOrDefault ?? "";
        bool password = element.Properties.IsPassword.ValueOrDefault;
        string? value = password ? null : element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault;
        Rectangle bounds = element.BoundingRectangle;
        List<string> actions = new();
        if (element.Patterns.Invoke.IsSupported || element.Patterns.Toggle.IsSupported || role is "Button" or "MenuItem" or "Hyperlink") actions.Add("activate");
        if (element.Patterns.Value.IsSupported && !password && !element.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault) actions.Add("set_value");
        if (element.Patterns.SelectionItem.IsSupported) { actions.Add("select"); actions.Add("select_many"); }
        if (element.Patterns.Selection.IsSupported) actions.Add("select_item");
        if (element.Patterns.Scroll.IsSupported) actions.Add("scroll");
        if (element.Patterns.ScrollItem.IsSupported) actions.Add("scroll_into_view");
        if (element.Patterns.ExpandCollapse.IsSupported) { actions.Add("expand"); actions.Add("collapse"); }
        if (element.Properties.IsKeyboardFocusable.ValueOrDefault) { actions.Add("focus"); actions.Add("key_chord"); }
        if (bounds.Width > 0 && bounds.Height > 0 && !password) { actions.Add("drag_to"); actions.Add("right_click"); actions.Add("double_click"); }
        string? id = element.Properties.AutomationId.ValueOrDefault;
        ElementLocator locator = new(AutomationId: string.IsNullOrEmpty(id) ? null : id,
            Name: string.IsNullOrEmpty(id) ? name : null, ControlType: role);
        return new SceneElement(reference, "uia", role, Clip(name) ?? string.Empty, Clip(value), element.IsEnabled,
            !element.IsOffscreen, element.Properties.HasKeyboardFocus.ValueOrDefault,
            element.Patterns.SelectionItem.PatternOrDefault?.IsSelected.ValueOrDefault,
            new WindowBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height), actions, window, locator,
            string.Join(".", element.Properties.RuntimeId.ValueOrDefault ?? []),
            ToggleState: element.Patterns.Toggle.PatternOrDefault?.ToggleState.ValueOrDefault.ToString(),
            StateHash: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name + "\0" + value))));
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
        if (action.Action is "drag_to" or "right_click" or "double_click")
        {
            if (!ValidatePoint(node, out string? error)) return OperationResult.Failure(error!);
            if (action.Action == "drag_to" && !ValidatePoint(additional[0], out error)) return OperationResult.Failure(error!);
        }
        beforeDispatch();

        switch (action.Action)
        {
            case "activate":
                if (element.Patterns.Invoke.PatternOrDefault is { } invoke) invoke.Invoke();
                else if (element.Patterns.Toggle.PatternOrDefault is { } toggle) toggle.Toggle();
                else { if (!ValidatePoint(node, out string? error)) return OperationResult.Failure(error!); Mouse.Click(Center(node)); }
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
                if (!KeyboardChordParser.TryParse(action.Text, out FlaUI.Core.WindowsAPI.VirtualKeyShort[] keys, out string? parseError)) return OperationResult.Failure(parseError!);
                ActionInvoker.DispatchKeyboardChord(keys); break;
            case "right_click": Mouse.Click(Center(node), MouseButton.Right); break;
            case "double_click": Mouse.DoubleClick(Center(node)); break;
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
            if (string.Join(".", current.Properties.RuntimeId.ValueOrDefault ?? []) == expected.Identity)
            {
                SceneElement actual = Read(current, expected.Window, expected.Ref);
                return actual.Name == expected.Name && actual.Role == expected.Role && actual.Value == expected.Value &&
                    actual.Bounds == expected.Bounds && actual.Selected == expected.Selected &&
                    actual.ToggleState == expected.ToggleState && actual.StateHash == expected.StateHash && actual.Enabled ? current : null;
            }
            foreach (AutomationElement child in current.FindAllChildren()) queue.Enqueue(child);
        }
        return null;
    }

    private static Point Center(SceneElement node) => new(node.Bounds.Left + node.Bounds.Width / 2, node.Bounds.Top + node.Bounds.Height / 2);
    private static bool ValidatePoint(SceneElement node, out string? error) => WindowGeometry.TryValidatePointRecipient(node.Window, Center(node).X, Center(node).Y, false, out error);
    private static string? Clip(string? value) => value?.Length > MaximumText ? value[..MaximumText] : value;
}
