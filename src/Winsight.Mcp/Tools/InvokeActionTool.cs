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
    [Description("Locate a UI element by automationId, name, or className inside the given process " +
                 "and trigger its primary action (Invoke, Toggle, or fallback mouse click). " +
                 "Returns an OperationResult JSON with success/error.")]
    public static string ClickElement(
        ActionInvoker invoker,
        [Description("Process id of the target application.")]
        int processId,
        [Description("AutomationId of the element. Optional if name or className is provided.")]
        string? automationId = null,
        [Description("Name property of the element. Optional if automationId or className is provided.")]
        string? name = null,
        [Description("ClassName of the element. Optional if automationId or name is provided.")]
        string? className = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            var locator = new ElementLocator(automationId, name, className);
            var result = invoker.Click(processId, locator);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "type_text")]
    [Description("Locate a UI element and set its value (via the Value pattern) or type into it " +
                 "(via keyboard fallback). Returns an OperationResult JSON with success/error.")]
    public static string TypeText(
        ActionInvoker invoker,
        [Description("Process id of the target application.")]
        int processId,
        [Description("Text to type into the element.")]
        string text,
        [Description("AutomationId of the element. Optional if name or className is provided.")]
        string? automationId = null,
        [Description("Name property of the element. Optional if automationId or className is provided.")]
        string? name = null,
        [Description("ClassName of the element. Optional if automationId or name is provided.")]
        string? className = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            var locator = new ElementLocator(automationId, name, className);
            var result = invoker.Type(processId, locator, text);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }
}
