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
using Winsight.Core.Modules.Lifecycle;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class LifecycleTool
{
    [McpServerTool(Name = "launch_app", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Start an application and wait until its window is actually present before returning, " +
                 "so you never race a still-initializing UI. Returns an OperationResult whose data " +
                 "carries the new processId and a classified window descriptor (kind, bounds, etc.) " +
                 "ready to drive, with no follow-up list_windows/poll needed. Fails if the process exits " +
                 "during startup or no window appears within the timeout.")]
    public static string LaunchApp(
        AppLauncher launcher,
        [Description("Absolute path to the executable to launch.")]
        string path,
        [Description("Optional command-line arguments passed to the process.")]
        string? arguments = null,
        [Description("Optional working directory. Defaults to the executable's own directory.")]
        string? workingDirectory = null,
        [Description("Optional case-insensitive substring of the window title to wait for. " +
                     "When omitted, the launcher waits for the process's main window.")]
        string? waitForWindowTitle = null,
        [Description("Max milliseconds to wait for the window. Default 15000; max 120000.")]
        int timeoutMs = AppLauncher.DefaultLaunchTimeoutMs)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (ToolAccessPolicy.GuardMutation("launch_app", new { path, arguments, workingDirectory }) is { } gated)
            {
                return JsonSerializer.Serialize(gated, JsonOptions.Default);
            }
            if (!ToolAccessPolicy.TryAuthorizeLaunch(path, out string policyError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(policyError), JsonOptions.Default);
            }
            OperationResult result = launcher.Launch(
                path, arguments, workingDirectory, waitForWindowTitle, timeoutMs);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "close_app", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Close a process by id. By default requests a graceful shutdown (CloseMainWindow) and " +
                 "reports stillRunning if it does not exit within the grace window; set force=true to " +
                 "kill the process tree immediately. Returns an OperationResult; a process that is " +
                 "already gone is reported as success.")]
    public static string CloseApp(
        AppLauncher launcher,
        SessionTarget target,
        [Description("Kill the process tree immediately instead of requesting a graceful close.")]
        bool force = false,
        [Description("Process id to close (from launch_app or list_windows). Optional; falls back to the session default set by set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            if (ToolAccessPolicy.GuardMutation("close_app", new { processId = pid, force }) is { } gated)
            {
                return JsonSerializer.Serialize(gated, JsonOptions.Default);
            }
            OperationResult result = launcher.Close(pid, force);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }
}
