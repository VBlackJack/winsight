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
public static class InvokeActionTool
{
    [McpServerTool(Name = "click_element")]
    [Description("Locate a UI element and click it. Locator criteria are combined with AND across all " +
                 "top-level windows of the process; if several elements match, the call fails and lists " +
                 "the candidates so you can add 'index' or narrow the locator. Off-screen elements are " +
                 "scrolled into view first. Returns an OperationResult JSON with success/error.")]
    public static string ClickElement(
        ActionInvoker invoker,
        [Description("Process id of the target application.")]
        int processId,
        [Description("Exact AutomationId of the element.")]
        string? automationId = null,
        [Description("Exact Name property of the element.")]
        string? name = null,
        [Description("Exact ClassName of the element.")]
        string? className = null,
        [Description("UIA control type (e.g. Button, Edit, CheckBox), case-insensitive.")]
        string? controlType = null,
        [Description("Case-insensitive substring of the Name property.")]
        string? nameContains = null,
        [Description("Zero-based index to pick one element when several match.")]
        int? index = null,
        [Description("Restrict the search to windows whose title contains this text (modal dialogs, popups).")]
        string? windowTitle = null,
        [Description("Click style: single (default, prefers Invoke/Toggle patterns), double, or right.")]
        string? clickStyle = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            ClickStyle style = ClickStyle.Primary;
            if (!string.IsNullOrEmpty(clickStyle) && !ClickStyleParser.TryParse(clickStyle, out style))
            {
                return JsonSerializer.Serialize(
                    OperationResult.Failure(
                        $"Unknown clickStyle \"{clickStyle}\". Accepted values: {ClickStyleParser.AcceptedValues}."),
                    JsonOptions.Default);
            }

            ElementLocator locator = new(automationId, name, className, controlType, nameContains, index);
            OperationResult result = invoker.Click(processId, locator, style, windowTitle);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "type_text")]
    [Description("Locate a UI element and set its value (via the Value pattern) or type into it " +
                 "(via keyboard fallback). Same strict locator semantics as click_element. " +
                 "Returns an OperationResult JSON with success/error.")]
    public static string TypeText(
        ActionInvoker invoker,
        [Description("Process id of the target application.")]
        int processId,
        [Description("Text to type into the element.")]
        string text,
        [Description("Exact AutomationId of the element.")]
        string? automationId = null,
        [Description("Exact Name property of the element.")]
        string? name = null,
        [Description("Exact ClassName of the element.")]
        string? className = null,
        [Description("UIA control type (e.g. Edit, ComboBox), case-insensitive.")]
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
            OperationResult result = invoker.Type(processId, locator, text, windowTitle);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "select_item")]
    [Description("Select an item by its visible text inside a located container: ComboBox, ListBox, " +
                 "or Tab (other containers fall back to a descendant SelectionItem search). " +
                 "On failure the available item names are listed. Returns an OperationResult JSON.")]
    public static string SelectItem(
        ActionInvoker invoker,
        [Description("Process id of the target application.")]
        int processId,
        [Description("Visible text of the item to select.")]
        string itemName,
        [Description("Exact AutomationId of the container element.")]
        string? automationId = null,
        [Description("Exact Name property of the container element.")]
        string? name = null,
        [Description("Exact ClassName of the container element.")]
        string? className = null,
        [Description("UIA control type of the container (e.g. ComboBox, List, Tab), case-insensitive.")]
        string? controlType = null,
        [Description("Case-insensitive substring of the container's Name property.")]
        string? nameContains = null,
        [Description("Zero-based index to pick one container when several match.")]
        int? index = null,
        [Description("Restrict the search to windows whose title contains this text (modal dialogs, popups).")]
        string? windowTitle = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            ElementLocator locator = new(automationId, name, className, controlType, nameContains, index);
            OperationResult result = invoker.SelectItem(processId, locator, itemName, windowTitle);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }
}
