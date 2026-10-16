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
public static class EvaluateJavascriptTool
{
    [McpServerTool(Name = "evaluate_javascript")]
    [Description("Evaluate a JavaScript expression inside an authorized WebView2 page over the Chrome " +
                 "DevTools Protocol (promises are awaited, the result is returned by value as JSON). " +
                 "Use this to read DOM state, extract visible text (e.g. document.body.innerText), or " +
                 "perform ad-hoc actions UIA cannot reach. The port must come from " +
                 "detect_webview2_endpoints for the session target or explicit hostProcessId; get the " +
                 "targetId from list_cdp_targets.")]
    public static Task<string> EvaluateJavascript(
        WebView2Detector detector,
        SessionTarget target,
        [Description("CDP debugging port.")]
        int port,
        [Description("Target id of the page (from list_cdp_targets).")]
        string targetId,
        [Description("JavaScript expression to evaluate in the page context.")]
        string expression,
        [Description("Optional per-command timeout in milliseconds. Defaults to " +
                     "WINSIGHT_CDP_TIMEOUT_MS or 10000.")]
        int? timeoutMs = null,
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
            if (string.IsNullOrWhiteSpace(targetId))
            {
                return JsonSerializer.Serialize(OperationResult.Failure("targetId is required."), JsonOptions.Default);
            }
            if (expression is null)
            {
                return JsonSerializer.Serialize(OperationResult.Failure("expression is required."), JsonOptions.Default);
            }
            if (ToolAccessPolicy.GuardMutation(
                    "evaluate_javascript",
                    new { hostProcessId = pid, port, targetId, expressionLength = expression.Length })
                is { } gated)
            {
                return JsonSerializer.Serialize(gated, JsonOptions.Default);
            }

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
