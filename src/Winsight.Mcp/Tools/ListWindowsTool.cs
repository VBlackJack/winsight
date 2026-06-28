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
public static class ListWindowsTool
{
    [McpServerTool(Name = "list_windows")]
    [Description("Enumerate top-level windows, in z-order (topmost first). Without processId, returns " +
                 "every named window on the desktop — use it to discover candidate target processes. " +
                 "With processId, returns that process's full window stack exhaustively, including " +
                 "untitled modal dialogs, MessageBoxes, dropdown menus, flyouts, and tooltip overlays " +
                 "that desktop enumeration hides — use it to find the dialog or overlay to act on, then " +
                 "scope other tools to it via windowTitle. Each window reports process id, native window " +
                 "handle, title, process name, foreground flag, bounding rectangle, plus kind " +
                 "(main/dialog/popup/menu/tooltip/other), className, ownerHandle, and isModal. " +
                 "On failure, returns an OperationResult JSON with success=false and an error message.")]
    public static string ListWindows(
        WindowDiscovery discovery,
        [Description("Optional process id. When set, returns that process's full window stack " +
                     "(including untitled dialogs, popups, and menus). When omitted, returns every " +
                     "named top-level window on the desktop.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (processId is int pid &&
                !ToolAccessPolicy.TryAuthorizeProcess(pid, out string error))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(error), JsonOptions.Default);
            }

            IReadOnlyList<WindowDescriptor> windows = discovery.List(processId);
            return JsonSerializer.Serialize(windows, JsonOptions.Default);
        });
    }
}
