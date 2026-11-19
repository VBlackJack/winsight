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
public static class WatchUiEventsTool
{
    [McpServerTool(Name = "watch_ui_events", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Subscribe to real-time Windows UI Automation events (window_opened, menu_opened, invoked, structure changes) on the target process for a bounded window. " +
                 "Catches transient popups, menus, and structural updates that polling can miss. Read-only.")]
    public static Task<string> WatchUiEvents(
        UiEventWatcher watcher,
        SessionTarget target,
        [Description("Duration to listen in milliseconds (100..30000, default 3000).")]
        int durationMs = UiEventWatcher.DefaultDurationMs,
        [Description("Maximum events to capture before stopping early (1..200, default 50).")]
        int maxEvents = UiEventWatcher.DefaultMaxEvents,
        [Description("Optional window title substring to scope subscriptions.")]
        string? windowTitle = null,
        [Description("Target process id. Optional; falls back to set_target.")]
        int? processId = null,
        CancellationToken cancellationToken = default)
        => ToolSafety.SafeInvokeAsync(async () =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(
                    AgentFailure.Repairable("missing_target", targetError, "target_resolution", "set_target"),
                    JsonOptions.Default);
            }

            OperationResult result = await watcher
                .WatchAsync(pid, durationMs, maxEvents, windowTitle, cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
}
