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
public static class SessionTargetTool
{
    [McpServerTool(Name = "set_target", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Set the session's default target process so you do not repeat processId on every call. " +
                 "Once set, any process-scoped tool (click_element, type_text, wait_for_element, " +
                 "capture_screenshot, run_steps, ...) uses it when its own processId is omitted; an " +
                 "explicit processId on a call still overrides it. Returns the target plus how many " +
                 "top-level windows the process currently exposes (0 is allowed; it may still be " +
                 "starting). Call this right after launch_app or list_windows.")]
    public static string SetTarget(
        SessionTarget target,
        WindowDiscovery discovery,
        [Description("Process id to make the session default (from launch_app or list_windows).")]
        int processId,
        [Description("Optional short alias (1..32 ASCII letters/digits/_/-) to remember this process for multi-app switching via switch_target.")]
        string? alias = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TrySet(processId, out string error, alias))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(error), JsonOptions.Default);
            }

            int windowCount = discovery.List(processId).Count;
            IntegrityComparison integrity = ProcessIntegrity.CheckTarget(processId);
            return JsonSerializer.Serialize(
                OperationResult.OkWith(new
                {
                    processId,
                    alias,
                    windowCount,
                    aliases = target.Aliases,
                    integrity = FormatIntegrityData(integrity),
                    integrityWarning = integrity.Warning,
                }),
                JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "switch_target", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Switch the active session default target to a previously bound named alias (from set_target with alias). Useful for multi-app workflows without juggling raw PIDs.")]
    public static string SwitchTarget(
        SessionTarget target,
        WindowDiscovery discovery,
        [Description("Named alias bound earlier via set_target.")]
        string alias)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TrySwitch(alias, out int processId, out string error))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(error), JsonOptions.Default);
            }

            int windowCount = discovery.List(processId).Count;
            IntegrityComparison integrity = ProcessIntegrity.CheckTarget(processId);
            return JsonSerializer.Serialize(
                OperationResult.OkWith(new
                {
                    alias,
                    processId,
                    windowCount,
                    aliases = target.Aliases,
                    integrity = FormatIntegrityData(integrity),
                    integrityWarning = integrity.Warning,
                }),
                JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "get_target", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Return the session's current default target process (or null) and any bound named aliases.")]
    public static string GetTarget(SessionTarget target)
    {
        return ToolSafety.SafeInvoke(() =>
            JsonSerializer.Serialize(
                OperationResult.OkWith(new { processId = target.ProcessId, aliases = target.Aliases }),
                JsonOptions.Default));
    }

    [McpServerTool(Name = "clear_target", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Clear a specific named alias, or clear the session default target and all aliases when alias is omitted.")]
    public static string ClearTarget(
        SessionTarget target,
        [Description("Optional alias to remove without clearing the active target.")]
        string? alias = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            target.Clear(alias);
            return JsonSerializer.Serialize(OperationResult.Ok(), JsonOptions.Default);
        });
    }

    private static object FormatIntegrityData(IntegrityComparison integrity) =>
        new
        {
            current = integrity.Current.Name,
            currentRid = integrity.Current.Rid,
            target = integrity.Target.Name,
            targetRid = integrity.Target.Rid,
            uipiBlocked = integrity.IsUipiBlocked,
        };
}
