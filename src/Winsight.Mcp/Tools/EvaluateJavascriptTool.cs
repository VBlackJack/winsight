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
public static class EvaluateJavascriptTool
{
    [McpServerTool(Name = "evaluate_javascript")]
    [Description("Evaluate a JavaScript expression inside a WebView2 page over the Chrome DevTools " +
                 "Protocol (promises are awaited, the result is returned by value as JSON). Use this " +
                 "to read DOM state, extract visible text (e.g. document.body.innerText), or perform " +
                 "ad-hoc actions UIA cannot reach. Get the port from detect_webview2_endpoints and " +
                 "the targetId from list_cdp_targets.")]
    public static Task<string> EvaluateJavascript(
        [Description("CDP debugging port.")]
        int port,
        [Description("Target id of the page (from list_cdp_targets).")]
        string targetId,
        [Description("JavaScript expression to evaluate in the page context.")]
        string expression,
        [Description("Optional per-command timeout in milliseconds. Defaults to " +
                     "WINSIGHT_CDP_TIMEOUT_MS or 10000.")]
        int? timeoutMs = null)
    {
        return ToolSafety.SafeInvokeAsync(async () =>
        {
            await using CdpClient client = await CdpClient
                .ConnectAsync(port, targetId, timeoutMs)
                .ConfigureAwait(false);
            CdpEvaluationResult evaluation = await client
                .EvaluateAsync(expression)
                .ConfigureAwait(false);
            return JsonSerializer.Serialize(evaluation, JsonOptions.Default);
        });
    }
}
