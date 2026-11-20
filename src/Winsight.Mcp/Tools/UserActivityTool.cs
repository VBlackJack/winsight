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
using ModelContextProtocol;
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

    [McpServerTool(Name = "get_user_activity", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Report whether a person is using this desktop, before taking the mouse or keyboard. " +
                 "Returns humanIdleMs (time since the last input not attributed to Winsight; null if none " +
                 "was seen), lastInputAgeMs, lastInputSource (human, winsight, other_injected when " +
                 "WINSIGHT_INPUT_HOOK=1 is enabled, or periodic_micro_move when WINSIGHT_IGNORE_PERIODIC_MICRO_MOVES=1 " +
                 "filters a hardware mouse jiggler), foreground (handle, processId, processName; title only " +
                 "for the target process), sessionLocked, yieldToUserMs, userActive, yieldOverridden (true " +
                 "only when the human enabled WINSIGHT_YIELD_OVERRIDE=1 in host config), and diagnostics " +
                 "(inputKind, cursorMoved, cursorPosition, inputHookActive, lastInputInjectedByOther, " +
                 "recentInputs, periodicMicroMoveFilterEnabled, periodicMicroMoveThresholdPx, " +
                 "lastInputIgnoredAsPeriodicMicroMove). Read-only.")]
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

    [McpServerTool(Name = "wait_for_user_idle", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Wait until nobody has touched the mouse or keyboard for idleMs (Winsight's own input " +
                 "excluded), or until the full timeoutMs elapses. Use a real timeout (for example " +
                 "idleMs=3000, timeoutMs=30000) before a batch on a shared screen or after user_active / " +
                 "foreground_changed, then call observe_scene again before acting. Returns " +
                 "{ idle, waitedMs, idleMs, activity }; on timeout fails with timed_out and the same data. " +
                 "Never sends input.")]
    public static Task<string> WaitForUserIdle(
        UserActivityMonitor monitor,
        SessionTarget target,
        [Description("Required quiet period in milliseconds (1 to 600000).")]
        int idleMs,
        [Description("Maximum wait in milliseconds (0 to 600000). 0 checks once.")]
        int timeoutMs,
        [Description(ProcessIdDescription)]
        int? processId = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSafety.SafeInvokeAsync(async () =>
        {
            Action<long, int, bool?>? onProgress = progress is null
                ? null
                : (waitedMs, totalMs, completedIdle) => progress.Report(new ProgressNotificationValue
                {
                    Progress = waitedMs,
                    Total = totalMs,
                    Message = completedIdle switch
                    {
                        true => "Desktop is idle.",
                        false => "Wait for user idle timed out.",
                        null => $"Waiting for user idle ({waitedMs}/{totalMs} ms)...",
                    },
                });

            OperationResult result = await monitor.WaitForIdleAsync(
                idleMs,
                timeoutMs,
                revealTitleFor: RevealTitleFor(processId, target),
                onProgress: onProgress,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }

    private static int[] RevealTitleFor(int? processId, SessionTarget target) =>
        (processId ?? target.ProcessId) is int pid ? [pid] : [];
}
