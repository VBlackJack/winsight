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
public static class SessionTargetTool
{
    [McpServerTool(Name = "set_target")]
    [Description("Set the session's default target process so you do not repeat processId on every call. " +
                 "Once set, any process-scoped tool (click_element, type_text, wait_for_element, " +
                 "capture_screenshot, run_steps, …) uses it when its own processId is omitted; an " +
                 "explicit processId on a call still overrides it. Returns the target plus how many " +
                 "top-level windows the process currently exposes (0 is allowed — it may still be " +
                 "starting). Call this right after launch_app or list_windows.")]
    public static string SetTarget(
        SessionTarget target,
        WindowDiscovery discovery,
        [Description("Process id to make the session default (from launch_app or list_windows).")]
        int processId)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!ToolAccessPolicy.TryAuthorizeProcess(processId, out string error))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(error), JsonOptions.Default);
            }

            int windowCount = discovery.List(processId).Count;
            target.Set(processId);
            return JsonSerializer.Serialize(
                OperationResult.OkWith(new { processId, windowCount }),
                JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "get_target")]
    [Description("Return the session's current default target process, or null when none is set.")]
    public static string GetTarget(SessionTarget target)
    {
        return ToolSafety.SafeInvoke(() =>
            JsonSerializer.Serialize(
                OperationResult.OkWith(new { processId = target.ProcessId }),
                JsonOptions.Default));
    }

    [McpServerTool(Name = "clear_target")]
    [Description("Clear the session's default target process. Subsequent process-scoped calls must " +
                 "pass processId explicitly again until a new set_target.")]
    public static string ClearTarget(SessionTarget target)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            target.Clear();
            return JsonSerializer.Serialize(OperationResult.Ok(), JsonOptions.Default);
        });
    }
}
