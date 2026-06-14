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
using Winsight.Core.Modules.WebView2;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class DetectWebView2EndpointsTool
{
    private const string HintNoWebView2 =
        "No msedgewebview2.exe descendant of the host process. The app may not embed WebView2, " +
        "or it has not finished initializing yet.";

    private const string HintNoCdpFlag =
        "WebView2 processes found, but none expose --remote-debugging-port. To enable CDP attach, " +
        "the host app must set the environment variable WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=" +
        "--remote-debugging-port=9222 (or use CoreWebView2EnvironmentOptions.AdditionalBrowserArguments) " +
        "before calling EnsureCoreWebView2Async.";

    private const string HintPortNotListening =
        "WebView2 was started with --remote-debugging-port, but the port is not currently listening. " +
        "The renderer may have crashed or not finished startup.";

    [McpServerTool(Name = "detect_webview2_endpoints")]
    [Description("Walk the process tree from the given host pid and report every msedgewebview2.exe " +
                 "descendant, indicating whether it was launched with --remote-debugging-port (Chrome " +
                 "DevTools Protocol). Use the returned port to attach Playwright/Puppeteer over CDP " +
                 "(http://localhost:PORT) and drive the embedded web content directly. The 'hint' field " +
                 "explains how to fix the situation when no usable endpoint is found.")]
    public static string DetectWebView2Endpoints(
        WebView2Detector detector,
        [Description("Host process id (from list_windows). The detector walks the entire descendant tree.")]
        int hostProcessId)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            IReadOnlyList<WebView2Endpoint> endpoints = detector.Detect(hostProcessId);
            var hint = ResolveHint(endpoints);
            var response = new
            {
                endpoints,
                hint,
            };
            return JsonSerializer.Serialize(response, JsonOptions.Default);
        });
    }

    private static string? ResolveHint(IReadOnlyList<WebView2Endpoint> endpoints)
    {
        if (endpoints.Count == 0)
        {
            return HintNoWebView2;
        }
        if (!endpoints.Any(e => e.RemoteDebuggingPort.HasValue))
        {
            return HintNoCdpFlag;
        }
        if (!endpoints.Any(e => e.IsPortListening))
        {
            return HintPortNotListening;
        }
        return null;
    }
}
