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
using Winsight.Core.Modules.Diagnostics;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class CaptureEventsTool
{
    [McpServerTool(Name = "capture_events")]
    [Description("Capture EventPipe events from one or more providers in a managed process. " +
                 "Useful for observing exceptions thrown, custom EventSource events, or any other " +
                 "diagnostic stream without injecting code. To list every exception thrown by an app " +
                 "during a scenario, pass providers=['Microsoft-Windows-DotNETRuntime'] and filter " +
                 "events by name 'ExceptionThrown_V1'. To capture a custom EventSource declared in " +
                 "the app, pass that provider's name. Returns a bounded list of events with normalized " +
                 "JSON-safe payloads. Same EDR-safe constraints as observe_counters.")]
    public static string CaptureEvents(
        DiagnosticsObserver observer,
        [Description("Process id of the managed target.")]
        int processId,
        [Description("EventSource provider names to subscribe to. " +
                     "Examples: 'Microsoft-Windows-DotNETRuntime', 'System.Threading.Tasks.TplEventSource', " +
                     "or any custom EventSource declared in the target app.")]
        string[] providers,
        [Description("Capture window in milliseconds. Min 500, max 30000. Default 3000.")]
        int durationMs = 3000,
        [Description("Maximum events to keep. Min 1, max 10000. Default 500. " +
                     "When the cap is reached, additional events are dropped and 'truncated' is set.")]
        int maxEvents = 500,
        [Description("Optional exact-match event name filter (case-sensitive). " +
                     "Use it to narrow to a specific event such as 'ExceptionThrown_V1'.")]
        string? eventNameFilter = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            var result = observer.CaptureEvents(processId, providers, durationMs, maxEvents, eventNameFilter);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }
}
