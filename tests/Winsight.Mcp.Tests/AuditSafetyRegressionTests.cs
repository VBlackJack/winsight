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
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Diagnostics;
using Winsight.Core.Modules.Orchestration;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Core.Modules.VisualRegression;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class AuditSafetyRegressionTests
{
    [Fact]
    public void Conflicting_macro_process_ids_are_rejected_before_observation_or_dispatch()
    {
        using EnvironmentFixture fixture = new();
        fixture.Set(ToolAccessPolicy.AllowedProcessIdsVariable, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AffordanceScanner scanner = new();
        WindowCapture capture = new();
        string json = AgentExperienceTool.ActAndObserve(
            Runner(), new UiSnapshotter(new WindowDiscovery(), scanner), scanner, capture,
            new SessionTarget(), new SessionMemory(),
            new BatchStep("click", ProcessId: int.MaxValue, AutomationId: "excluded"),
            processId: Environment.ProcessId);

        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetString().Should().Contain("same target");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Baseline_dry_run_preserves_bytes_and_does_not_capture_or_create_files(bool exists)
    {
        using EnvironmentFixture fixture = new();
        fixture.Set(ToolAccessPolicy.DryRunVariable, "true");
        fixture.Set(CompareVisualBaselineTool.BaselineDirectoryVariable, fixture.Directory);
        fixture.Set(ScreenshotOutput.OutputDirectoryVariable, fixture.Directory);
        string path = Path.Combine(fixture.Directory, "reference.png");
        byte[] sentinel = { 1, 2, 3, 4 };
        if (exists) File.WriteAllBytes(path, sentinel);

        CallToolResult result = CompareVisualBaselineTool.CompareVisualBaseline(
            new WindowCapture(), new VisualDiffer(), new SessionTarget(), "reference",
            updateBaseline: exists, processId: Environment.ProcessId);

        using JsonDocument document = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text);
        document.RootElement.GetProperty("data").GetProperty("dryRun").GetBoolean().Should().BeTrue();
        if (exists) File.ReadAllBytes(path).Should().Equal(sentinel);
        else File.Exists(path).Should().BeFalse();
        System.IO.Directory.GetFiles(fixture.Directory).Should().HaveCount(exists ? 1 : 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Trace_io_failure_preserves_all_wrapper_outcomes(bool failBody, bool failAppend)
    {
        using EnvironmentFixture fixture = new();
        string path = Path.Combine(fixture.Directory, "trace");
        if (failAppend)
        {
            System.IO.Directory.CreateDirectory(path);
            System.IO.Directory.CreateDirectory(Path.Combine(path, $"winsight-{DateTime.UtcNow:yyyyMMdd}.jsonl"));
        }
        else File.WriteAllText(path, "a file cannot be used as a trace directory");
        fixture.Set(TraceRecorder.TraceDirectoryVariable, path);
        int calls = 0;
        string Body()
        {
            calls++;
            if (failBody) throw new InvalidOperationException("original failure");
            return "{\"success\":true}";
        }

        string sync = ToolSafety.SafeInvoke(Body);
        string asyncResult = await ToolSafety.SafeInvokeAsync(() => Task.FromResult(Body()));
        CallToolResult content = ToolSafety.SafeInvokeContent(() => new CallToolResult
        {
            Content = { new TextContentBlock { Text = Body() } },
        });
        calls.Should().Be(3, "each body must execute exactly once despite trace failures");
        foreach (string json in new[] { sync, asyncResult, ((TextContentBlock)content.Content[0]).Text })
        {
            using JsonDocument document = JsonDocument.Parse(json);
            document.RootElement.GetProperty("success").GetBoolean().Should().Be(!failBody);
            if (failBody) document.RootElement.GetProperty("error").GetString().Should().Be("original failure");
        }
    }

    [Fact]
    public void Failed_replay_write_does_not_replace_the_batch_result()
    {
        using EnvironmentFixture fixture = new();
        string file = Path.Combine(fixture.Directory, "blocked");
        File.WriteAllText(file, "not a directory");
        fixture.Set(TraceRecorder.TraceDirectoryVariable, file);
        string json = RunStepsTool.RunSteps(Runner(), new DiagnosticsObserver(), new SessionTarget(),
            new[] { new BatchStep("click", AutomationId: "test") }, processId: Environment.ProcessId,
            dryRun: true, traceRun: true);
        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("completed").GetInt32().Should().Be(1);
        document.RootElement.TryGetProperty("tracePath", out _).Should().BeFalse();
    }

    [Fact]
    public void Batch_and_concurrent_capture_paths_are_unique()
    {
        using EnvironmentFixture fixture = new();
        fixture.Set(ScreenshotOutput.OutputDirectoryVariable, fixture.Directory);
        string[] concurrent = new string[500];
        Parallel.For(0, concurrent.Length, i => concurrent[i] = ScreenshotOutput.BuildTimestampedPath("capture-", false));
        concurrent.Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(concurrent.Length);
        string json = RunStepsTool.RunSteps(Runner(), new DiagnosticsObserver(), new SessionTarget(),
            Enumerable.Range(0, 100).Select(_ => new BatchStep("screenshot")).ToArray(),
            processId: Environment.ProcessId, dryRun: true);
        using JsonDocument document = JsonDocument.Parse(json);
        string?[] paths = document.RootElement.GetProperty("steps").EnumerateArray()
            .Select(step => step.GetProperty("data").GetProperty("outputPath").GetString()).ToArray();
        paths.Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(100);
    }

    private static StepRunner Runner() => new(new ActionInvoker(), new ElementWaiter(), new ElementStateReader(), new WindowCapture());

    private sealed class EnvironmentFixture : IDisposable
    {
        private readonly Dictionary<string, string?> _original = new(StringComparer.Ordinal);
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "winsight-tests", Guid.NewGuid().ToString("N"));
        public EnvironmentFixture() => System.IO.Directory.CreateDirectory(Directory);
        public void Set(string key, string value)
        {
            _original.TryAdd(key, Environment.GetEnvironmentVariable(key));
            Environment.SetEnvironmentVariable(key, value);
        }
        public void Dispose()
        {
            foreach (KeyValuePair<string, string?> item in _original) Environment.SetEnvironmentVariable(item.Key, item.Value);
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
