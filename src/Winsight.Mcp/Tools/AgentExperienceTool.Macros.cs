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
    [McpServerTool(Name = "click_and_wait", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
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
            bool hasSeparateWaitTarget = waitAutomationId is not null || waitName is not null || waitControlType is not null;
            ElementLocator waitLocator = hasSeparateWaitTarget
                ? LocatorFactory.From(waitAutomationId, waitName, null, waitControlType, null, null, null)
                : clickLocator;
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

    [McpServerTool(Name = "type_and_assert", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
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
            bool hasSeparateAssertTarget = assertAutomationId is not null || assertName is not null;
            ElementLocator assertLocator = hasSeparateAssertTarget
                ? LocatorFactory.From(assertAutomationId, assertName, null, null, null, null, null)
                : typeLocator;
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

}
