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
public static class InspectUiTreeTool
{
    [McpServerTool(Name = "inspect_ui_tree")]
    [Description("Walk the UI Automation tree of a process window and return its structure. " +
                 "Each node carries automationId, name, controlType, className, enabled/visible flags, " +
                 "current value (Value pattern, when supported), and its bounding rectangle. " +
                 "Use this to discover automation ids and locate elements for click_element or type_text. " +
                 "Filters prune the tree to matching nodes and their ancestors. Without windowTitle the " +
                 "topmost window that is not a transient overlay is inspected: an open tooltip or menu is " +
                 "skipped; set mainWindow=true to force the process main window.")]
    public static string InspectUiTree(
        UiTreeInspector inspector,
        SessionTarget target,
        [Description("Maximum tree depth to walk. Defaults to 5; max 20.")]
        int maxDepth = 5,
        [Description("Inspect the first window whose title contains this text instead of the first window of the process (modal dialogs, popups).")]
        string? windowTitle = null,
        [Description("Keep only nodes of this UIA control type (e.g. Button, Edit), case-insensitive, plus their ancestors.")]
        string? controlType = null,
        [Description("Keep only nodes whose Name contains this text (case-insensitive), plus their ancestors.")]
        string? nameContains = null,
        [Description("Inspect the process main window even when a dialog or overlay is on top of it. Ignored when windowTitle is set.")]
        bool mainWindow = false,
        [Description("Target process id (from list_windows). Optional; falls back to the session default set by set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            UiElementNode node = inspector.Inspect(pid, maxDepth, windowTitle, controlType, nameContains, mainWindow);
            return JsonSerializer.Serialize(node, JsonOptions.Default);
        });
    }
}
