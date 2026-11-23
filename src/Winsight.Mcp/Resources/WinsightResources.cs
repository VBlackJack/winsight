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
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Serialization;
using Winsight.Mcp.Tools;

namespace Winsight.Mcp.Resources;

/// <summary>
/// Read-only MCP resources exposing live Winsight context without requiring
/// extra tool calls from the client.
/// </summary>
[McpServerResourceType]
public static class WinsightResources
{
    private const string JsonMimeType = "application/json";

    [McpServerResource(
        UriTemplate = "winsight://guide",
        Name = "winsight_guide",
        Title = "Winsight Agent Guide",
        MimeType = JsonMimeType)]
    [Description("Compact agent guide, server build metadata, registered tools, and error recovery policy.")]
    public static string GetGuide(McpServer? server = null)
        => AgentExperienceTool.GetWinsightGuide(server);

    [McpServerResource(
        UriTemplate = "winsight://state",
        Name = "winsight_state",
        Title = "Winsight Agent Session State",
        MimeType = JsonMimeType)]
    [Description("Current session target, guardrails, recent memory, last failure, and recommended next step.")]
    public static string GetState(SessionTarget target, SessionMemory memory)
        => AgentExperienceTool.GetAgentState(target, memory);

    [McpServerResource(
        UriTemplate = "winsight://windows",
        Name = "winsight_windows",
        Title = "Winsight Visible Windows",
        MimeType = JsonMimeType)]
    [Description("Classified visible windows for the current session target, or all desktop windows when no target is set " +
                 "(titles withheld in that desktop-wide case: titleWithheld=true; call list_windows with includeTitles=true to see them).")]
    public static string GetWindows(WindowDiscovery discovery, SessionTarget target)
    {
        int? targetProcessId = target.ProcessId;
        return ListWindowsTool.List(discovery, targetProcessId, targetProcessId, includeTitles: false);
    }

    [McpServerResource(
        UriTemplate = "winsight://scene/latest",
        Name = "winsight_latest_scene",
        Title = "Latest Active Scene Observation",
        MimeType = JsonMimeType)]
    [Description("Most recent non-expired observe_scene snapshot in the session, or available=false when none exists.")]
    public static string GetLatestScene(SceneStore store)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            SceneStore.Entry? latest = store.Latest();
            if (latest is null)
            {
                return JsonSerializer.Serialize(new { available = false }, JsonOptions.Default);
            }

            return JsonSerializer.Serialize(new { available = true, observation = latest }, JsonOptions.Default);
        });
    }
}
