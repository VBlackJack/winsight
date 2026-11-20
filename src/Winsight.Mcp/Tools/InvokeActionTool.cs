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
        "Target process id. Optional; falls back to the session default set by set_target.";

    private const string AllowWebView2InputDescription =
        "Send synthetic keyboard input even when it would land inside a WebView2 surface. Default false: " +
        "such input has crashed ctfmon, so prefer type_into_webview for web content.";

    // Kept in step with PointerDispatch.DefaultSettleMs and MaxSettleMs by InvokeActionToolDescriptionTests.
    internal const string SettleMsDescription =
        "Milliseconds the pointer rests on the point before the press, so the target sees the move first " +
        "(0 to 2000, default 150).";

    [McpServerTool(Name = "click_element", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
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
            ClickStyle style = ClickStyle.Primary;
            if (!string.IsNullOrEmpty(clickStyle) && !ClickStyleParser.TryParse(clickStyle, out style))
            {
                return JsonSerializer.Serialize(
                    OperationResult.Failure(
                        $"Unknown clickStyle \"{clickStyle}\". Accepted values: {ClickStyleParser.AcceptedValues}."),
                    JsonOptions.Default);
            }
            if (ToolAccessPolicy.GuardMutation(
                    "click_element",
                    new { processId = pid, automationId, name, className, controlType, nameContains, index, windowTitle, clickStyle })
                is { } gated)
            {
                return JsonSerializer.Serialize(gated, JsonOptions.Default);
            }

            ElementLocator locator = LocatorFactory.From(
                automationId, name, className, controlType, nameContains, index, descendantText);
            OperationResult result = invoker.Click(pid, locator, style, windowTitle);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "click_point", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Click at a coordinate, bypassing UI Automation element location. Use this to act on " +
                 "a point returned by find_element_visually, or any control UIA cannot address (custom " +
                 "Canvas, SkiaSharp, DirectX surfaces). By default x,y are window-relative (the same " +
                 "origin as a full-window screenshot), so the (x, y) from find_element_visually can be " +
                 "passed straight through; set coordinateSpace=screen for absolute desktop coordinates. " +
                 "Screen coordinates are still required to fall inside a visible target process window " +
                 "unless allowOutOfWindow=true. The target window should be visible and unoccluded; pass " +
                 "focusWindow=true to raise it first. The pointer moves, settles for settleMs, and presses only " +
                 "if it is still there (error cursor_moved otherwise), so immediate-mode UIs (iced, winit, egui) " +
                 "see the move before the press. Returns " +
                 "an OperationResult JSON whose data carries the absolute screenX/screenY that was clicked.")]
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
        [Description("When coordinateSpace=screen, allow the point to be outside the target window bounds. Default false.")]
        bool allowOutOfWindow = false,
        [Description("Bring the target window to the foreground and verify it before validating the point; " +
                     "fails if focus cannot be verified. Default false.")]
        bool focusWindow = false,
        [Description(SettleMsDescription)]
        int? settleMs = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            if (PointerDispatch.ValidateSettle(settleMs) is { } settleError)
            {
                return JsonSerializer.Serialize(OperationResult.Failure(settleError), JsonOptions.Default);
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

            if (ToolAccessPolicy.GuardMutation(
                    "click_point",
                    new
                    {
                        processId = pid, x, y, clickStyle, windowTitle, coordinateSpace, allowOutOfWindow,
                        focusWindow, settleMs,
                    })
                is { } gated)
            {
                return JsonSerializer.Serialize(gated, JsonOptions.Default);
            }

            OperationResult result = invoker.ClickPoint(
                pid, x, y, style, windowTitle, windowRelative, allowOutOfWindow,
                focusWindow: focusWindow, settleMs: settleMs);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "scroll_point", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Turn the mouse wheel at a coordinate, for lists and canvases that UI Automation cannot " +
                 "scroll (iced, winit, egui). Same coordinates and checks as click_point: window-relative by " +
                 "default, the point must reach a visible target window, focusWindow raises it first, and the " +
                 "pointer settles on the point (settleMs) before the wheel turns, because Windows sends the " +
                 "wheel to the window under the pointer. delta is in wheel notches: positive scrolls up, or " +
                 "right with horizontal=true. Returns the absolute screenX/screenY.")]
    public static string ScrollPoint(
        ActionInvoker invoker,
        SessionTarget target,
        [Description("X coordinate. Window-relative by default; see coordinateSpace.")]
        int x,
        [Description("Y coordinate. Window-relative by default; see coordinateSpace.")]
        int y,
        [Description("Wheel notches, -50 to 50 and not 0. Positive scrolls up (or right when horizontal).")]
        int delta,
        [Description("Turn the horizontal wheel instead. Default false.")]
        bool horizontal = false,
        [Description("Restrict the target window to those whose title contains this text.")]
        string? windowTitle = null,
        [Description("Coordinate space: window (default) or screen.")]
        string? coordinateSpace = null,
        [Description("When coordinateSpace=screen, allow the point to be outside the target window bounds. Default false.")]
        bool allowOutOfWindow = false,
        [Description("Bring the target window to the foreground and verify it first. Default false.")]
        bool focusWindow = false,
        [Description(SettleMsDescription)]
        int? settleMs = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!TryPointTarget(target, processId, coordinateSpace, out int pid, out bool windowRelative, out string? error))
            {
                return Serialize(OperationResult.Failure(error!));
            }
            if (ToolAccessPolicy.GuardMutation(
                    "scroll_point",
                    new { processId = pid, x, y, delta, horizontal, windowTitle, coordinateSpace, focusWindow, settleMs })
                is { } gated)
            {
                return Serialize(gated);
            }
            return Serialize(invoker.ScrollPoint(
                pid, x, y, delta, horizontal, windowTitle, windowRelative, allowOutOfWindow, focusWindow, settleMs));
        });
    }

    [McpServerTool(Name = "hover_point", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Rest the pointer on a coordinate without pressing, to reveal hover styles, tooltips or " +
                 "menus that a drawn UI only shows under the pointer. Same coordinates and checks as " +
                 "click_point; the pointer settles (settleMs), then stays for holdMs and is checked again " +
                 "(cursor_moved if something moved it). Capture or read_text_visually afterwards to see the " +
                 "effect. Use hover_element when UI Automation can locate the element. Returns the absolute " +
                 "screenX/screenY.")]
    public static string HoverPoint(
        ActionInvoker invoker,
        SessionTarget target,
        [Description("X coordinate. Window-relative by default; see coordinateSpace.")]
        int x,
        [Description("Y coordinate. Window-relative by default; see coordinateSpace.")]
        int y,
        [Description("Milliseconds to keep the pointer still after the settle (0 to 10000). Default 0.")]
        int? holdMs = null,
        [Description("Restrict the target window to those whose title contains this text.")]
        string? windowTitle = null,
        [Description("Coordinate space: window (default) or screen.")]
        string? coordinateSpace = null,
        [Description("When coordinateSpace=screen, allow the point to be outside the target window bounds. Default false.")]
        bool allowOutOfWindow = false,
        [Description("Bring the target window to the foreground and verify it first. Default false.")]
        bool focusWindow = false,
        [Description(SettleMsDescription)]
        int? settleMs = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!TryPointTarget(target, processId, coordinateSpace, out int pid, out bool windowRelative, out string? error))
            {
                return Serialize(OperationResult.Failure(error!));
            }
            if (ToolAccessPolicy.GuardMutation(
                    "hover_point",
                    new { processId = pid, x, y, holdMs, windowTitle, coordinateSpace, focusWindow, settleMs })
                is { } gated)
            {
                return Serialize(gated);
            }
            return Serialize(invoker.HoverPoint(
                pid, x, y, holdMs, windowTitle, windowRelative, allowOutOfWindow, focusWindow, settleMs));
        });
    }

    [McpServerTool(Name = "diagnose_pointer", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Pointer movement diagnostic for a point inside an authorized target window. " +
                 "Tries four pointer move methods in order (SendInput ABSOLUTE|VIRTUALDESK, SendInput ABSOLUTE " +
                 "primary monitor, SendInput relative, SetCursorPos), measures GetCursorPos and " +
                 "GetPhysicalCursorPos immediately and after 50 ms and 150 ms, and restores the original " +
                 "cursor position after each attempt without clicking. Honors WINSIGHT_DRY_RUN (reports what " +
                 "would be tried without moving the cursor) and the yield-to-user guard. Also reports " +
                 "ClipCursor, GetCursorInfo flags (showing/suppressed), mouse buttons down, " +
                 "SPI_GETMOUSE / SPI_GETMOUSESPEED, foreground process, and input context.")]
    public static string DiagnosePointer(
        ActionInvoker invoker,
        SessionTarget target,
        [Description("X coordinate inside the target window. Window-relative by default; see coordinateSpace.")]
        int x,
        [Description("Y coordinate inside the target window. Window-relative by default; see coordinateSpace.")]
        int y,
        [Description("Restrict the target window to those whose title contains this text.")]
        string? windowTitle = null,
        [Description("Coordinate space: window (default) or screen.")]
        string? coordinateSpace = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!TryPointTarget(target, processId, coordinateSpace, out int pid, out bool windowRelative, out string? error))
            {
                return Serialize(OperationResult.Failure(error!));
            }
            if (ToolAccessPolicy.GuardMutation(
                    "diagnose_pointer",
                    new
                    {
                        processId = pid,
                        x,
                        y,
                        windowTitle,
                        coordinateSpace = windowRelative ? "window" : "screen",
                        methods = PointerMoveMethods.AllOrdered,
                    })
                is { } gated)
            {
                return Serialize(gated);
            }
            return Serialize(invoker.DiagnosePointer(pid, x, y, windowTitle, windowRelative));
        });
    }

    private static bool TryPointTarget(
        SessionTarget target,
        int? processId,
        string? coordinateSpace,
        out int pid,
        out bool windowRelative,
        out string? error)
    {
        windowRelative = true;
        if (!target.TryResolve(processId, out pid, out string targetError))
        {
            error = targetError;
            return false;
        }
        string space = (coordinateSpace ?? "window").Trim();
        if (string.Equals(space, "screen", StringComparison.OrdinalIgnoreCase))
        {
            windowRelative = false;
        }
        else if (!string.Equals(space, "window", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Unknown coordinateSpace \"{coordinateSpace}\". Accepted values: window, screen.";
            return false;
        }
        error = null;
        return true;
    }

    private static string Serialize(OperationResult result) => JsonSerializer.Serialize(result, JsonOptions.Default);

    [McpServerTool(Name = "type_text", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Locate a UI element and set its value (via the Value pattern) or type into it " +
                 "(via keyboard fallback). Same strict locator semantics as click_element. " +
                 "An element inside a WebView2 surface is refused (synthetic keyboard input there has " +
                 "crashed ctfmon); use type_into_webview for web content. " +
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
        [Description(AllowWebView2InputDescription)]
        bool allowWebView2Input = false,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            if (text is null)
            {
                return JsonSerializer.Serialize(OperationResult.Failure("text is required."), JsonOptions.Default);
            }
            if (ToolAccessPolicy.GuardMutation(
                    "type_text",
                    new { processId = pid, automationId, name, className, controlType, nameContains, index, windowTitle, focusWindow, allowWebView2Input, textLength = text.Length })
                is { } gated)
            {
                return JsonSerializer.Serialize(gated, JsonOptions.Default);
            }

            ElementLocator locator = LocatorFactory.From(
                automationId, name, className, controlType, nameContains, index, descendantText);
            OperationResult result = invoker.Type(pid, locator, text, windowTitle, focusWindow, allowWebView2Input);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "send_key_chord", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Focus a target top-level window, verify it became the foreground window, then send a " +
                 "keyboard chord such as Ctrl+Shift+N through SendInput, with hardware scan codes and the " +
                 "extended-key flag (PageUp, arrows, Home/End) so low-level hooks, WM_KEYDOWN and WPF all see " +
                 "the complete chord. Returns an explicit failure if the focus check does not pass, and refuses " +
                 "to send when keyboard focus is inside a WebView2 surface. On success, data.focus describes " +
                 "the element that received the chord.")]
    public static string SendKeyChord(
        ActionInvoker invoker,
        SessionTarget target,
        [Description("Keyboard chord such as Ctrl+Shift+N, Alt+F4, or Ctrl+Enter.")]
        string chord,
        [Description("Restrict focus to windows whose title contains this text.")]
        string? windowTitle = null,
        [Description(AllowWebView2InputDescription)]
        bool allowWebView2Input = false,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            if (!KeyboardChordParser.TryParse(chord, out _, out string? parseError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(parseError!), JsonOptions.Default);
            }
            if (ToolAccessPolicy.GuardMutation(
                    "send_key_chord",
                    new { processId = pid, chord, windowTitle, allowWebView2Input })
                is { } gated)
            {
                return JsonSerializer.Serialize(gated, JsonOptions.Default);
            }

            OperationResult result = invoker.SendKeyChord(pid, chord, windowTitle, allowWebView2Input);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "select_item", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Select an item inside a located container: ComboBox, ListBox, or Tab (other " +
                 "containers, such as a TreeView, fall back to a descendant SelectionItem search). Name the " +
                 "item with exactly one of itemName (exact), itemNameContains (case-insensitive substring) or " +
                 "itemNamePattern (.NET regular expression); the last two survive names that carry live data " +
                 "such as a clock, and must match a single item. On failure the available or matching item " +
                 "names are listed. Returns an OperationResult JSON whose data.selectedName is the full name " +
                 "of the selected item.")]
    public static string SelectItem(
        ActionInvoker invoker,
        SessionTarget target,
        [Description("Exact visible text of the item to select.")]
        string? itemName = null,
        [Description("Case-insensitive substring of the item name, for names that change (e.g. 'lab-c,').")]
        string? itemNameContains = null,
        [Description("Regular expression the item name must match (e.g. '^lab-c, protocol SFTP').")]
        string? itemNamePattern = null,
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
            if (!ItemNameMatcher.TryCreate(
                    itemName, itemNameContains, itemNamePattern, out ItemNameMatcher? matcher, out string? matcherError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(matcherError!), JsonOptions.Default);
            }
            if (ToolAccessPolicy.GuardMutation(
                    "select_item",
                    new { processId = pid, automationId, name, className, controlType, nameContains, index, windowTitle, itemName, itemNameContains, itemNamePattern })
                is { } gated)
            {
                return JsonSerializer.Serialize(gated, JsonOptions.Default);
            }

            ElementLocator locator = LocatorFactory.From(
                automationId, name, className, controlType, nameContains, index, descendantText);
            OperationResult result = invoker.SelectItem(pid, locator, matcher!, windowTitle);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "hover_element", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Locate a UI element, move the mouse to its center, and capture a newly appearing " +
                 "nearby tooltip. Stale tooltips visible before the hover are ignored. The tooltip popup window " +
                 "is awaited for the whole timeout; data.tooltip.source is \"popup\" when its text comes from that " +
                 "window, or \"helpText\" when no popup opened and the element's AutomationProperties.HelpText was " +
                 "used instead. If expectedTooltipText is provided, the call fails unless a matching tooltip appears.")]
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
            if (ToolAccessPolicy.GuardMutation(
                    "hover_element",
                    new { processId = pid, automationId, name, className, controlType, nameContains, index, windowTitle, expectedTooltipText, timeoutMs })
                is { } gated)
            {
                return JsonSerializer.Serialize(gated, JsonOptions.Default);
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
