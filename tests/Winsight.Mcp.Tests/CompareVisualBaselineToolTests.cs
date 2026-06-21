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
using Winsight.Core.Modules.VisualRegression;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

[SupportedOSPlatform("windows")]
public class CompareVisualBaselineToolTests
{
    private const int NoWindowProcessId = 4;

    [Fact]
    public void Resolve_baseline_path_uses_configured_directory_and_adds_png_extension()
    {
        string? original = Environment.GetEnvironmentVariable(CompareVisualBaselineTool.BaselineDirectoryVariable);
        try
        {
            string baselineDirectory = Path.Combine(Path.GetTempPath(), "winsight-baselines-test");
            Environment.SetEnvironmentVariable(
                CompareVisualBaselineTool.BaselineDirectoryVariable,
                baselineDirectory);

            string path = CompareVisualBaselineTool.ResolveBaselinePath("SampleWpfApp/counter-initial");

            path.Should().Be(Path.Combine(baselineDirectory, "SampleWpfApp", "counter-initial.png"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(CompareVisualBaselineTool.BaselineDirectoryVariable, original);
        }
    }

    [Theory]
    [InlineData("..\\escape.png")]
    [InlineData("../escape.png")]
    [InlineData("C:\\escape.png")]
    [InlineData("baseline.jpg")]
    public void Resolve_baseline_path_rejects_unsafe_or_non_png_keys(string baselineKey)
    {
        string? original = Environment.GetEnvironmentVariable(CompareVisualBaselineTool.BaselineDirectoryVariable);
        try
        {
            string baselineDirectory = Path.Combine(Path.GetTempPath(), "winsight-baselines-test");
            Environment.SetEnvironmentVariable(
                CompareVisualBaselineTool.BaselineDirectoryVariable,
                baselineDirectory);

            Action act = () => CompareVisualBaselineTool.ResolveBaselinePath(baselineKey);

            act.Should().Throw<ArgumentException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable(CompareVisualBaselineTool.BaselineDirectoryVariable, original);
        }
    }

    [Fact]
    public void Failure_message_does_not_throw_through_to_mcp_framework()
    {
        var capture = new WindowCapture();
        var differ = new VisualDiffer();

        Func<CallToolResult> act = () => CompareVisualBaselineTool.CompareVisualBaseline(
            capture,
            differ,
            new SessionTarget(),
            "missing-window.png",
            processId: NoWindowProcessId);

        act.Should().NotThrow();
    }

    [Fact]
    public void Returns_failure_operation_result_when_window_capture_fails()
    {
        var capture = new WindowCapture();
        var differ = new VisualDiffer();

        CallToolResult result = CompareVisualBaselineTool.CompareVisualBaseline(
            capture,
            differ,
            new SessionTarget(),
            "missing-window.png",
            processId: NoWindowProcessId);

        result.IsError.Should().BeTrue();
        string json = result.Content.OfType<TextContentBlock>().First().Text;
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString()
            .Should().Contain($"process {NoWindowProcessId}");
    }
}
