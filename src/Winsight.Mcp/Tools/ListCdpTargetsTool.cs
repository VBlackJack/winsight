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
using Winsight.Core.Modules.WebView2;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class ListCdpTargetsTool
{
    private const string HintNoPageTargets =
        "The endpoint answered but exposes no 'page' target. The WebView2 content may still be " +
        "navigating; retry after the app finishes loading.";

    [McpServerTool(Name = "list_cdp_targets")]
    [Description("List the debuggable targets of an authorized Chrome DevTools Protocol endpoint. " +
                 "The port must be reported by detect_webview2_endpoints for the session target or " +
                 "explicit hostProcessId. Returns one entry per target with its id, title, url and " +
                 "type. Pass a 'page' target id to evaluate_javascript or type_into_webview to drive " +
                 "the embedded web content.")]
    public static Task<string> ListCdpTargets(
        CdpTargetDiscovery discovery,
        WebView2Detector detector,
        SessionTarget target,
        [Description("CDP debugging port (RemoteDebuggingPort from detect_webview2_endpoints).")]
        int port,
        [Description("Host process id that owns the WebView2 endpoint. Optional; falls back to the session default set by set_target.")]
        int? hostProcessId = null)
    {
        return ToolSafety.SafeInvokeAsync(async () =>
        {
            if (!target.TryResolve(hostProcessId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            if (CdpEndpointPolicy.DenyUnauthorizedPort(detector, pid, port) is { } denied)
            {
                return JsonSerializer.Serialize(denied, JsonOptions.Default);
            }
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
