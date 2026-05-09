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
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class InspectUiTreeTool
{
    [McpServerTool(Name = "inspect_ui_tree")]
    [Description("Walk the UI Automation tree of a process's main window and return its structure. " +
                 "Each node carries automationId, name, controlType, className, enabled/visible flags, " +
                 "and its bounding rectangle. Use this to discover automation ids and locate elements " +
                 "for click_element or type_text.")]
    public static string InspectUiTree(
        UiTreeInspector inspector,
        [Description("Process id of the target application (from list_windows).")]
        int processId,
        [Description("Maximum tree depth to walk. Defaults to 5; max 20.")]
        int maxDepth = 5)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            var node = inspector.Inspect(processId, maxDepth);
            return JsonSerializer.Serialize(node, JsonOptions.Default);
        });
    }
}
