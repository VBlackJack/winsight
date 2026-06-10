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
using Winsight.Core.Modules.Cdp;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class ListCdpTargetsTool
{
    private const string HintNoPageTargets =
        "The endpoint answered but exposes no 'page' target. The WebView2 content may still be " +
        "navigating; retry after the app finishes loading.";

    [McpServerTool(Name = "list_cdp_targets")]
    [Description("List the debuggable targets of a Chrome DevTools Protocol endpoint (use the port " +
                 "reported by detect_webview2_endpoints). Returns one entry per target with its id, " +
                 "title, url and type. Pass a 'page' target id to evaluate_javascript or " +
                 "type_into_webview to drive the embedded web content.")]
    public static Task<string> ListCdpTargets(
        CdpTargetDiscovery discovery,
        [Description("CDP debugging port (RemoteDebuggingPort from detect_webview2_endpoints).")]
        int port)
    {
        return ToolSafety.SafeInvokeAsync(async () =>
        {
            IReadOnlyList<CdpTarget> targets = await discovery.ListAsync(port).ConfigureAwait(false);
            bool hasPage = targets.Any(t =>
                string.Equals(t.Type, "page", StringComparison.OrdinalIgnoreCase));
            var response = new
            {
                targets,
                hint = hasPage ? null : HintNoPageTargets,
            };
            return JsonSerializer.Serialize(response, JsonOptions.Default);
        });
    }
}
