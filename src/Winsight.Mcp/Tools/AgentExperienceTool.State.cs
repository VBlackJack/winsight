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
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Orchestration;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

public static partial class AgentExperienceTool
{
    [McpServerTool(Name = "get_session_memory", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Return Winsight's session memory: recent observed locators and recent repairable failures.")]
    public static string GetSessionMemory(SessionTarget target, SessionMemory memory)
    {
        return ToolSafety.SafeInvoke(() =>
            JsonSerializer.Serialize(OperationResult.OkWith(memory.Snapshot(target.ProcessId)), JsonOptions.Default));
    }

    [McpServerTool(Name = "clear_session_memory", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Clear Winsight's session memory without clearing the session target.")]
    public static string ClearSessionMemory(SessionMemory memory)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            memory.Clear();
            return JsonSerializer.Serialize(OperationResult.Ok(), JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "get_agent_state", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Return compact agent-oriented state: target, guardrails, recent memory, last failure, " +
                 "and available recipes. Use this when the agent is unsure what context it has.")]
    public static string GetAgentState(SessionTarget target, SessionMemory memory)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            SessionMemorySnapshot snapshot = memory.Snapshot(target.ProcessId);
            SessionFailure? lastFailure = snapshot.Failures.Count > 0 ? snapshot.Failures[0] : null;
            var data = new
            {
                target = new
                {
                    processId = target.ProcessId,
                    hasTarget = target.ProcessId.HasValue,
                },
                guardrails = new
                {
                    dryRun = ToolAccessPolicy.IsDryRunEnabled,
                    allowedProcessIdsConfigured = IsConfigured(ToolAccessPolicy.AllowedProcessIdsVariable),
                    allowedProcessNamesConfigured = IsConfigured(ToolAccessPolicy.AllowedProcessNamesVariable),
                    allowedExecutablesConfigured = IsConfigured(ToolAccessPolicy.AllowedExecutablePathsVariable),
                    traceEnabled = TraceRecorder.IsEnabled,
                    traceDirectory = TraceRecorder.CurrentDirectory,
                },
                memory = snapshot,
                lastFailure,
                recommendedNextStep = ResolveRecommendedNextStep(snapshot, target.ProcessId),
                recipes = AgentRecipeCatalog.All.Select(recipe => new { recipe.Id, recipe.Goal, recipe.WhenToUse }),
            };
            return JsonSerializer.Serialize(OperationResult.OkWith(data), JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "get_winsight_guide", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Return a compact embedded guide for agents: preferred workflows, repair loops, and when to use " +
                 "high-level tools. Also reports the server build this session runs (version, buildTimeUtc), the " +
                 "exact registeredTools, and newerBuildAvailable: when it is true, or a tool named in docs is " +
                 "missing from registeredTools, the client session is stale and must be restarted.")]
    public static string GetWinsightGuide(McpServer? server = null)
    {
        return ToolSafety.SafeInvoke(() =>
            JsonSerializer.Serialize(
                OperationResult.OkWith(new
                {
                    server = ServerBuildInfo.Describe(
                        server?.ServerOptions.ToolCollection?.Select(tool => tool.ProtocolTool.Name) ?? []),
                    defaultLoop = DefaultLoop,
                    preferredTools = new
                    {
                        perceive = "observe_scene",
                        state = "get_agent_state",
                        cautiousAction = "act_verified",
                        batchAction = "run_steps",
                        locatorRepair = "repair_locator",
                        replay = "replay_steps",
                        sharedDesktop = "get_user_activity",
                        coordinateClick = "click_point focusWindow=true",
                        textWithoutElement = "type_keys",
                        visualSync = "wait_for_visual_change / wait_for_visual_stable",
                        readDrawnText = "read_text_visually",
                    },
                    errorPolicy = new
                    {
                        missing_target = "call set_target or pass processId",
                        locator_ambiguous = "use candidates[0].locatorArgs or add index",
                        locator_not_found = "call repair_locator with the failed label/id",
                        policy_denied = "change WINSIGHT_ALLOWED_* or choose another process",
                        user_active = "a person is using the desktop: wait_for_user_idle, then observe again",
                        foreground_changed = "the batch stopped untouched: wait_for_user_idle, observe, rerun the rest",
                        cursor_moved = "something moved the pointer during the settle: get_user_activity, then retry",
                        clipboard_disabled = "set WINSIGHT_ALLOW_CLIPBOARD=1 in the server environment, or use type_keys mode=keys",
                        ocr_unavailable = "pass language with one of the installed OCR languages listed in the error",
                        timed_out = "the condition never held: observe or capture, then adjust the wait or the action",
                    },
                    recipes = AgentRecipeCatalog.All,
                }),
                JsonOptions.Default));
    }


    private static bool IsConfigured(string variableName) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variableName));

    private static string ResolveRecommendedNextStep(SessionMemorySnapshot snapshot, int? processId)
    {
        if (processId is null)
        {
            return "call list_windows or launch_app, then set_target";
        }
        if (snapshot.Failures.Count > 0 && snapshot.Failures[0] is { SuggestedTool: not null } failure)
        {
            return $"recover with {failure.SuggestedTool} for {failure.Code}";
        }
        if (snapshot.LastObservationUtc is null)
        {
            return "call observe";
        }
        return "plan_steps, run_steps dryRun=true, then act_and_observe or run_steps dryRun=false";
    }

}
