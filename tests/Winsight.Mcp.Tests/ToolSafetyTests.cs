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

using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

/// <summary>
/// ToolSafety is the outer guard at the MCP tool boundary. If it stops working,
/// any unhandled exception inside any tool will kill the .NET host and close
/// the stdio transport, leaving every other tool dead for the rest of the
/// agent session — exactly the regression that triggered its introduction.
/// These tests pin down its invariants.
/// </summary>
public class ToolSafetyTests
{
    [Fact]
    public void Returns_body_output_unchanged_on_success()
    {
        const string expected = "{\"hello\":\"world\"}";

        var actual = ToolSafety.SafeInvoke(() => expected);

        actual.Should().Be(expected);
    }

    [Fact]
    public void Wraps_thrown_exception_into_failure_operation_result_json()
    {
        var json = ToolSafety.SafeInvoke(() =>
            throw new InvalidOperationException("could not attach to process 1234"));

        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Be("could not attach to process 1234");
    }

    [Fact]
    public void Catches_com_exception_so_uia_failures_do_not_kill_the_host()
    {
        // COMException is the canonical UIA failure type — Codex hit one in the
        // wild while driving Win11Forge, which crashed the whole MCP host.
        // CA2201 normally forbids throwing reserved exceptions, but here we are
        // simulating a runtime-originated COMException to validate the guard.
#pragma warning disable CA2201
        var json = ToolSafety.SafeInvoke(() =>
            throw new COMException("E_ELEMENTNOTAVAILABLE"));
#pragma warning restore CA2201

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetString()
            .Should().Contain("E_ELEMENTNOTAVAILABLE");
    }

    // Note: there is no test for OutOfMemoryException / StackOverflowException
    // pass-through. CA2201 forbids throwing them directly in user code, and
    // synthesizing a real one in a test is unreliable. The IsFatal allowlist in
    // ToolSafety is small enough to review by inspection.

    [Fact]
    public void SafeInvokeContent_returns_body_result_unchanged_on_success()
    {
        CallToolResult expected = new()
        {
            Content = { new TextContentBlock { Text = "{\"ok\":true}" } },
        };

        CallToolResult actual = ToolSafety.SafeInvokeContent(() => expected);

        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public void SafeInvokeContent_wraps_exception_into_error_result_with_failure_json()
    {
        CallToolResult result = ToolSafety.SafeInvokeContent(() =>
            throw new InvalidOperationException("capture failed for process 4"));

        result.IsError.Should().BeTrue();
        string json = result.Content.OfType<TextContentBlock>().First().Text;
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetString()
            .Should().Be("capture failed for process 4");
    }

    [Fact]
    public async Task Async_overload_returns_body_output_on_success()
    {
        const string expected = "{\"async\":true}";

        var actual = await ToolSafety.SafeInvokeAsync(() => Task.FromResult(expected));

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task Async_overload_wraps_thrown_exception()
    {
        var json = await ToolSafety.SafeInvokeAsync(() =>
            Task.FromException<string>(new TimeoutException("vision API call timed out")));

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetString()
            .Should().Be("vision API call timed out");
    }

    [Fact]
    public void SafeInvoke_writes_trace_event_when_trace_directory_is_set()
    {
        string traceDir = Path.Combine(Path.GetTempPath(), "winsight-tests", Guid.NewGuid().ToString("N"));
        string? before = Environment.GetEnvironmentVariable(TraceRecorder.TraceDirectoryVariable);
        Environment.SetEnvironmentVariable(TraceRecorder.TraceDirectoryVariable, traceDir);
        try
        {
            ToolSafety.SafeInvoke(() => "{\"success\":true}");

            Directory.GetFiles(traceDir, "*.jsonl").Should().ContainSingle();
            File.ReadAllText(Directory.GetFiles(traceDir, "*.jsonl").Single())
                .Should().Contain("\"success\": true");
        }
        finally
        {
            Environment.SetEnvironmentVariable(TraceRecorder.TraceDirectoryVariable, before);
            if (Directory.Exists(traceDir))
            {
                Directory.Delete(traceDir, recursive: true);
            }
        }
    }
}
