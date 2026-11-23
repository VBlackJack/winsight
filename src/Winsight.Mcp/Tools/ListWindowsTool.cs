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
    [McpServerTool(Name = "list_windows", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Enumerate top-level windows, in z-order (topmost first). Without processId, returns " +
                 "every named window on the desktop; use it to discover candidate target processes. " +
                 "In that desktop-wide mode the titles of windows that do not belong to the session " +
                 "target are withheld (title is empty and titleWithheld=true) because they can expose " +
                 "unrelated documents, chats, and browser tabs: pick the process by processName, " +
                 "className, and bounds, or pass includeTitles=true to opt in to every title. " +
                 "With processId, returns that process's full window stack exhaustively, including " +
                 "untitled modal dialogs, MessageBoxes, dropdown menus, flyouts, and tooltip overlays " +
                 "that desktop enumeration hides; use it to find the dialog or overlay to act on, then " +
                 "scope other tools to it via windowTitle. Each window reports process id, native window " +
                 "handle, title, process name, foreground flag, bounding rectangle, plus kind " +
                 "(main/dialog/popup/menu/tooltip/other), className, ownerHandle, and isModal. " +
                 "Each entry also carries titleWithheld. " +
                 "On failure, returns an OperationResult JSON with success=false and an error message.")]
    public static string ListWindows(
        WindowDiscovery discovery,
        SessionTarget target,
        [Description("Optional process id. When set, returns that process's full window stack " +
                     "(including untitled dialogs, popups, and menus) with full titles. When omitted, " +
                     "returns every named top-level window on the desktop, with titles of non-target " +
                     "processes withheld unless includeTitles=true.")]
        int? processId = null,
        [Description("Desktop-wide mode only (processId omitted): set true to return the titles of every " +
                     "window, including processes that are not the session target. Default false. Use it " +
                     "only when you must locate an app by window title.")]
        bool includeTitles = false)
    {
        return List(discovery, target.ProcessId, processId, includeTitles);
    }

    /// <summary>
    /// Compatibility overload for callers without a session target: desktop-wide
    /// listings withhold every title, per-process listings keep them.
    /// </summary>
    public static string ListWindows(WindowDiscovery discovery, int? processId)
        => List(discovery, targetProcessId: null, processId, includeTitles: false);

    /// <summary>
    /// List windows for the given session target. <paramref name="processId"/> selects
    /// per-process mode (full titles); without it the desktop-wide titles of non-target
    /// processes are withheld unless <paramref name="includeTitles"/> is set.
    /// </summary>
    public static string List(
        WindowDiscovery discovery,
        int? targetProcessId,
        int? processId,
        bool includeTitles)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (processId is int pid &&
                !ToolAccessPolicy.TryAuthorizeProcess(pid, out string error))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(error), JsonOptions.Default);
            }

            return Render(discovery.List(processId), targetProcessId, processId, includeTitles);
        });
    }

    /// <summary>Serialize discovered windows after applying the title-withholding policy.</summary>
    public static string Render(
        IReadOnlyList<WindowDescriptor> windows,
        int? targetProcessId,
        int? processId,
        bool includeTitles)
    {
        bool keepAllTitles = includeTitles || processId is not null;
        return JsonSerializer.Serialize(
            WindowListProjection.Project(windows, targetProcessId, keepAllTitles),
            JsonOptions.Default);
    }
}
