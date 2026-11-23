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

[McpServerToolType]
public static partial class AgentExperienceTool
{
    private const string ObservePrefix = "observe-";
    private const int DefaultStableForMs = 350;
    private static readonly char[] TermSeparators = { ' ', '-', '_', '.', ':' };
    private static readonly string[] DefaultLoop =
    {
        "set_target after launch_app or list_windows (pick by processName/className; includeTitles=true only to find an app by title)",
        "observe_scene to get expiring references, windows, states and supported actions",
        "act_verified with an explicit postcondition and dryRun=true to preview",
        "act_verified with dryRun=false, then inspect verified and dispatched separately",
        "observe again after every dispatch; never blindly repeat dispatched_unconfirmed",
        "observe_dom_scene for authorized WebView2 or locate_scene_target for configured vision",
        "save_app_profile and run_app_profile for version-pinned reusable workflows",
    };

    [McpServerTool(Name = "observe", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("One-stop perception tool for agents. It returns the target windows, actionable " +
                 "elements with ready-to-use locators, likely next actions, recent session memory, " +
                 "and optionally a screenshot path. Use this before acting on an unfamiliar UI instead " +
                 "of manually combining list_windows + describe_ui + capture_screenshot.")]
    public static string Observe(
        WindowDiscovery discovery,
        AffordanceScanner scanner,
        WindowCapture capture,
        SessionTarget target,
        SessionMemory memory,
        [Description("Scope the observation to the first window whose title contains this text.")]
        string? windowTitle = null,
        [Description("Include a screenshot path in the response. The image is written to WINSIGHT_OUTPUT_DIR.")]
        bool includeScreenshot = false,
        [Description("Capture source when includeScreenshot=true: 'window' (default, PrintWindow on one HWND) or 'screen' " +
                     "(DWM-composited desktop rectangle of the target window bounds, showing popups/menus/tooltips).")]
        string captureSource = CaptureSourceParser.WindowName,
        [Description("When includeScreenshot=true and captureSource='screen', allow capturing even when another process window " +
                     "occludes the target window (default false). Minimized or invisible targets are still refused.")]
        bool allowOccluded = false,
        [Description("Maximum actionable elements to return. Default 100.")]
        int maxResults = 100,
        [Description("Target process id. Optional; falls back to set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!CaptureSourceParser.TryParse(captureSource, out CaptureSource parsedSource, out string? sourceError))
            {
                return JsonSerializer.Serialize(
                    AgentFailure.Repairable(
                        "invalid_argument",
                        sourceError!,
                        cause: "argument_validation",
                        suggestedTool: "observe",
                        suggestedArgs: new { captureSource = CaptureSourceParser.WindowName }),
                    JsonOptions.Default);
            }
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                memory.RecordFailure(nameof(Observe), "missing_target", targetError, "set_target");
                return JsonSerializer.Serialize(
                    AgentFailure.Repairable(
                        "missing_target",
                        targetError,
                        cause: "target_resolution",
                        suggestedTool: "set_target",
                        suggestedArgs: new { processId = "<target process id>" }),
                    JsonOptions.Default);
            }
            if (maxResults <= 0 || maxResults > UiaDefaults.MaxDescribeResults)
            {
                string error = $"maxResults must be between 1 and {UiaDefaults.MaxDescribeResults}.";
                return JsonSerializer.Serialize(
                    AgentFailure.Repairable(
                        "invalid_argument",
                        error,
                        cause: "argument_validation",
                        suggestedTool: "observe",
                        suggestedArgs: new { maxResults = 100 }),
                    JsonOptions.Default);
            }

            IReadOnlyList<WindowDescriptor> windows = discovery.List(pid);
            IReadOnlyList<UiInteractable> elements = scanner.Describe(pid, windowTitle, maxResults);
            memory.RecordObservation(pid, windowTitle, elements);

            ScreenshotResult? screenshot = null;
            if (includeScreenshot)
            {
                screenshot = capture.Capture(
                    pid,
                    ScreenshotOutput.BuildTimestampedPath(ObservePrefix),
                    windowTitle,
                    mainWindow: false,
                    captureSource: parsedSource,
                    allowOccluded: allowOccluded);
            }

            var response = new
            {
                processId = pid,
                observedAtUtc = DateTime.UtcNow,
                windowTitle,
                windows,
                elements,
                nextActions = SuggestActions(elements, pid, windowTitle),
                screenshot,
                memory = memory.Snapshot(target.ProcessId),
            };
            return JsonSerializer.Serialize(OperationResult.OkWith(response), JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "plan_steps", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Build a conservative run_steps draft from a natural-language goal and the current UI. " +
                 "This is a helper, not an LLM: it ranks visible controls and returns a proposed BatchStep " +
                 "plan that should be dry-run before execution.")]
    public static string PlanSteps(
        AffordanceScanner scanner,
        SessionTarget target,
        SessionMemory memory,
        [Description("Natural-language goal, e.g. 'click increment' or 'type hello in note'.")]
        string goal,
        [Description("Text to use when the plan chooses a type step.")]
        string? text = null,
        [Description("Expected value for an optional assertion step.")]
        string? expectedValue = null,
        [Description("Scope the plan to a window title substring.")]
        string? windowTitle = null,
        [Description("Target process id. Optional; falls back to set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                memory.RecordFailure(nameof(PlanSteps), "missing_target", targetError, "set_target");
                return JsonSerializer.Serialize(
                    AgentFailure.Repairable(
                        "missing_target",
                        targetError,
                        cause: "target_resolution",
                        suggestedTool: "set_target",
                        suggestedArgs: new { processId = "<target process id>" }),
                    JsonOptions.Default);
            }

            IReadOnlyList<UiInteractable> elements = scanner.Describe(pid, windowTitle, 100);
            memory.RecordObservation(pid, windowTitle, elements);
            List<RankedInteractable> ranked = RankElements(goal, elements, maxResults: 5);
            RankedInteractable? first = ranked.Count > 0 ? ranked[0] : null;
            UiInteractable? best = first?.Element;
            PlannedAgentAction plannedAction = AgentStepPlanner.ChooseAction(goal, text, best);
            List<BatchStep> steps = new();
            if (best is not null)
            {
                steps.Add(BuildStep(plannedAction.Action, best, text, pid, windowTitle));
                if (!string.IsNullOrEmpty(expectedValue))
                {
                    steps.Add(BuildAssertionStep(best, expectedValue, pid, windowTitle));
                }
            }

            var response = new
            {
                goal,
                confidence = first?.Score ?? 0,
                requiresReview = steps.Count == 0 || (first?.Score ?? 0) < 30 || plannedAction.RequiresReview,
                reviewReason = plannedAction.ReviewReason,
                suggestedTool = "run_steps",
                suggestedRun = new { dryRun = true, processId = pid, steps },
                candidates = ranked,
                memoryMatches = memory.Find(goal, pid, windowTitle, maxResults: 5),
            };
            return JsonSerializer.Serialize(OperationResult.OkWith(response), JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "repair_locator", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Find likely replacement locators after a locator failed or became ambiguous. Searches " +
                 "the current UI plus session memory and returns ranked candidates with structured " +
                 "locator args usable in click_element/type_text/run_steps.")]
    public static string RepairLocator(
        AffordanceScanner scanner,
        SessionTarget target,
        SessionMemory memory,
        [Description("Human clue, failed name, AutomationId, label text, or previous locator string.")]
        string query,
        [Description("Scope repair to a window title substring.")]
        string? windowTitle = null,
        [Description("Target process id. Optional; falls back to set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                memory.RecordFailure(nameof(RepairLocator), "missing_target", targetError, "set_target");
                return JsonSerializer.Serialize(
                    AgentFailure.Repairable(
                        "missing_target",
                        targetError,
                        cause: "target_resolution",
                        suggestedTool: "set_target",
                        suggestedArgs: new { processId = "<target process id>" }),
                    JsonOptions.Default);
            }

            IReadOnlyList<UiInteractable> elements = scanner.Describe(pid, windowTitle, 100);
            memory.RecordObservation(pid, windowTitle, elements);
            var response = new
            {
                query,
                currentUi = RankElements(query, elements, maxResults: 10)
                    .Select(match => new
                    {
                        match.Score,
                        match.Reason,
                        match.Element,
                        locatorArgs = LocatorArgs(match.Element),
                    }),
                memory = memory.Find(query, pid, windowTitle, maxResults: 10),
                suggestedTool = "run_steps",
                suggestedArgs = new
                {
                    dryRun = true,
                    note = "Use currentUi[0].locatorArgs when the top candidate matches the intended control.",
                },
                fallbackTool = "observe",
            };
            return JsonSerializer.Serialize(OperationResult.OkWith(response), JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "act_and_observe", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Run one BatchStep and return before/after structural feedback. Use this when an agent " +
                 "wants to click/type/select once and immediately see the UI diff without manually calling " +
                 "snapshot_ui + run_steps + diff_ui.")]
    public static string ActAndObserve(
        StepRunner runner,
        UiSnapshotter snapshotter,
        AffordanceScanner scanner,
        WindowCapture capture,
        SessionTarget target,
        SessionMemory memory,
        [Description("Single BatchStep to execute. Mutating actions are dry-run when dryRun=true or WINSIGHT_DRY_RUN is set.")]
        BatchStep step,
        [Description("Scope observation to a window title substring.")]
        string? windowTitle = null,
        [Description("Include an after-action screenshot path.")]
        bool includeScreenshot = false,
        [Description("Validate and journal without clicking/typing/selecting.")]
        bool dryRun = false,
        [Description("Target process id. Optional; falls back to set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (processId is int explicitPid && step.ProcessId is int stepPid && explicitPid != stepPid)
            {
                return JsonSerializer.Serialize(
                    OperationResult.Failure("processId and step.processId must identify the same target."),
                    JsonOptions.Default);
            }
            if (!target.TryResolve(processId ?? step.ProcessId, out int pid, out string targetError))
            {
                memory.RecordFailure(nameof(ActAndObserve), "missing_target", targetError, "set_target");
                return JsonSerializer.Serialize(
                    AgentFailure.Repairable(
                        "missing_target",
                        targetError,
                        cause: "target_resolution",
                        suggestedTool: "set_target",
                        suggestedArgs: new { processId = "<target process id>" }),
                    JsonOptions.Default);
            }

            bool effectiveDryRun = dryRun || ToolAccessPolicy.IsDryRunEnabled;
            UiSnapshot before = snapshotter.Capture(pid, windowTitle);
            BatchResult result = runner.Run(
                new[] { step with { ProcessId = pid } },
                pid,
                stopOnError: true,
                maxDurationMs: null,
                dryRun: effectiveDryRun)
                with
            {
                DryRun = effectiveDryRun,
            };
            UiSnapshot after = snapshotter.Capture(pid, windowTitle);
            UiChange change = UiDiffer.Diff(before, after);
            IReadOnlyList<UiInteractable> elements = scanner.Describe(pid, windowTitle, 100);
            memory.RecordObservation(pid, windowTitle, elements);

            ScreenshotResult? screenshot = includeScreenshot
                ? capture.Capture(pid, ScreenshotOutput.BuildTimestampedPath(ObservePrefix), windowTitle)
                : null;
            var data = new
            {
                processId = pid,
                dryRun = effectiveDryRun,
                action = result,
                change,
                elements,
                screenshot,
            };
            return JsonSerializer.Serialize(AgentMacroResult.FromBatch("act_and_observe", result, data), JsonOptions.Default);
        });
    }

}
