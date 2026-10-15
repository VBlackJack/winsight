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
public static class ManageWindowTool
{
    [McpServerTool(Name = "manage_window")]
    [Description("Move, resize, maximize, minimize or restore a top-level window of the target process: the " +
                 "main window by default, or the first window whose title contains windowTitle. Coordinates are " +
                 "screen pixels of the window's outer frame. Moving or resizing a maximized or minimized window " +
                 "restores it first. Returns an OperationResult JSON whose data carries the settled state " +
                 "(normal, maximized, minimized), the actual bounds, and matchesRequest=false when the " +
                 "application constrained the request (minimum size, snapping). Use it to check that a layout " +
                 "adapts to a new size, or to make a tab strip overflow.")]
    public static string ManageWindow(
        WindowManager manager,
        SessionTarget target,
        [Description("Action: move, resize, set_bounds, maximize, minimize, or restore.")]
        string action,
        [Description("Left edge in screen pixels, for move and set_bounds.")]
        int? x = null,
        [Description("Top edge in screen pixels, for move and set_bounds.")]
        int? y = null,
        [Description("Outer width in pixels, for resize and set_bounds.")]
        int? width = null,
        [Description("Outer height in pixels, for resize and set_bounds.")]
        int? height = null,
        [Description("Act on the first window whose title contains this text instead of the main window.")]
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
            if (!WindowActionParser.TryParse(action, out WindowAction parsed))
            {
                return Serialize(OperationResult.Failure(
                    $"Unknown action \"{action}\". Accepted values: {WindowActionParser.AcceptedValues}."));
            }
            if (WindowActionParser.ValidateArguments(parsed, x, y, width, height) is { } invalid)
            {
                return Serialize(OperationResult.Failure(invalid));
            }
            if (ToolAccessPolicy.DryRunMutation(
                    "manage_window",
                    new { processId = pid, action, x, y, width, height, windowTitle })
                is { } dryRun)
            {
                return Serialize(dryRun);
            }
            return Serialize(manager.Apply(pid, parsed, x, y, width, height, windowTitle));
        });
    }

    private static string Serialize(OperationResult result) =>
        JsonSerializer.Serialize(result, JsonOptions.Default);
}
