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
public static class ObserveCountersTool
{
    private const string DefaultProvider = "System.Runtime";

    [McpServerTool(Name = "observe_counters")]
    [Description("Subscribe to .NET EventCounters from a managed process via EventPipe (the official " +
                 "Microsoft diagnostic IPC, not raw ETW — works without admin and is invisible to EDR " +
                 "since it is the same channel dotnet-counters and dotnet-trace use). " +
                 "Useful as a non-intrusive oracle: read exception-count, cpu-usage, alloc-rate, gc-heap-size, " +
                 "threadpool-thread-count, and other counters before/after an action to assert behavior. " +
                 "Common providers: System.Runtime (default), Microsoft.AspNetCore.Hosting, System.Net.Http, " +
                 "or any custom EventSource declared in the target app. Target must be .NET Core 3.0+ / .NET 5+.")]
    public static string ObserveCounters(
        DiagnosticsObserver observer,
        [Description("Process id of the managed target.")]
        int processId,
        [Description("EventCounter provider name. Defaults to 'System.Runtime' which exposes runtime-level counters.")]
        string providerName = DefaultProvider,
        [Description("Observation window in milliseconds. Min 500, max 30000. Default 2000.")]
        int durationMs = 2000)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            var snapshot = observer.ObserveCounters(processId, providerName, durationMs);
            return JsonSerializer.Serialize(snapshot, JsonOptions.Default);
        });
    }
}
