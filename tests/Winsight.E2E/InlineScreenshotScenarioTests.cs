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

using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// capture_screenshot returns the captured PNG as an inline MCP image (plus the
/// JSON path), so a supervising agent sees the UI directly instead of reading a
/// file. Verified end to end against a live window.
/// </summary>
[Trait("Category", "E2E")]
public class InlineScreenshotScenarioTests
{
    private const string ReadyLabelId = "CountLabel";
    private const string PngMediaType = "image/png";

    [Fact]
    public void Capture_returns_the_png_as_an_inline_image_plus_the_json_path()
    {
        using Process process = LaunchAndWait();
        try
        {
            CallToolResult result = CaptureScreenshotTool.CaptureScreenshot(
                new WindowCapture(), process.Id, includeImage: true);

            result.IsError.Should().NotBe(true);

            // Text block carries the same OperationResult JSON as before.
            string json = result.Content.OfType<TextContentBlock>().First().Text;
            using JsonDocument document = JsonDocument.Parse(json);
            document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            document.RootElement.GetProperty("data").GetProperty("width").GetInt32()
                .Should().BeGreaterThan(0);

            // Image block carries the PNG bytes inline.
            ImageContentBlock image = result.Content.OfType<ImageContentBlock>().Single();
            image.MimeType.Should().Be(PngMediaType);
            image.DecodedData.Length.Should().BeGreaterThan(0, "the captured PNG bytes are attached");
        }
        finally
        {
            Kill(process);
        }
    }

    [Fact]
    public void Include_image_false_returns_only_the_json_text()
    {
        using Process process = LaunchAndWait();
        try
        {
            CallToolResult result = CaptureScreenshotTool.CaptureScreenshot(
                new WindowCapture(), process.Id, includeImage: false);

            result.Content.Should().ContainSingle("only the JSON text is returned when the image is opted out");
            result.Content[0].Should().BeOfType<TextContentBlock>();
        }
        finally
        {
            Kill(process);
        }
    }

    private static Process LaunchAndWait()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        OperationResult appeared = new ElementWaiter().Wait(
            process.Id, new ElementLocator(AutomationId: ReadyLabelId), WaitCondition.Exists);
        appeared.Success.Should().BeTrue(appeared.Error);
        return process;
    }

    private static void Kill(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
