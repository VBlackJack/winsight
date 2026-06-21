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
public static class DescribeUiTool
{
    [McpServerTool(Name = "describe_ui")]
    [Description("Answer \"what can I do here?\" — return the window's actionable elements as a compact, " +
                 "position-ordered list (top-to-bottom, left-to-right) instead of a full UIA tree. Each " +
                 "entry keeps only what you need to act: controlType, label (name), value/toggle/selection " +
                 "and enabled/visible state, bounds, and a recommendedLocator — the smallest criterion " +
                 "(automationId, else name, else controlType+index) that targets it for click_element / " +
                 "type_text / select_item. Use this to orient on an unfamiliar window before acting; reach " +
                 "for inspect_ui_tree only when you need the full structure. Interactable kinds: buttons, " +
                 "text inputs, checkboxes, radio buttons, combo boxes, lists and items, tabs, menu items, " +
                 "hyperlinks, tree items, sliders.")]
    public static string DescribeUi(
        AffordanceScanner scanner,
        SessionTarget target,
        [Description("Inspect the first window whose title contains this text (modal dialogs, popups). " +
                     "Omit to use the process's first window.")]
        string? windowTitle = null,
        [Description("Maximum elements to return. Default 200.")]
        int maxResults = AffordanceScanner.DefaultMaxResults,
        [Description("Target process id (from list_windows). Optional — falls back to the session default set by set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }

            IReadOnlyList<UiInteractable> elements = scanner.Describe(pid, windowTitle, maxResults);
            return JsonSerializer.Serialize(OperationResult.OkWith(elements), JsonOptions.Default);
        });
    }
}
