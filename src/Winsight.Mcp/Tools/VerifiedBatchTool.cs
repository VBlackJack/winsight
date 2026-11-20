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
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Winsight.Core.Models;
using Winsight.Core.Modules.UserActivity;

namespace Winsight.Mcp.Tools;

public sealed record VerifiedBatchStep(
    ElementLocator Target,
    string Action,
    SceneExpectation Expect,
    string? Text = null,
    string? ItemName = null,
    string? WindowTitle = null,
    int TimeoutMs = 3000);

[McpServerToolType]
public static class VerifiedBatchTool
{
    private const int MaxBatchSteps = 20;

    [McpServerTool(Name = "run_verified_steps", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Run 1..20 semantic steps in a single round-trip with fresh observe_scene + act_verified at every step. " +
                 "Use this for multi-field forms or wizards instead of one act_verified call per field. " +
                 "Stops immediately at the first unverified, stale, or blocked step (never retries blindly) and returns a fresh recovery observation. " +
                 "On full success returns verified=true and a fresh nextObservation. Emits MCP progress notifications between steps when requested.")]
    public static Task<string> RunVerifiedSteps(
        SceneStore store,
        SessionTarget target,
        [Description("Ordered semantic steps (1..20), each with exact Target locator, Action, Expect postcondition, and optional Text/ItemName/WindowTitle/TimeoutMs.")]
        VerifiedBatchStep[] steps,
        [Description("Target process id. Optional; falls back to set_target.")]
        int? processId = null,
        [Description("Validate all steps without dispatching any input.")]
        bool dryRun = false,
        [Description("Only allow background-safe UIA patterns (no focus/mouse/keyboard stealing); bypasses user_active yield checks.")]
        bool backgroundOnly = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
        => ToolSafety.SafeInvokeAsync(async () =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return VerifiedSceneTool.Serialize(AgentFailure.Repairable(
                    "missing_target", targetError, "target_resolution", "set_target",
                    new { processId = "<target process id>" }));
            }
            if (steps is null || steps.Length is < 1 or > MaxBatchSteps)
            {
                return VerifiedSceneTool.Serialize(OperationResult.Failure($"Expected 1..{MaxBatchSteps} steps."));
            }

            for (int i = 0; i < steps.Length; i++)
            {
                string? invalid = ValidateStep(steps[i]);
                if (invalid is not null)
                {
                    return VerifiedSceneTool.Serialize(new
                    {
                        status = "blocked",
                        verified = false,
                        failedAtIndex = i,
                        message = $"Step {i}: {invalid}",
                    });
                }
            }

            if (dryRun || ToolAccessPolicy.IsDryRunEnabled)
            {
                return VerifiedSceneTool.Serialize(new
                {
                    status = "preview",
                    dryRun = true,
                    verified = false,
                    total = steps.Length,
                    steps,
                });
            }

            if (UserActivityPolicy.CheckSessionLocked() is { } sessionLocked)
            {
                return VerifiedSceneTool.Serialize(OperationResult.Failure(sessionLocked));
            }
            if (!backgroundOnly && UserActivityPolicy.CheckYield() is { } userActive)
            {
                return VerifiedSceneTool.Serialize(new
                {
                    status = "blocked",
                    verified = false,
                    message = userActive,
                    hint = AgentFailure.BuildHint("run_verified_steps", "blocked", userActive, store.Services.Memory),
                });
            }

            using Process process = Process.GetProcessById(pid);
            long processStart = process.StartTime.ToUniversalTime().Ticks;
            List<JsonElement> results = new();

            for (int i = 0; i < steps.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VerifiedBatchStep step = steps[i];
                progress?.Report(new ProgressNotificationValue
                {
                    Progress = i,
                    Total = steps.Length,
                    Message = $"Step {i + 1}/{steps.Length}: {step.Action}",
                });

                if (process.HasExited)
                {
                    return VerifiedSceneTool.Serialize(new
                    {
                        status = "blocked",
                        verified = false,
                        completed = results.Count,
                        failedAtIndex = i,
                        message = "Target process exited during verified batch.",
                        results,
                    });
                }

                SceneStore.Entry scene = VerifiedSceneTool.Capture(store, pid, step.WindowTitle);
                if (scene.ProcessStart != processStart)
                {
                    return VerifiedSceneTool.Serialize(new
                    {
                        status = "stale",
                        verified = false,
                        completed = results.Count,
                        failedAtIndex = i,
                        message = "Target process was replaced.",
                        results,
                    });
                }

                SceneElement[] matches = scene.Capture.Elements.Where(e => VerifiedSceneTool.Matches(e, step.Target)).ToArray();
                if (scene.Capture.Truncated || matches.Length != 1)
                {
                    string reason = scene.Capture.Truncated
                        ? "Scene truncated before resolving step target."
                        : matches.Length == 0
                            ? "Step target was not found in live scene."
                            : $"Step target matched {matches.Length} elements (ambiguous).";
                    return VerifiedSceneTool.Serialize(new
                    {
                        status = "blocked",
                        verified = false,
                        completed = results.Count,
                        failedAtIndex = i,
                        message = reason,
                        results,
                        recovery = scene,
                        hint = AgentFailure.BuildHint("run_verified_steps", "blocked", reason, store.Services.Memory),
                    });
                }

                SceneAction action = new(
                    scene.Id,
                    matches[0].Ref,
                    step.Action,
                    step.Expect,
                    Text: step.Text,
                    ItemName: step.ItemName,
                    TimeoutMs: step.TimeoutMs,
                    BackgroundOnly: backgroundOnly);
                string output = await VerifiedSceneTool.Execute(store, action, cancellationToken).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(output);
                results.Add(doc.RootElement.Clone());

                if (!doc.RootElement.GetProperty("verified").GetBoolean())
                {
                    object? recovery = !process.HasExited ? VerifiedSceneTool.Capture(store, pid, step.WindowTitle) : null;
                    return VerifiedSceneTool.Serialize(new
                    {
                        status = "stopped",
                        verified = false,
                        completed = results.Count - 1,
                        failedAtIndex = i,
                        results,
                        recovery,
                    });
                }
            }

            progress?.Report(new ProgressNotificationValue
            {
                Progress = steps.Length,
                Total = steps.Length,
                Message = $"Completed {steps.Length}/{steps.Length} verified steps.",
            });

            object? nextObservation = !process.HasExited
                ? VerifiedSceneTool.Capture(store, pid, steps[^1].WindowTitle)
                : null;
            return VerifiedSceneTool.Serialize(new
            {
                status = "verified",
                verified = true,
                completed = results.Count,
                total = steps.Length,
                results,
                nextObservation,
            });
        });

    internal static string? ValidateStep(VerifiedBatchStep step)
    {
        if (step is null || step.Target is null || step.Expect is null) return "Target and expect are required.";
        if (step.Target.Index is not null || step.Target.ClassName is not null || step.Target.NameContains is not null || step.Target.DescendantText is not null ||
            (string.IsNullOrEmpty(step.Target.AutomationId) && string.IsNullOrEmpty(step.Target.Name)))
        {
            return "Step target requires an exact AutomationId or Name (optional ControlType only).";
        }
        if (step.Action is not ("activate" or "set_value" or "select" or "select_item" or "expand" or "collapse" or "scroll_into_view" or "focus" or "right_click" or "double_click"))
        {
            return "Unsupported verified batch action.";
        }
        return VerifiedSceneTool.Validate(new SceneAction("preview", "preview", step.Action, step.Expect, Text: step.Text, ItemName: step.ItemName, TimeoutMs: step.TimeoutMs));
    }
}
