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
public static class ElementStateTool
{
    private const string ProcessIdDescription =
        "Target process id. Optional — falls back to the session default set by set_target.";

    [McpServerTool(Name = "get_element_state")]
    [Description("Read the observable state of a located UI element: name, value (Value pattern), " +
                 "toggle state, selection, enabled/visible/focus flags, and bounding rectangle. " +
                 "This is the lightweight UI-level oracle for assertions — use it instead of " +
                 "read_managed_state when the on-screen state is enough. Same strict locator " +
                 "semantics as click_element.")]
    public static string GetElementState(
        ElementStateReader reader,
        SessionTarget target,
        [Description("Exact AutomationId of the element.")]
        string? automationId = null,
        [Description("Exact Name property of the element.")]
        string? name = null,
        [Description("Exact ClassName of the element.")]
        string? className = null,
        [Description("UIA control type (e.g. Text, CheckBox, ComboBox), case-insensitive.")]
        string? controlType = null,
        [Description("Case-insensitive substring of the Name property.")]
        string? nameContains = null,
        [Description("Zero-based index to pick one element when several match.")]
        int? index = null,
        [Description("Case-insensitive substring exposed by a descendant's Name or Value, searched to bounded depth.")]
        string? descendantText = null,
        [Description("Restrict the search to windows whose title contains this text (modal dialogs, popups).")]
        string? windowTitle = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }

            ElementLocator locator = LocatorFactory.From(
                automationId, name, className, controlType, nameContains, index, descendantText);
            OperationResult result = reader.Read(pid, locator, windowTitle);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "wait_for_element")]
    [Description("Poll until a condition holds for a locator: exists, not_exists, enabled, visible, " +
                 "value_equals, value_contains, or stable_for. " +
                 "Use it to synchronize before clicking (wait for a button to appear or enable) and " +
                 "after acting (wait for a dialog to close with not_exists). Returns the elapsed time " +
                 "and the element's final state. Fails when the timeout elapses first.")]
    public static string WaitForElement(
        ElementWaiter waiter,
        SessionTarget target,
        [Description("Condition to wait for: exists, not_exists, enabled, visible, value_equals, value_contains, or stable_for.")]
        string condition,
        [Description("Expected Value-pattern content for value_equals or value_contains.")]
        string? expectedValue = null,
        [Description("Exact AutomationId of the element.")]
        string? automationId = null,
        [Description("Exact Name property of the element.")]
        string? name = null,
        [Description("Exact ClassName of the element.")]
        string? className = null,
        [Description("UIA control type (e.g. Button, Window), case-insensitive.")]
        string? controlType = null,
        [Description("Case-insensitive substring of the Name property.")]
        string? nameContains = null,
        [Description("Zero-based index to pick one element when several match.")]
        int? index = null,
        [Description("Case-insensitive substring exposed by a descendant's Name or Value, searched to bounded depth.")]
        string? descendantText = null,
        [Description("Restrict the search to windows whose title contains this text (modal dialogs, popups).")]
        string? windowTitle = null,
        [Description("Timeout in milliseconds. Defaults to 10000 (or WINSIGHT_UIA_WAIT_TIMEOUT_MS); max 120000.")]
        int? timeoutMs = null,
        [Description("Delay between polls in milliseconds. Defaults to 250; min 50.")]
        int? pollIntervalMs = null,
        [Description("Required unchanged-state duration in milliseconds for stable_for.")]
        int? stableForMs = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }

            if (!WaitConditionParser.TryParse(condition, out WaitCondition parsed))
            {
                return JsonSerializer.Serialize(
                    OperationResult.Failure(
                        $"Unknown condition \"{condition}\". Accepted values: {WaitConditionParser.AcceptedValues}."),
                    JsonOptions.Default);
            }

            ElementLocator locator = LocatorFactory.From(
                automationId, name, className, controlType, nameContains, index, descendantText);
            OperationResult result = waiter.Wait(
                pid,
                locator,
                parsed,
                timeoutMs,
                pollIntervalMs,
                windowTitle,
                expectedValue,
                stableForMs);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }
}
