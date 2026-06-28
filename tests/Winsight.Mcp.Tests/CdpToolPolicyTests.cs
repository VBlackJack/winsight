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

using System.Text.Json;
using System.Runtime.Versioning;
using FluentAssertions;
using Winsight.Core.Modules.Cdp;
using Winsight.Core.Modules.WebView2;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

[SupportedOSPlatform("windows")]
public sealed class CdpToolPolicyTests
{
    [Fact]
    public async Task List_cdp_targets_requires_a_session_or_explicit_host_process()
    {
        string json = await ListCdpTargetsTool.ListCdpTargets(
            new CdpTargetDiscovery(new HttpClient()),
            new WebView2Detector(),
            new SessionTarget(),
            port: 9222);

        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetString().Should().Contain("set_target");
    }

    [Fact]
    public async Task Evaluate_javascript_rejects_ports_not_owned_by_the_authorized_process()
    {
        string json = await EvaluateJavascriptTool.EvaluateJavascript(
            new WebView2Detector(),
            new SessionTarget(),
            port: 9222,
            targetId: "page",
            expression: "document.title",
            hostProcessId: 4);

        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetString().Should().Contain("not exposed");
    }

    [Fact]
    public async Task Type_into_webview_rejects_ports_not_owned_by_the_authorized_process()
    {
        string json = await TypeIntoWebViewTool.TypeIntoWebView(
            new WebView2Detector(),
            new SessionTarget(),
            port: 9222,
            targetId: "page",
            text: "hello",
            hostProcessId: 4);

        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetString().Should().Contain("not exposed");
    }
}
