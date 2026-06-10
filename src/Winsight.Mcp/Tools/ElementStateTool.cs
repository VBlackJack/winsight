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
    [McpServerTool(Name = "get_element_state")]
    [Description("Read the observable state of a located UI element: name, value (Value pattern), " +
                 "toggle state, selection, enabled/visible/focus flags, and bounding rectangle. " +
                 "This is the lightweight UI-level oracle for assertions — use it instead of " +
                 "read_managed_state when the on-screen state is enough. Same strict locator " +
                 "semantics as click_element.")]
    public static string GetElementState(
        ElementStateReader reader,
        [Description("Process id of the target application.")]
        int processId,
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
        [Description("Restrict the search to windows whose title contains this text (modal dialogs, popups).")]
        string? windowTitle = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            ElementLocator locator = new(automationId, name, className, controlType, nameContains, index);
            OperationResult result = reader.Read(processId, locator, windowTitle);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "wait_for_element")]
    [Description("Poll until a condition holds for a locator: exists, not_exists, enabled, or visible. " +
                 "Use it to synchronize before clicking (wait for a button to appear or enable) and " +
                 "after acting (wait for a dialog to close with not_exists). Returns the elapsed time " +
                 "and the element's final state. Fails when the timeout elapses first.")]
    public static string WaitForElement(
        ElementWaiter waiter,
        [Description("Process id of the target application.")]
        int processId,
        [Description("Condition to wait for: exists, not_exists, enabled, or visible.")]
        string condition,
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
        [Description("Restrict the search to windows whose title contains this text (modal dialogs, popups).")]
        string? windowTitle = null,
        [Description("Timeout in milliseconds. Defaults to 10000 (or WINSIGHT_UIA_WAIT_TIMEOUT_MS); max 120000.")]
        int? timeoutMs = null,
        [Description("Delay between polls in milliseconds. Defaults to 250; min 50.")]
        int? pollIntervalMs = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!WaitConditionParser.TryParse(condition, out WaitCondition parsed))
            {
                return JsonSerializer.Serialize(
                    OperationResult.Failure(
                        $"Unknown condition \"{condition}\". Accepted values: {WaitConditionParser.AcceptedValues}."),
                    JsonOptions.Default);
            }

            ElementLocator locator = new(automationId, name, className, controlType, nameContains, index);
            OperationResult result = waiter.Wait(processId, locator, parsed, timeoutMs, pollIntervalMs, windowTitle);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }
}
