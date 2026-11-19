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

using Winsight.Core.Models;

namespace Winsight.Mcp.Tools;

internal sealed record RepairableFailureData(
    string Code,
    string Message,
    string? Cause,
    string? SuggestedTool,
    object? SuggestedArgs,
    object? Candidates,
    bool Retryable,
    LoopWarning? Loop = null);

internal static class AgentFailure
{
    public static OperationResult Repairable(
        string code,
        string message,
        string? cause = null,
        string? suggestedTool = null,
        object? suggestedArgs = null,
        object? candidates = null,
        bool retryable = true,
        LoopWarning? loop = null)
    {
        return OperationResult.FailureWith(
            message,
            new RepairableFailureData(
                code,
                message,
                cause,
                suggestedTool,
                suggestedArgs,
                candidates,
                retryable,
                loop));
    }

    public static object BuildHint(string toolName, string status, string message, SessionMemory? memory = null)
    {
        string code = ClassifyCode(status, message);
        (string suggestedTool, string guidance) = code switch
        {
            "uipi_blocked" => ("set_target", "Target process runs at a higher integrity level than Winsight. Windows UIPI silently drops synthetic input; restart Winsight at the same integrity level."),
            "move_not_applied" => ("observe_scene", "The synthetic pointer move did not move the cursor (not caused by user input). Inspect SendInput error, integrity levels, and virtual desktop coordinates in the failure data."),
            "user_active" => ("wait_for_user_idle", "A person is using the desktop. Wait for idle, then call observe_scene."),
            "foreground_changed" => ("wait_for_user_idle", "Foreground moved away from target. Wait for idle, then re-observe."),
            "stale" => ("observe_scene", "Observation or element state changed before dispatch. Use recovery observation or call observe_scene."),
            "dispatched_unconfirmed" => ("observe_scene", "Input was dispatched but postcondition was not confirmed. NEVER retry blindly; inspect state with observe_scene or capture_scene."),
            "missing_target" => ("set_target", "Bind a target process with set_target or launch_app."),
            "cdp_unauthorized" => ("detect_webview2_endpoints", "Discover an authorized WebView2 port for the target process first."),
            _ => ("observe_scene", "Inspect current windows and actionable elements before choosing the next action."),
        };

        LoopWarning? loop = memory?.RecordFailure(toolName, code, message, suggestedTool);
        return new
        {
            code,
            suggestedTool,
            guidance,
            loopWarning = loop,
        };
    }

    private static string ClassifyCode(string status, string message)
    {
        if (message.Contains("uipi_blocked", StringComparison.OrdinalIgnoreCase)) return "uipi_blocked";
        if (message.Contains("move_not_applied", StringComparison.OrdinalIgnoreCase)) return "move_not_applied";
        if (message.Contains("user_active", StringComparison.OrdinalIgnoreCase)) return "user_active";
        if (message.Contains("foreground_changed", StringComparison.OrdinalIgnoreCase)) return "foreground_changed";
        if (message.Contains("No processId", StringComparison.OrdinalIgnoreCase)) return "missing_target";
        if (message.Contains("not exposed by a WebView2", StringComparison.OrdinalIgnoreCase)) return "cdp_unauthorized";
        if (status is "stale" or "dispatched_unconfirmed" or "blocked") return status;
        return "action_failed";
    }
}
