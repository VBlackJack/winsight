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
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Diagnostics;
using Winsight.Core.Modules.Orchestration;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// The causal exception oracle: run_steps with captureExceptions attaches the
/// exceptions the target process threw <em>during</em> the batch. Clicking the
/// sample app's "Throw Exception" button must surface that exception; a benign
/// click must not surface it.
/// </summary>
[Trait("Category", "E2E")]
public class ExceptionOracleScenarioTests
{
    private const string ReadyLabelId = "CountLabel";
    private const string ThrowButtonId = "ThrowButton";
    private const string IncrementButtonId = "IncrementButton";
    private const string DeliberateMessage = "deliberate exception";

    [Fact]
    public void Captured_exceptions_include_the_one_the_clicked_button_threw()
    {
        using Process process = LaunchAndWait();
        try
        {
            string json = RunBatch(process.Id, ThrowButtonId);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            root.GetProperty("success").GetBoolean().Should().BeTrue();

            JsonElement exceptions = root.GetProperty("exceptions");
            ExceptionMessages(exceptions).Should().Contain(
                m => m.Contains(DeliberateMessage, StringComparison.Ordinal),
                "the clicked button throws an InvalidOperationException with that message");
        }
        finally
        {
            Kill(process);
        }
    }

    [Fact]
    public void A_benign_click_does_not_surface_the_deliberate_exception()
    {
        using Process process = LaunchAndWait();
        try
        {
            string json = RunBatch(process.Id, IncrementButtonId);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            root.GetProperty("success").GetBoolean().Should().BeTrue();

            // 'exceptions' is present (capture was requested) but must not carry
            // the deliberate one. Incidental framework exceptions are tolerated.
            root.TryGetProperty("exceptions", out JsonElement exceptions).Should().BeTrue();
            ExceptionMessages(exceptions).Should().NotContain(
                m => m.Contains(DeliberateMessage, StringComparison.Ordinal));
        }
        finally
        {
            Kill(process);
        }
    }

    private static string RunBatch(int processId, string buttonId)
    {
        var runner = new StepRunner(
            new ActionInvoker(), new ElementWaiter(), new ElementStateReader(), new WindowCapture());
        var diagnostics = new DiagnosticsObserver();

        return RunStepsTool.RunSteps(
            runner,
            diagnostics,
            new SessionTarget(),
            new[] { new BatchStep("click", AutomationId: buttonId) },
            processId,
            stopOnError: true,
            captureExceptions: true);
    }

    private static IEnumerable<string> ExceptionMessages(JsonElement exceptions)
    {
        foreach (JsonElement item in exceptions.EnumerateArray())
        {
            if (item.TryGetProperty("payload", out JsonElement payload)
                && payload.TryGetProperty("ExceptionMessage", out JsonElement message)
                && message.ValueKind == JsonValueKind.String)
            {
                yield return message.GetString() ?? string.Empty;
            }
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
