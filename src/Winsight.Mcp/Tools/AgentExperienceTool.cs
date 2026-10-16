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
public static class AgentExperienceTool
{
    private const string ObservePrefix = "observe-";
    private const int DefaultStableForMs = 350;
    private static readonly char[] TermSeparators = { ' ', '-', '_', '.', ':' };
    private static readonly string[] DefaultLoop =
    {
        "set_target after launch_app or list_windows",
        "observe_scene to get expiring references, windows, states and supported actions",
        "act_verified with an explicit postcondition and dryRun=true to preview",
        "act_verified with dryRun=false, then inspect verified and dispatched separately",
        "observe again after every dispatch; never blindly repeat dispatched_unconfirmed",
        "observe_dom_scene for authorized WebView2 or locate_scene_target for configured vision",
        "save_app_profile and run_app_profile for version-pinned reusable workflows",
    };

    [McpServerTool(Name = "observe")]
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
        [Description("Maximum actionable elements to return. Default 100.")]
        int maxResults = 100,
        [Description("Target process id. Optional; falls back to set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
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
                screenshot = capture.Capture(pid, ScreenshotOutput.BuildTimestampedPath(ObservePrefix), windowTitle);
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

    [McpServerTool(Name = "plan_steps")]
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

    [McpServerTool(Name = "repair_locator")]
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

    [McpServerTool(Name = "act_and_observe")]
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

    [McpServerTool(Name = "click_and_wait")]
    [Description("Macro: click a located element, then wait for a condition on either the same locator " +
                 "or a second locator. Returns click, wait, and UI diff. Use this instead of separate " +
                 "click_element + wait_for_element when synchronizing after a click.")]
    public static string ClickAndWait(
        ActionInvoker invoker,
        ElementWaiter waiter,
        UiSnapshotter snapshotter,
        SessionTarget target,
        [Description("Exact AutomationId of the click target.")]
        string? automationId = null,
        [Description("Exact Name of the click target.")]
        string? name = null,
        [Description("Click target control type.")]
        string? controlType = null,
        [Description("Substring of the click target Name.")]
        string? nameContains = null,
        [Description("Index for ambiguous click targets.")]
        int? index = null,
        [Description("Wait condition after the click. Defaults to stable_for on the click target.")]
        string condition = "stable_for",
        [Description("Expected value for value_equals/value_contains.")]
        string? expectedValue = null,
        [Description("AutomationId of the wait target. Omit to wait on the click target.")]
        string? waitAutomationId = null,
        [Description("Name of the wait target. Omit to wait on the click target.")]
        string? waitName = null,
        [Description("Control type of the wait target. Omit to wait on the click target.")]
        string? waitControlType = null,
        [Description("Timeout in milliseconds.")]
        int? timeoutMs = null,
        [Description("Required unchanged-state duration for stable_for. Defaults to 350 ms.")]
        int? stableForMs = null,
        [Description("Scope to a window title substring.")]
        string? windowTitle = null,
        [Description("Target process id. Optional; falls back to set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(
                    AgentFailure.Repairable(
                        "missing_target",
                        targetError,
                        cause: "target_resolution",
                        suggestedTool: "set_target",
                        suggestedArgs: new { processId = "<target process id>" }),
                    JsonOptions.Default);
            }
            ElementLocator clickLocator = LocatorFactory.From(
                automationId, name, null, controlType, nameContains, index, null);
            ElementLocator waitLocator = LocatorFactory.From(
                waitAutomationId ?? automationId,
                waitName ?? name,
                null,
                waitControlType ?? controlType,
                nameContains,
                index,
                null);
            if (!WaitConditionParser.TryParse(condition, out WaitCondition parsed))
            {
                return JsonSerializer.Serialize(
                    AgentFailure.Repairable(
                        "invalid_wait_condition",
                        $"Unknown condition \"{condition}\". Accepted values: {WaitConditionParser.AcceptedValues}.",
                        cause: "argument_validation",
                        suggestedTool: "click_and_wait",
                        suggestedArgs: new { condition = "exists" },
                        candidates: WaitConditionParser.AcceptedValues),
                    JsonOptions.Default);
            }
            if (ToolAccessPolicy.GuardMutation(
                    "click_and_wait",
                    new { processId = pid, clickLocator, waitLocator, condition, stableForMs })
                is { } gated)
            {
                return JsonSerializer.Serialize(gated, JsonOptions.Default);
            }

            UiSnapshot before = snapshotter.Capture(pid, windowTitle);
            OperationResult click = invoker.Click(pid, clickLocator, ClickStyle.Primary, windowTitle);
            int? effectiveStableForMs = parsed == WaitCondition.StableFor
                ? ResolveStableForMs(stableForMs, timeoutMs)
                : stableForMs;
            OperationResult wait = click.Success
                ? waiter.Wait(pid, waitLocator, parsed, timeoutMs, null, windowTitle, expectedValue, effectiveStableForMs)
                : OperationResult.FailureWith("Wait skipped because click failed.", new { skipped = true, reason = "click_failed" });
            UiSnapshot after = snapshotter.Capture(pid, windowTitle);
            var data = new { click, wait, change = UiDiffer.Diff(before, after) };
            return JsonSerializer.Serialize(AgentMacroResult.FromParts("click_and_wait", data, click, wait), JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "type_and_assert")]
    [Description("Macro: type into a located element, then assert an element state. Use this for form " +
                 "fill operations that need immediate machine-checkable feedback.")]
    public static string TypeAndAssert(
        ActionInvoker invoker,
        ElementStateReader reader,
        SessionTarget target,
        [Description("Text to type.")]
        string text,
        [Description("Assertion operator, e.g. equals, contains, enabled, checked.")]
        string @operator,
        [Description("Expected assertion value.")]
        string? expected = null,
        [Description("Exact AutomationId of the type target.")]
        string? automationId = null,
        [Description("Exact Name of the type target.")]
        string? name = null,
        [Description("Control type of the type target.")]
        string? controlType = null,
        [Description("Substring of the type target Name.")]
        string? nameContains = null,
        [Description("AutomationId of the assertion target. Omit to assert on the typed element.")]
        string? assertAutomationId = null,
        [Description("Name of the assertion target. Omit to assert on the typed element.")]
        string? assertName = null,
        [Description("Scope to a window title substring.")]
        string? windowTitle = null,
        [Description("Target process id. Optional; falls back to set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(
                    AgentFailure.Repairable(
                        "missing_target",
                        targetError,
                        cause: "target_resolution",
                        suggestedTool: "set_target",
                        suggestedArgs: new { processId = "<target process id>" }),
                    JsonOptions.Default);
            }
            ElementLocator typeLocator = LocatorFactory.From(
                automationId, name, null, controlType, nameContains, null, null);
            ElementLocator assertLocator = LocatorFactory.From(
                assertAutomationId ?? automationId,
                assertName ?? name,
                null,
                controlType,
                nameContains,
                null,
                null);
            if (text is null)
            {
                return JsonSerializer.Serialize(
                    AgentFailure.Repairable(
                        "invalid_argument",
                        "text is required.",
                        cause: "argument_validation",
                        suggestedTool: "type_and_assert",
                        suggestedArgs: new { text = "<text>" }),
                    JsonOptions.Default);
            }
            if (!AssertionEvaluator.TryParseOperator(@operator, out AssertionOperator parsed))
            {
                return JsonSerializer.Serialize(
                    AgentFailure.Repairable(
                        "invalid_assertion_operator",
                        $"Unknown operator \"{@operator}\". Accepted: {AssertionEvaluator.AcceptedOperators}.",
                        cause: "argument_validation",
                        suggestedTool: "type_and_assert",
                        suggestedArgs: new { @operator = "contains" },
                        candidates: AssertionEvaluator.AcceptedOperators),
                    JsonOptions.Default);
            }
            if (ToolAccessPolicy.GuardMutation(
                    "type_and_assert",
                    new { processId = pid, typeLocator, assertLocator, textLength = text.Length, @operator, expected })
                is { } gated)
            {
                return JsonSerializer.Serialize(gated, JsonOptions.Default);
            }

            OperationResult type = invoker.Type(pid, typeLocator, text, windowTitle, focusWindow: false);
            OperationResult assertion;
            if (type.Success)
            {
                OperationResult read = reader.Read(pid, assertLocator, windowTitle);
                assertion = read.Success
                    ? BuildAssertion(parsed, expected, (ElementState)read.Data!)
                    : read;
            }
            else
            {
                assertion = OperationResult.FailureWith(
                    "Assertion skipped because type failed.",
                    new { skipped = true, reason = "type_failed" });
            }

            var data = new { type, assertion };
            return JsonSerializer.Serialize(AgentMacroResult.FromParts("type_and_assert", data, type, assertion), JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "get_session_memory")]
    [Description("Return Winsight's session memory: recent observed locators and recent repairable failures.")]
    public static string GetSessionMemory(SessionTarget target, SessionMemory memory)
    {
        return ToolSafety.SafeInvoke(() =>
            JsonSerializer.Serialize(OperationResult.OkWith(memory.Snapshot(target.ProcessId)), JsonOptions.Default));
    }

    [McpServerTool(Name = "clear_session_memory")]
    [Description("Clear Winsight's session memory without clearing the session target.")]
    public static string ClearSessionMemory(SessionMemory memory)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            memory.Clear();
            return JsonSerializer.Serialize(OperationResult.Ok(), JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "get_agent_state")]
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

    [McpServerTool(Name = "get_winsight_guide")]
    [Description("Return a compact embedded guide for agents: preferred workflows, repair loops, and when to use high-level tools.")]
    public static string GetWinsightGuide()
    {
        return ToolSafety.SafeInvoke(() =>
            JsonSerializer.Serialize(
                OperationResult.OkWith(new
                {
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
                    },
                    recipes = AgentRecipeCatalog.All,
                }),
                JsonOptions.Default));
    }

    private static List<object> SuggestActions(
        IReadOnlyList<UiInteractable> elements,
        int processId,
        string? windowTitle)
    {
        return elements
            .Where(element => element.IsEnabled && element.IsVisible)
            .Take(20)
            .Select(element => new
            {
                element.AffordanceId,
                action = ActionFor(element),
                label = string.IsNullOrEmpty(element.Name) ? element.AutomationId : element.Name,
                element.ControlType,
                tool = SuggestedToolFor(element),
                requiredInput = RequiredInputFor(element),
                arguments = SuggestedArgsFor(element, processId, windowTitle),
                risk = RiskFor(element),
                expectedFeedback = ExpectedFeedbackFor(element),
                element.RecommendedLocator,
                locatorArgs = LocatorArgs(element),
            })
            .Cast<object>()
            .ToList();
    }

    private static List<RankedInteractable> RankElements(
        string query,
        IReadOnlyList<UiInteractable> elements,
        int maxResults)
    {
        string[] terms = query.Split(
            TermSeparators,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return elements
            .Select(element => Score(query, terms, element))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .Take(maxResults)
            .ToList();
    }

    private static RankedInteractable Score(string query, IReadOnlyList<string> terms, UiInteractable element)
    {
        var score = 0;
        List<string> reasons = new();
        if (string.Equals(element.Name, query, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
            reasons.Add("exactName");
        }
        if (string.Equals(element.AutomationId, query, StringComparison.OrdinalIgnoreCase))
        {
            score += 90;
            reasons.Add("exactAutomationId");
        }
        if (element.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
            reasons.Add("nameContains");
        }
        if (element.AutomationId?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
        {
            score += 50;
            reasons.Add("automationIdContains");
        }
        foreach (string term in terms)
        {
            if (element.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                element.RecommendedLocator.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                element.ControlType.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }
        }
        if (!element.IsEnabled || !element.IsVisible)
        {
            score -= 25;
            reasons.Add("notReady");
        }

        return new RankedInteractable(Math.Max(0, score), string.Join(", ", reasons.Distinct()), element);
    }

    private static BatchStep BuildStep(
        string action,
        UiInteractable element,
        string? text,
        int processId,
        string? windowTitle)
    {
        LocatorParts locator = LocatorArgs(element);
        return new BatchStep(
            action,
            Label: $"planned:{element.Name}",
            ProcessId: processId,
            AutomationId: locator.AutomationId,
            Name: locator.Name,
            ControlType: locator.ControlType,
            Index: locator.Index,
            WindowTitle: windowTitle ?? element.WindowTitle,
            Text: action == "type" ? text ?? string.Empty : null);
    }

    private static BatchStep BuildAssertionStep(
        UiInteractable element,
        string expectedValue,
        int processId,
        string? windowTitle)
    {
        LocatorParts locator = LocatorArgs(element);
        return new BatchStep(
            "assert",
            Label: $"assert:{element.Name}",
            ProcessId: processId,
            AutomationId: locator.AutomationId,
            Name: locator.Name,
            ControlType: locator.ControlType,
            Index: locator.Index,
            WindowTitle: windowTitle ?? element.WindowTitle,
            Operator: "contains",
            ExpectedValue: expectedValue);
    }

    private static string ActionFor(UiInteractable element)
    {
        return element.ControlType switch
        {
            "Edit" => "type",
            _ => "click",
        };
    }

    private static string SuggestedToolFor(UiInteractable element) =>
        element.ControlType == "Edit" ? "type_and_assert" : "act_and_observe";

    private static string[] RequiredInputFor(UiInteractable element) =>
        element.ControlType == "Edit" ? new[] { "text" } : Array.Empty<string>();

    private static object SuggestedArgsFor(UiInteractable element, int processId, string? windowTitle)
    {
        LocatorParts locator = LocatorArgs(element);
        if (element.ControlType == "Edit")
        {
            return new
            {
                processId,
                text = "<text>",
                @operator = "contains",
                expected = "<expected text>",
                automationId = locator.AutomationId,
                name = locator.Name,
                controlType = locator.ControlType,
                nameContains = (string?)null,
                windowTitle = windowTitle ?? element.WindowTitle,
            };
        }

        BatchStep step = BuildStep(ActionFor(element), element, null, processId, windowTitle);
        return new
        {
            processId,
            dryRun = true,
            step,
        };
    }

    private static string RiskFor(UiInteractable element)
    {
        if (!element.IsEnabled || !element.IsVisible)
        {
            return "not_ready";
        }
        return element.ControlType switch
        {
            "Button" or "SplitButton" or "MenuItem" or "Hyperlink" => "mutates_or_navigates",
            "Edit" => "writes_text",
            "ComboBox" or "List" or "ListItem" or "Tab" or "TabItem" => "changes_selection",
            _ => "low",
        };
    }

    private static string ExpectedFeedbackFor(UiInteractable element)
    {
        return element.ControlType switch
        {
            "Edit" => "type_and_assert should confirm value, or get_element_state should expose the new value.",
            "Button" or "SplitButton" or "MenuItem" or "Hyperlink" =>
                "act_and_observe should report changed UI, opened windows, or a follow-up assertion target.",
            "ComboBox" or "List" or "ListItem" or "Tab" or "TabItem" =>
                "observe or get_element_state should show selection/value changed.",
            _ => "act_and_observe should report whether the UI changed.",
        };
    }

    private static OperationResult BuildAssertion(
        AssertionOperator op,
        string? expected,
        ElementState state)
    {
        AssertionResult outcome = AssertionEvaluator.Evaluate(op, expected, state);
        return outcome.Passed
            ? OperationResult.OkWith(new { assertion = outcome.Detail })
            : OperationResult.Failure($"Assertion failed: {outcome.Detail}");
    }

    private static int ResolveStableForMs(int? stableForMs, int? timeoutMs)
    {
        if (stableForMs is int configured)
        {
            return configured;
        }

        int timeout = timeoutMs ?? UiaDefaults.ResolveWaitTimeoutMs();
        return Math.Min(DefaultStableForMs, timeout);
    }

    private static LocatorParts LocatorArgs(UiInteractable element)
    {
        if (!string.IsNullOrEmpty(element.AutomationId) &&
            element.RecommendedLocator.StartsWith("automationId=", StringComparison.Ordinal))
        {
            return new LocatorParts(element.AutomationId, null, null, null);
        }
        if (!string.IsNullOrEmpty(element.Name) &&
            element.RecommendedLocator.StartsWith("name=", StringComparison.Ordinal))
        {
            return new LocatorParts(null, element.Name, null, null);
        }

        return new LocatorParts(null, null, element.ControlType, ParseIndex(element.RecommendedLocator));
    }

    private static int? ParseIndex(string recommendedLocator)
    {
        const string marker = "index=";
        int index = recommendedLocator.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }
        string raw = recommendedLocator[(index + marker.Length)..].Trim();
        return int.TryParse(raw, System.Globalization.NumberStyles.Integer, null, out int parsed)
            ? parsed
            : null;
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

    private sealed record RankedInteractable(int Score, string Reason, UiInteractable Element);

    private sealed record LocatorParts(
        string? AutomationId,
        string? Name,
        string? ControlType,
        int? Index);
}
