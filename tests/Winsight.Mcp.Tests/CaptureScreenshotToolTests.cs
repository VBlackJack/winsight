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

using System.Runtime.Versioning;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Winsight.Core.Modules.Capture;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

[SupportedOSPlatform("windows")]
public class CaptureScreenshotToolTests
{
    // Sentinel id chosen so no top-level window can match: the System Idle Process
    // (PID 0) and the System process (PID 4) never own user-visible windows, but
    // they exist on every Windows host and exercise the "no window" branch
    // deterministically.
    private const int NoWindowProcessId = 4;

    [Fact]
    public void Returns_failure_operation_result_when_no_window_found()
    {
        var capture = new WindowCapture();

        CallToolResult result = CaptureScreenshotTool.CaptureScreenshot(capture, new SessionTarget(), processId: NoWindowProcessId);

        result.IsError.Should().BeTrue();
        using var document = JsonDocument.Parse(TextOf(result));
        JsonElement root = document.RootElement;

        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString()
            .Should().Contain($"process {NoWindowProcessId}");
        // JsonOptions.Default ignores null members, so `data` is absent on
        // failure — that is expected and matches every other failing tool's shape.
        root.TryGetProperty("data", out _).Should().BeFalse();
    }

    [Fact]
    public void Failure_does_not_throw_through_to_mcp_framework()
    {
        // Regression: previously the tool let exceptions propagate, producing a
        // generic "An error occurred invoking 'capture_screenshot'." message that
        // hid the underlying cause from the agent. The wrapping must convert it
        // into an exploitable error result.
        var capture = new WindowCapture();

        Func<CallToolResult> act = () => CaptureScreenshotTool.CaptureScreenshot(capture, new SessionTarget(), processId: NoWindowProcessId);

        act.Should().NotThrow();
    }

    [Fact]
    public void Failure_carries_no_image_block()
    {
        var capture = new WindowCapture();

        CallToolResult result = CaptureScreenshotTool.CaptureScreenshot(capture, new SessionTarget(), processId: NoWindowProcessId);

        result.Content.Should().ContainSingle("a failure returns only the error text, no image");
        result.Content[0].Should().BeOfType<TextContentBlock>();
    }

    [Fact]
    public void Window_title_scope_is_safe_when_no_window_found()
    {
        var capture = new WindowCapture();

        Func<CallToolResult> act = () => CaptureScreenshotTool.CaptureScreenshot(
            capture,
            new SessionTarget(),
            windowTitle: "Missing dialog",
            processId: NoWindowProcessId);

        act.Should().NotThrow();
    }

    private static string TextOf(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().First().Text;
}
