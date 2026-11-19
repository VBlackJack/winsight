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
public static class DragTool
{
    private const string WindowSpace = "window";
    private const string ScreenSpace = "screen";

    [McpServerTool(Name = "drag", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Drag from a source to a target with the left mouse button, paced for OLE drag and drop " +
                 "(WPF DragDrop.DoDragDrop): press on the source, hold, leave the system drag rectangle, move " +
                 "in several steps, settle on the target, wait, release. Each end is an element (source*/target* " +
                 "locator fields, strict like click_element, aimed at its center plus targetOffsetX/Y) or a point " +
                 "(sourceX/sourceY, targetX/targetY; window-relative by default, see coordinateSpace). The target " +
                 "window is brought to the foreground first; both points " +
                 "must reach a window of the target process unless allowOutOfWindow=true. Use it to reorder " +
                 "TabItems, drop onto a TreeView, or move list rows. Returns an OperationResult JSON whose data " +
                 "carries the screen path (fromX, fromY, toX, toY, moves); verify the outcome with " +
                 "wait_for_element or get_element_state.")]
    public static string Drag(
        ActionInvoker invoker,
        SessionTarget target,
        [Description("Exact AutomationId of the source element.")]
        string? sourceAutomationId = null,
        [Description("Exact Name of the source element.")]
        string? sourceName = null,
        [Description("Case-insensitive substring of the source element's Name.")]
        string? sourceNameContains = null,
        [Description("UIA control type of the source element (e.g. TabItem, TreeItem, ListItem).")]
        string? sourceControlType = null,
        [Description("Zero-based index when several elements match the source locator.")]
        int? sourceIndex = null,
        [Description("Source point X, instead of a source element.")]
        int? sourceX = null,
        [Description("Source point Y, instead of a source element.")]
        int? sourceY = null,
        [Description("Exact AutomationId of the target element.")]
        string? targetAutomationId = null,
        [Description("Exact Name of the target element.")]
        string? targetName = null,
        [Description("Case-insensitive substring of the target element's Name.")]
        string? targetNameContains = null,
        [Description("UIA control type of the target element.")]
        string? targetControlType = null,
        [Description("Zero-based index when several elements match the target locator.")]
        int? targetIndex = null,
        [Description("Target point X, instead of a target element.")]
        int? targetX = null,
        [Description("Target point Y, instead of a target element.")]
        int? targetY = null,
        [Description("Horizontal offset in pixels from the target element's center (e.g. negative to drop on its left half).")]
        int targetOffsetX = 0,
        [Description("Vertical offset in pixels from the target element's center.")]
        int targetOffsetY = 0,
        [Description("Coordinate space of sourceX/Y and targetX/Y: window (default, origin at the target window's top-left) or screen.")]
        string? coordinateSpace = null,
        [Description("Milliseconds to hold the button before the first move. Default 150, max 5000.")]
        int holdMs = DragOptions.DefaultHoldMs,
        [Description("Intermediate moves between the start and the target. Default 12, range 1-100.")]
        int steps = DragOptions.DefaultSteps,
        [Description("Milliseconds between moves. Default 25, max 1000.")]
        int stepDelayMs = DragOptions.DefaultStepDelayMs,
        [Description("Milliseconds to hover on the target before releasing. Default 250, max 5000.")]
        int dropDelayMs = DragOptions.DefaultDropDelayMs,
        [Description("Allow the target point to lie outside the target process windows (drop into another application). Default false.")]
        bool allowOutOfWindow = false,
        [Description("Restrict element lookup and window-relative points to windows whose title contains this text.")]
        string? windowTitle = null,
        [Description("Target process id. Optional, falls back to the session default set by set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return Serialize(OperationResult.Failure(targetError));
            }
            string space = (coordinateSpace ?? WindowSpace).Trim();
            bool windowRelative = string.Equals(space, WindowSpace, StringComparison.OrdinalIgnoreCase);
            if (!windowRelative && !string.Equals(space, ScreenSpace, StringComparison.OrdinalIgnoreCase))
            {
                return Serialize(OperationResult.Failure(
                    $"Unknown coordinateSpace \"{coordinateSpace}\". Accepted values: {WindowSpace}, {ScreenSpace}."));
            }

            DragEndpoint source = BuildEndpoint(
                sourceAutomationId, sourceName, sourceNameContains, sourceControlType, sourceIndex,
                sourceX, sourceY, 0, 0);
            DragEndpoint destination = BuildEndpoint(
                targetAutomationId, targetName, targetNameContains, targetControlType, targetIndex,
                targetX, targetY, targetOffsetX, targetOffsetY);
            DragOptions pacing = new(holdMs, steps, stepDelayMs, dropDelayMs);
            string? invalid = source.Validate("source") ?? destination.Validate("target") ?? pacing.Validate();
            if (invalid is not null)
            {
                return Serialize(OperationResult.Failure(invalid));
            }
            if (ToolAccessPolicy.GuardMutation(
                    "drag",
                    new { processId = pid, source, target = destination, pacing, coordinateSpace = space, allowOutOfWindow, windowTitle })
                is { } gated)
            {
                return Serialize(gated);
            }

            return Serialize(invoker.Drag(pid, source, destination, pacing, windowTitle, windowRelative, allowOutOfWindow));
        });
    }

    private static DragEndpoint BuildEndpoint(
        string? automationId,
        string? name,
        string? nameContains,
        string? controlType,
        int? index,
        int? x,
        int? y,
        int offsetX,
        int offsetY)
    {
        ElementLocator locator = LocatorFactory.From(automationId, name, null, controlType, nameContains, index, null);
        return locator.IsEmpty
            ? new DragEndpoint(null, x, y)
            : new DragEndpoint(locator, x, y, offsetX, offsetY);
    }

    private static string Serialize(OperationResult result) =>
        JsonSerializer.Serialize(result, JsonOptions.Default);
}
