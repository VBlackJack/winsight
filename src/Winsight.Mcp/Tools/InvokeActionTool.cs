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

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class InvokeActionTool
{
    private const string ProcessIdDescription =
        "Target process id. Optional — falls back to the session default set by set_target.";

    [McpServerTool(Name = "click_element")]
    [Description("Locate a UI element and click it. Locator criteria are combined with AND across all " +
                 "top-level windows of the process; if several elements match, the call fails and lists " +
                 "the candidates so you can add 'index' or narrow the locator. Off-screen elements are " +
                 "scrolled into view first. Returns an OperationResult JSON with success/error.")]
    public static string ClickElement(
        ActionInvoker invoker,
        SessionTarget target,
        [Description("Exact AutomationId of the element.")]
        string? automationId = null,
        [Description("Exact Name property of the element.")]
        string? name = null,
        [Description("Exact ClassName of the element.")]
        string? className = null,
        [Description("UIA control type (e.g. Button, Edit, CheckBox), case-insensitive.")]
        string? controlType = null,
        [Description("Case-insensitive substring of the Name property.")]
        string? nameContains = null,
        [Description("Zero-based index to pick one element when several match.")]
        int? index = null,
        [Description("Case-insensitive substring exposed by a descendant's Name or Value, searched to bounded depth.")]
        string? descendantText = null,
        [Description("Restrict the search to windows whose title contains this text (modal dialogs, popups).")]
        string? windowTitle = null,
        [Description("Click style: single (default, prefers Invoke/Toggle patterns), double, or right.")]
        string? clickStyle = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            if (ToolAccessPolicy.DryRunMutation(
                    "click_element",
                    new { processId = pid, automationId, name, className, controlType, nameContains, index, windowTitle, clickStyle })
                is { } dryRun)
            {
                return JsonSerializer.Serialize(dryRun, JsonOptions.Default);
            }

            ClickStyle style = ClickStyle.Primary;
            if (!string.IsNullOrEmpty(clickStyle) && !ClickStyleParser.TryParse(clickStyle, out style))
            {
                return JsonSerializer.Serialize(
                    OperationResult.Failure(
                        $"Unknown clickStyle \"{clickStyle}\". Accepted values: {ClickStyleParser.AcceptedValues}."),
                    JsonOptions.Default);
            }

            ElementLocator locator = LocatorFactory.From(
                automationId, name, className, controlType, nameContains, index, descendantText);
            OperationResult result = invoker.Click(pid, locator, style, windowTitle);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "click_point")]
    [Description("Click at a coordinate, bypassing UI Automation element location. Use this to act on " +
                 "a point returned by find_element_visually, or any control UIA cannot address (custom " +
                 "Canvas, SkiaSharp, DirectX surfaces). By default x,y are window-relative (the same " +
                 "origin as a full-window screenshot), so the (x, y) from find_element_visually can be " +
                 "passed straight through; set coordinateSpace=screen for absolute desktop coordinates. " +
                 "The target window should be visible and unoccluded. Returns an OperationResult JSON " +
                 "whose data carries the absolute screenX/screenY that was clicked.")]
    public static string ClickPoint(
        ActionInvoker invoker,
        SessionTarget target,
        [Description("X coordinate of the click. Window-relative by default; see coordinateSpace.")]
        int x,
        [Description("Y coordinate of the click. Window-relative by default; see coordinateSpace.")]
        int y,
        [Description("Click style: single (default), double, or right.")]
        string? clickStyle = null,
        [Description("Restrict the target window to those whose title contains this text (modal dialogs, popups).")]
        string? windowTitle = null,
        [Description("Coordinate space: window (default, origin at the target window's top-left) or screen (absolute desktop).")]
        string? coordinateSpace = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            if (ToolAccessPolicy.DryRunMutation(
                    "click_point",
                    new { processId = pid, x, y, clickStyle, windowTitle, coordinateSpace })
                is { } dryRun)
            {
                return JsonSerializer.Serialize(dryRun, JsonOptions.Default);
            }

            ClickStyle style = ClickStyle.Primary;
            if (!string.IsNullOrEmpty(clickStyle) && !ClickStyleParser.TryParse(clickStyle, out style))
            {
                return JsonSerializer.Serialize(
                    OperationResult.Failure(
                        $"Unknown clickStyle \"{clickStyle}\". Accepted values: {ClickStyleParser.AcceptedValues}."),
                    JsonOptions.Default);
            }

            string space = (coordinateSpace ?? "window").Trim();
            bool windowRelative;
            if (string.Equals(space, "window", StringComparison.OrdinalIgnoreCase))
            {
                windowRelative = true;
            }
            else if (string.Equals(space, "screen", StringComparison.OrdinalIgnoreCase))
            {
                windowRelative = false;
            }
            else
            {
                return JsonSerializer.Serialize(
                    OperationResult.Failure(
                        $"Unknown coordinateSpace \"{coordinateSpace}\". Accepted values: window, screen."),
                    JsonOptions.Default);
            }

            OperationResult result = invoker.ClickPoint(pid, x, y, style, windowTitle, windowRelative);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "type_text")]
    [Description("Locate a UI element and set its value (via the Value pattern) or type into it " +
                 "(via keyboard fallback). Same strict locator semantics as click_element. " +
                 "Returns an OperationResult JSON with success/error.")]
    public static string TypeText(
        ActionInvoker invoker,
        SessionTarget target,
        [Description("Text to type into the element.")]
        string text,
        [Description("Exact AutomationId of the element.")]
        string? automationId = null,
        [Description("Exact Name property of the element.")]
        string? name = null,
        [Description("Exact ClassName of the element.")]
        string? className = null,
        [Description("UIA control type (e.g. Edit, ComboBox), case-insensitive.")]
        string? controlType = null,
        [Description("Case-insensitive substring of the Name property.")]
        string? nameContains = null,
        [Description("Zero-based index to pick one element when several match.")]
        int? index = null,
        [Description("Case-insensitive substring exposed by a descendant's Name or Value, searched to bounded depth.")]
        string? descendantText = null,
        [Description("Restrict the search to windows whose title contains this text (modal dialogs, popups).")]
        string? windowTitle = null,
        [Description("Bring the matched top-level window to the foreground before writing text; fails if focus cannot be verified.")]
        bool focusWindow = false,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            if (ToolAccessPolicy.DryRunMutation(
                    "type_text",
                    new { processId = pid, automationId, name, className, controlType, nameContains, index, windowTitle, focusWindow, textLength = text.Length })
                is { } dryRun)
            {
                return JsonSerializer.Serialize(dryRun, JsonOptions.Default);
            }

            ElementLocator locator = LocatorFactory.From(
                automationId, name, className, controlType, nameContains, index, descendantText);
            OperationResult result = invoker.Type(pid, locator, text, windowTitle, focusWindow);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "send_key_chord")]
    [Description("Focus a target top-level window, verify it became the foreground window, then send a " +
                 "keyboard chord such as Ctrl+Shift+N via SendInput-backed FlaUI keyboard input. " +
                 "Returns an explicit failure if the focus check does not pass.")]
    public static string SendKeyChord(
        ActionInvoker invoker,
        SessionTarget target,
        [Description("Keyboard chord such as Ctrl+Shift+N, Alt+F4, or Ctrl+Enter.")]
        string chord,
        [Description("Restrict focus to windows whose title contains this text.")]
        string? windowTitle = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            if (ToolAccessPolicy.DryRunMutation(
                    "send_key_chord",
                    new { processId = pid, chord, windowTitle })
                is { } dryRun)
            {
                return JsonSerializer.Serialize(dryRun, JsonOptions.Default);
            }

            OperationResult result = invoker.SendKeyChord(pid, chord, windowTitle);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "select_item")]
    [Description("Select an item by its visible text inside a located container: ComboBox, ListBox, " +
                 "or Tab (other containers fall back to a descendant SelectionItem search). " +
                 "On failure the available item names are listed. Returns an OperationResult JSON.")]
    public static string SelectItem(
        ActionInvoker invoker,
        SessionTarget target,
        [Description("Visible text of the item to select.")]
        string itemName,
        [Description("Exact AutomationId of the container element.")]
        string? automationId = null,
        [Description("Exact Name property of the container element.")]
        string? name = null,
        [Description("Exact ClassName of the container element.")]
        string? className = null,
        [Description("UIA control type of the container (e.g. ComboBox, List, Tab), case-insensitive.")]
        string? controlType = null,
        [Description("Case-insensitive substring of the container's Name property.")]
        string? nameContains = null,
        [Description("Zero-based index to pick one container when several match.")]
        int? index = null,
        [Description("Case-insensitive substring exposed by a descendant's Name or Value, searched to bounded depth.")]
        string? descendantText = null,
        [Description("Restrict the search to windows whose title contains this text (modal dialogs, popups).")]
        string? windowTitle = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            if (ToolAccessPolicy.DryRunMutation(
                    "select_item",
                    new { processId = pid, automationId, name, className, controlType, nameContains, index, windowTitle, itemName })
                is { } dryRun)
            {
                return JsonSerializer.Serialize(dryRun, JsonOptions.Default);
            }

            ElementLocator locator = LocatorFactory.From(
                automationId, name, className, controlType, nameContains, index, descendantText);
            OperationResult result = invoker.SelectItem(pid, locator, itemName, windowTitle);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "hover_element")]
    [Description("Locate a UI element, move the mouse to its center, and capture a newly appearing " +
                 "nearby tooltip. Stale tooltips visible before the hover are ignored. If " +
                 "expectedTooltipText is provided, the call fails unless a matching tooltip appears.")]
    public static string HoverElement(
        ActionInvoker invoker,
        SessionTarget target,
        [Description("Exact AutomationId of the element.")]
        string? automationId = null,
        [Description("Exact Name property of the element.")]
        string? name = null,
        [Description("Exact ClassName of the element.")]
        string? className = null,
        [Description("UIA control type (e.g. Button, Edit, CheckBox), case-insensitive.")]
        string? controlType = null,
        [Description("Case-insensitive substring of the Name property.")]
        string? nameContains = null,
        [Description("Zero-based index to pick one element when several match.")]
        int? index = null,
        [Description("Case-insensitive substring exposed by a descendant's Name or Value, searched to bounded depth.")]
        string? descendantText = null,
        [Description("Restrict the search to windows whose title contains this text (modal dialogs, popups).")]
        string? windowTitle = null,
        [Description("Optional tooltip text assertion, matched case-insensitively as a substring.")]
        string? expectedTooltipText = null,
        [Description("Tooltip wait timeout in milliseconds. Defaults to 1500; max 10000.")]
        int? timeoutMs = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            if (ToolAccessPolicy.DryRunMutation(
                    "hover_element",
                    new { processId = pid, automationId, name, className, controlType, nameContains, index, windowTitle, expectedTooltipText, timeoutMs })
                is { } dryRun)
            {
                return JsonSerializer.Serialize(dryRun, JsonOptions.Default);
            }

            ElementLocator locator = LocatorFactory.From(
                automationId, name, className, controlType, nameContains, index, descendantText);
            OperationResult result = invoker.Hover(
                pid,
                locator,
                expectedTooltipText,
                windowTitle,
                timeoutMs);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }
}
