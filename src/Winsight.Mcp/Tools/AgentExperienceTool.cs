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
    private static readonly char[] TermSeparators = { ' ', '-', '_', '.', ':' };
    private static readonly string[] DefaultLoop =
    {
        "set_target after launch_app or list_windows",
        "observe to get windows, actionable elements, recommended locators, and memory",
        "plan_steps with the user goal",
        "run_steps with dryRun=true",
        "run_steps or act_and_observe with dryRun=false",
        "repair_locator if a locator fails or becomes ambiguous",
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
                    RepairableFailure("missing_target", targetError, new { tool = "set_target" }),
                    JsonOptions.Default);
            }
            if (maxResults <= 0 || maxResults > UiaDefaults.MaxDescribeResults)
            {
                string error = $"maxResults must be between 1 and {UiaDefaults.MaxDescribeResults}.";
                return JsonSerializer.Serialize(
                    RepairableFailure("invalid_argument", error, new { maxResults = 100 }),
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
                nextActions = SuggestActions(elements),
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
                return JsonSerializer.Serialize(
                    RepairableFailure("missing_target", targetError, new { tool = "set_target" }),
                    JsonOptions.Default);
            }

            IReadOnlyList<UiInteractable> elements = scanner.Describe(pid, windowTitle, 100);
            memory.RecordObservation(pid, windowTitle, elements);
            List<RankedInteractable> ranked = RankElements(goal, elements, maxResults: 5);
            RankedInteractable? first = ranked.Count > 0 ? ranked[0] : null;
            UiInteractable? best = first?.Element;
            string action = ChooseAction(goal, text, best);
            List<BatchStep> steps = new();
            if (best is not null)
            {
                steps.Add(BuildStep(action, best, text, pid, windowTitle));
                if (!string.IsNullOrEmpty(expectedValue))
                {
                    steps.Add(BuildAssertionStep(best, expectedValue, pid, windowTitle));
                }
            }

            var response = new
            {
                goal,
                confidence = first?.Score ?? 0,
                requiresReview = steps.Count == 0 || (first?.Score ?? 0) < 30,
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
                return JsonSerializer.Serialize(
                    RepairableFailure("missing_target", targetError, new { tool = "set_target" }),
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
                suggestedFix = new
                {
                    useTopCurrentUiCandidate = true,
                    thenRetryOriginalAction = true,
                    fallbackTool = "observe",
                },
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
            if (!target.TryResolve(processId ?? step.ProcessId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(
                    RepairableFailure("missing_target", targetError, new { tool = "set_target" }),
                    JsonOptions.Default);
            }

            bool effectiveDryRun = dryRun || ToolAccessPolicy.IsDryRunEnabled;
            if (ToolAccessPolicy.DryRunMutation("act_and_observe", new { processId = pid, step }) is { } dryPreview)
            {
                return JsonSerializer.Serialize(dryPreview, JsonOptions.Default);
            }

            UiSnapshot before = snapshotter.Capture(pid, windowTitle);
            BatchResult result = runner.Run(
                new[] { step },
                pid,
                stopOnError: true,
                maxDurationMs: null,
                dryRun: effectiveDryRun) with { DryRun = effectiveDryRun };
            UiSnapshot after = snapshotter.Capture(pid, windowTitle);
            UiChange change = UiDiffer.Diff(before, after);
            IReadOnlyList<UiInteractable> elements = scanner.Describe(pid, windowTitle, 100);
            memory.RecordObservation(pid, windowTitle, elements);

            ScreenshotResult? screenshot = includeScreenshot
                ? capture.Capture(pid, ScreenshotOutput.BuildTimestampedPath(ObservePrefix), windowTitle)
                : null;
            return JsonSerializer.Serialize(
                OperationResult.OkWith(new
                {
                    processId = pid,
                    action = result,
                    change,
                    elements,
                    screenshot,
                }),
                JsonOptions.Default);
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
                    RepairableFailure("missing_target", targetError, new { tool = "set_target" }),
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
            if (ToolAccessPolicy.DryRunMutation(
                    "click_and_wait",
                    new { processId = pid, clickLocator, waitLocator, condition })
                is { } dryRun)
            {
                return JsonSerializer.Serialize(dryRun, JsonOptions.Default);
            }
            if (!WaitConditionParser.TryParse(condition, out WaitCondition parsed))
            {
                return JsonSerializer.Serialize(
                    RepairableFailure(
                        "invalid_wait_condition",
                        $"Unknown condition \"{condition}\". Accepted values: {WaitConditionParser.AcceptedValues}.",
                        new { condition = "exists" }),
                    JsonOptions.Default);
            }

            UiSnapshot before = snapshotter.Capture(pid, windowTitle);
            OperationResult click = invoker.Click(pid, clickLocator, ClickStyle.Primary, windowTitle);
            OperationResult wait = click.Success
                ? waiter.Wait(pid, waitLocator, parsed, timeoutMs, null, windowTitle, expectedValue, null)
                : OperationResult.OkWith(new { skipped = true, reason = "click_failed" });
            UiSnapshot after = snapshotter.Capture(pid, windowTitle);
            return JsonSerializer.Serialize(
                OperationResult.OkWith(new { click, wait, change = UiDiffer.Diff(before, after) }),
                JsonOptions.Default);
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
                    RepairableFailure("missing_target", targetError, new { tool = "set_target" }),
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
            if (ToolAccessPolicy.DryRunMutation(
                    "type_and_assert",
                    new { processId = pid, typeLocator, assertLocator, textLength = text.Length, @operator, expected })
                is { } dryRun)
            {
                return JsonSerializer.Serialize(dryRun, JsonOptions.Default);
            }
            if (!AssertionEvaluator.TryParseOperator(@operator, out AssertionOperator parsed))
            {
                return JsonSerializer.Serialize(
                    RepairableFailure(
                        "invalid_assertion_operator",
                        $"Unknown operator \"{@operator}\". Accepted: {AssertionEvaluator.AcceptedOperators}.",
                        new { @operator = "contains" }),
                    JsonOptions.Default);
            }

            OperationResult type = invoker.Type(pid, typeLocator, text, windowTitle, focusWindow: false);
            OperationResult read = reader.Read(pid, assertLocator, windowTitle);
            OperationResult assertion = read.Success
                ? BuildAssertion(parsed, expected, (ElementState)read.Data!)
                : read;
            return JsonSerializer.Serialize(OperationResult.OkWith(new { type, assertion }), JsonOptions.Default);
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
                        perceive = "observe",
                        cautiousAction = "act_and_observe",
                        batchAction = "run_steps",
                        locatorRepair = "repair_locator",
                        replay = "replay_steps",
                    },
                    errorPolicy = new
                    {
                        missing_target = "call set_target or pass processId",
                        locator_ambiguous = "use candidates[0].locatorArgs or add index",
                        locator_not_found = "call repair_locator with the failed label/id",
                        policy_denied = "change WINSIGHT_ALLOWED_* or choose another process",
                    },
                }),
                JsonOptions.Default));
    }

    private static OperationResult RepairableFailure(string code, string message, object? suggestedFix)
    {
        return OperationResult.FailureWith(
            message,
            new
            {
                code,
                message,
                suggestedFix,
            });
    }

    private static List<object> SuggestActions(IReadOnlyList<UiInteractable> elements)
    {
        return elements
            .Where(element => element.IsEnabled && element.IsVisible)
            .Take(20)
            .Select(element => new
            {
                action = ActionFor(element),
                label = string.IsNullOrEmpty(element.Name) ? element.AutomationId : element.Name,
                element.ControlType,
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

    private static string ChooseAction(string goal, string? text, UiInteractable? element)
    {
        if (!string.IsNullOrEmpty(text) ||
            goal.Contains("type", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("enter", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("fill", StringComparison.OrdinalIgnoreCase) ||
            element?.ControlType == "Edit")
        {
            return "type";
        }
        if (goal.Contains("select", StringComparison.OrdinalIgnoreCase) ||
            element?.ControlType is "ComboBox" or "List" or "ListItem" or "TabItem")
        {
            return "select";
        }
        return "click";
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
            "ComboBox" or "List" or "ListItem" or "Tab" or "TabItem" => "select",
            _ => "click",
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

    private sealed record RankedInteractable(int Score, string Reason, UiInteractable Element);

    private sealed record LocatorParts(
        string? AutomationId,
        string? Name,
        string? ControlType,
        int? Index);
}
