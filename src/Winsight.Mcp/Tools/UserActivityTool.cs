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
using Winsight.Core.Modules.UserActivity;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

/// <summary>
/// Shared-desktop awareness: is a person using this screen right now? Both
/// tools only read; neither sends input.
/// </summary>
[McpServerToolType]
public static class UserActivityTool
{
    private const string ProcessIdDescription =
        "Process whose foreground window title may be returned. Optional: falls back to the session " +
        "target. Titles of other processes are always withheld.";

    [McpServerTool(Name = "get_user_activity")]
    [Description("Report whether a person is using this desktop, before taking the mouse or keyboard. " +
                 "Returns humanIdleMs (time since the last input not injected by any Winsight process in " +
                 "this session; null if none was seen), lastInputAgeMs and lastInputSource (human or " +
                 "winsight), the foreground window (handle, processId, processName; its title only when it " +
                 "belongs to the target process), sessionLocked, the WINSIGHT_YIELD_TO_USER_MS threshold and " +
                 "userActive (true when a human input is more recent than that threshold). Input injected by " +
                 "other tools counts as human. Read-only.")]
    public static string GetUserActivity(
        UserActivityMonitor monitor,
        SessionTarget target,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
            JsonSerializer.Serialize(
                OperationResult.OkWith(monitor.Read(RevealTitleFor(processId, target))),
                JsonOptions.Default));
    }

    [McpServerTool(Name = "wait_for_user_idle")]
    [Description("Wait until nobody has touched the mouse or keyboard for idleMs (Winsight's own input " +
                 "excluded), or until timeoutMs elapses. Call it before a batch on a shared screen, or after " +
                 "a user_active or foreground_changed error. Returns { idle, waitedMs, idleMs, activity }; " +
                 "on timeout the call fails with timed_out and the same data. Never sends input.")]
    public static string WaitForUserIdle(
        UserActivityMonitor monitor,
        SessionTarget target,
        [Description("Required quiet period in milliseconds (1 to 600000).")]
        int idleMs,
        [Description("Maximum wait in milliseconds (0 to 600000). 0 checks once.")]
        int timeoutMs,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
            JsonSerializer.Serialize(
                monitor.WaitForIdle(idleMs, timeoutMs, revealTitleFor: RevealTitleFor(processId, target)),
                JsonOptions.Default));
    }

    private static int[] RevealTitleFor(int? processId, SessionTarget target) =>
        (processId ?? target.ProcessId) is int pid ? [pid] : [];
}
