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
public static class TypeIntoWebViewTool
{
    [McpServerTool(Name = "type_into_webview")]
    [Description("Type text into an authorized WebView2 page as real key events over the Chrome " +
                 "DevTools Protocol (Input.dispatchKeyEvent). Unlike UIA type_text, this reaches " +
                 "keydown-driven surfaces such as xterm.js terminals embedded in HwndHost. " +
                 "Newlines ('\\n') are sent as Enter presses, '\\t' as Tab. The port must come from " +
                 "detect_webview2_endpoints for the session target or explicit hostProcessId; get the " +
                 "targetId from list_cdp_targets.")]
    public static Task<string> TypeIntoWebView(
        WebView2Detector detector,
        SessionTarget target,
        [Description("CDP debugging port.")]
        int port,
        [Description("Target id of the page (from list_cdp_targets).")]
        string targetId,
        [Description("Text to type. '\\n' becomes Enter, '\\t' becomes Tab.")]
        string text,
        [Description("Optional CSS selector to focus before typing (e.g. '.xterm-helper-textarea'). " +
                     "Fails explicitly when no element matches.")]
        string? focusSelector = null,
        [Description("Optional per-command timeout in milliseconds. Defaults to " +
                     "WINSIGHT_CDP_TIMEOUT_MS or 10000.")]
        int? timeoutMs = null,
        [Description("Host process id that owns the WebView2 endpoint. Optional — falls back to the session default set by set_target.")]
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
            if (text is null)
            {
                return JsonSerializer.Serialize(OperationResult.Failure("text is required."), JsonOptions.Default);
            }
            if (ToolAccessPolicy.DryRunMutation(
                    "type_into_webview",
                    new { hostProcessId = pid, port, targetId, textLength = text.Length, focusSelector })
                is { } dryRun)
            {
                return JsonSerializer.Serialize(dryRun, JsonOptions.Default);
            }

            await using CdpClient client = await CdpClient
                .ConnectAsync(port, targetId, timeoutMs)
                .ConfigureAwait(false);
            int dispatchedEvents = await client
                .TypeTextAsync(text, focusSelector)
                .ConfigureAwait(false);
            return JsonSerializer.Serialize(
                OperationResult.OkWith(new { dispatchedEvents }),
                JsonOptions.Default);
        });
    }
}
