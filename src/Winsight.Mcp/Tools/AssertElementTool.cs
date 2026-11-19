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
public static class AssertElementTool
{
    [McpServerTool(Name = "assert_element", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Assert a condition on a located element and get a clear pass/fail. Operators: equals, " +
                 "not_equals, contains, not_contains (on the element's value, else its name); greater_than, " +
                 "less_than (numeric on the value); enabled, disabled, visible, hidden; checked, unchecked " +
                 "(checkboxes); selected; exists. Provide 'expected' for the text/numeric operators. " +
                 "Returns success=true with the detail when the assertion holds, success=false with an " +
                 "explanatory detail (expected vs actual) when it fails; unlike wait_for_element, this is " +
                 "an immediate check, not a poll. Same strict locator semantics as click_element. " +
                 "The same operators are available as an 'assert' step in run_steps.")]
    public static string AssertElement(
        ElementStateReader reader,
        SessionTarget target,
        [Description("Assertion operator (see the list in this tool's description).")]
        string @operator,
        [Description("Expected operand for text/numeric operators (equals, contains, greater_than, ...).")]
        string? expected = null,
        [Description("Exact AutomationId of the element.")]
        string? automationId = null,
        [Description("Exact Name property of the element.")]
        string? name = null,
        [Description("Exact ClassName of the element.")]
        string? className = null,
        [Description("UIA control type (e.g. Text, CheckBox, Edit), case-insensitive.")]
        string? controlType = null,
        [Description("Case-insensitive substring of the Name property.")]
        string? nameContains = null,
        [Description("Zero-based index to pick one element when several match.")]
        int? index = null,
        [Description("Case-insensitive substring exposed by a descendant's Name or Value, searched to bounded depth.")]
        string? descendantText = null,
        [Description("Restrict the search to windows whose title contains this text (modal dialogs, popups).")]
        string? windowTitle = null,
        [Description("Target process id. Optional; falls back to the session default set by set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            if (!AssertionEvaluator.TryParseOperator(@operator, out AssertionOperator op))
            {
                return JsonSerializer.Serialize(
                    OperationResult.Failure(
                        $"Unknown operator \"{@operator}\". Accepted: {AssertionEvaluator.AcceptedOperators}."),
                    JsonOptions.Default);
            }

            ElementLocator locator = LocatorFactory.From(
                automationId, name, className, controlType, nameContains, index, descendantText);
            OperationResult read = reader.Read(pid, locator, windowTitle);
            if (!read.Success)
            {
                return JsonSerializer.Serialize(read, JsonOptions.Default);
            }

            AssertionResult outcome = AssertionEvaluator.Evaluate(op, expected, (ElementState)read.Data!);
            OperationResult result = outcome.Passed
                ? OperationResult.OkWith(new { assertion = outcome.Detail })
                : OperationResult.Failure($"Assertion failed: {outcome.Detail}");
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }
}
