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
using Winsight.Core.Models;
using Winsight.Mcp.Serialization;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class CompactJsonTests : IDisposable
{
    private const int RecordCount = 3;
    private static readonly string[] SampleItems = ["a", "b"];
    private readonly string? _previousDirectory = Environment.GetEnvironmentVariable(TraceRecorder.TraceDirectoryVariable);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "winsight-compact-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(TraceRecorder.TraceDirectoryVariable, _previousDirectory);
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Operation_result_is_serialized_without_newlines_or_spaced_separators()
    {
        string json = JsonSerializer.Serialize(
            OperationResult.OkWith(new { items = SampleItems, nested = new { value = 1 } }), JsonOptions.Default);

        json.Should().NotContain("\n");
        json.Should().NotContain("\r");
        json.Should().NotContain("\": ");
        json.Should().Contain("\"success\":true");
    }

    [Fact]
    public void Indented_options_keep_the_same_content_but_span_lines()
    {
        object value = OperationResult.OkWith(new { value = 1 });

        string compact = JsonSerializer.Serialize(value, JsonOptions.Default);
        string indented = JsonSerializer.Serialize(value, JsonOptions.Indented);

        indented.Should().Contain("\n");
        JsonDocument.Parse(indented).RootElement.GetRawText().Should().NotBeEmpty();
        JsonSerializer.Serialize(JsonDocument.Parse(indented).RootElement).Should().Be(compact);
    }

    [Fact]
    public void Trace_file_holds_one_json_record_per_line()
    {
        Environment.SetEnvironmentVariable(TraceRecorder.TraceDirectoryVariable, _directory);
        string payload = JsonSerializer.Serialize(OperationResult.Ok(), JsonOptions.Default);

        for (int i = 0; i < RecordCount; i++)
        {
            TraceRecorder.RecordToolResult("tool_" + i, TimeSpan.FromMilliseconds(1), payload);
        }

        string file = Directory.GetFiles(_directory, "*.jsonl").Should().ContainSingle().Subject;
        string[] lines = File.ReadAllLines(file);
        lines.Should().HaveCount(RecordCount);
        foreach (string line in lines)
        {
            using JsonDocument record = JsonDocument.Parse(line);
            record.RootElement.GetProperty("toolName").GetString().Should().StartWith("tool_");
        }
    }
}
