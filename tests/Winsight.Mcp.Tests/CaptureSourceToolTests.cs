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
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public class CaptureSourceToolTests
{
    [Fact]
    public void CaptureScreenshot_rejects_invalid_captureSource()
    {
        CallToolResult result = CaptureScreenshotTool.CaptureScreenshot(
            new WindowCapture(),
            new SessionTarget(),
            captureSource: "invalid");

        result.IsError.Should().BeTrue();
        string text = ((TextContentBlock)result.Content[0]).Text;
        text.Should().Contain("Invalid captureSource");
    }

    [Fact]
    public void CaptureScene_rejects_invalid_captureSource()
    {
        SceneStore store = new();
        CallToolResult result = SceneScreenshotTool.Capture(
            store,
            observation: "obs-1",
            captureSource: "invalid");

        result.IsError.Should().BeTrue();
        string text = ((TextContentBlock)result.Content[0]).Text;
        text.Should().Contain("Invalid captureSource");
    }

    [Fact]
    public void Observe_rejects_invalid_captureSource()
    {
        string json = AgentExperienceTool.Observe(
            new WindowDiscovery(),
            new AffordanceScanner(),
            new WindowCapture(),
            new SessionTarget(),
            new SessionMemory(),
            includeScreenshot: true,
            captureSource: "invalid");

        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetString().Should().Contain("Invalid captureSource");
    }
}
