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

using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Winsight.Mcp;

/// <summary>
/// Configures MCP server-level instructions so AI clients know the preferred
/// autonomous control loop on initialize, and applies optional tool surface
/// filtering via <see cref="ToolProfileVariable"/>.
/// </summary>
internal sealed class McpServerConfiguration : IConfigureOptions<McpServerOptions>
{
    public const string ToolProfileVariable = "WINSIGHT_TOOL_PROFILE";
    public const string CompactProfileName = "compact";

    internal const string Instructions =
        "Winsight drives and inspects Windows desktop applications. Follow this autonomous loop:\n" +
        "1. Target: call launch_app (auto-sets session target) or list_windows + set_target.\n" +
        "2. Perceive: call observe_scene for expiring eN target references, window/modal state, and supported actions. " +
        "Call capture_scene(observation) when visual layout helps (returns an inline Set-of-Mark annotated PNG).\n" +
        "3. Act & verify: prefer act_verified with an explicit expect postcondition (strict predicates: exists, absent, value, name, selected, toggle, focused, visible, selected_item, window_open, window_closed, managed; exploratory predicates: value_contains, name_contains, any_change, visual_change). " +
        "Set observeAfter=true to receive a fresh observation and structural diff in the same call.\n" +
        "4. Batch & custom surfaces: use run_steps for multi-step scripts (captureExceptions=true for causal CLR exceptions). " +
        "For GPU/custom-drawn UIs without UIA, use click_point, type_keys, read_text_visually, and wait_for_visual_change/wait_for_visual_stable. " +
        "For authorized WebView2, use detect_webview2_endpoints + list_cdp_targets + observe_dom_scene.\n" +
        "5. Recovery: on stale, use the returned recovery observation or call observe_scene. " +
        "NEVER blindly retry dispatched_unconfirmed: observe first. On user_active or foreground_changed, call wait_for_user_idle then observe again.\n" +
        "6. Reuse: check list_app_profiles / run_app_profile for version-pinned workflows. Live context is also available via winsight://guide, winsight://state, winsight://windows, and winsight://scene/latest resources.";

    internal static readonly HashSet<string> CompactToolNames = new(StringComparer.Ordinal)
    {
        "launch_app",
        "close_app",
        "list_windows",
        "set_target",
        "get_target",
        "observe_scene",
        "capture_scene",
        "act_verified",
        "run_steps",
        "click_point",
        "type_keys",
        "send_key_chord",
        "read_text_visually",
        "wait_for_visual_change",
        "wait_for_visual_stable",
        "observe_dom_scene",
        "locate_scene_target",
        "read_managed_state",
        "get_user_activity",
        "wait_for_user_idle",
        "get_agent_state",
        "get_winsight_guide",
        "list_app_profiles",
        "save_app_profile",
        "run_app_profile",
        "delete_app_profile",
    };

    public void Configure(McpServerOptions options)
    {
        options.ServerInstructions = Instructions;
        ApplyToolProfile(options, Environment.GetEnvironmentVariable(ToolProfileVariable));
    }

    internal static void ApplyToolProfile(McpServerOptions options, string? profile)
    {
        if (!string.Equals(profile?.Trim(), CompactProfileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (options.ToolCollection is null)
        {
            return;
        }

        List<McpServerTool> toRemove = options.ToolCollection
            .Where(tool => !CompactToolNames.Contains(tool.ProtocolTool.Name))
            .ToList();
        foreach (McpServerTool tool in toRemove)
        {
            options.ToolCollection.Remove(tool);
        }
    }
}
