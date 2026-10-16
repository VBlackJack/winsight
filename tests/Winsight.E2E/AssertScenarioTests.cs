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
/// First-class assertions: an 'assert' run_steps step and the assert_element
/// tool produce a clear pass/fail with expected-vs-actual detail.
/// </summary>
[Trait("Category", "E2E")]
public class AssertScenarioTests
{
    private const string ReadyLabelId = "CountLabel";
    private const string IncrementButtonId = "IncrementButton";
    private const string CountLabelId = "CountLabel";
    private const string FeatureToggleId = "FeatureToggle";

    [Fact]
    public void Assert_step_and_assert_element_pass_and_fail_with_clear_detail()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        try
        {
            var stateReader = new ElementStateReader();
            OperationResult appeared = new ElementWaiter().Wait(
                process.Id, new ElementLocator(AutomationId: ReadyLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);

            var target = new SessionTarget();
            target.Set(process.Id);
            var runner = new StepRunner(
                new ActionInvoker(), new ElementWaiter(), stateReader, new WindowCapture());
            var diagnostics = new DiagnosticsObserver();

            // Click Increment, then assert the label shows the new count, all in one batch.
            string okJson = RunStepsTool.RunSteps(
                runner, diagnostics, target,
                new[]
                {
                    new BatchStep("click", AutomationId: IncrementButtonId),
                    new BatchStep("assert", Label: "count-is-1",
                        AutomationId: CountLabelId, Operator: "contains", ExpectedValue: "1"),
                });
            using (JsonDocument okDoc = JsonDocument.Parse(okJson))
            {
                okDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
                okDoc.RootElement.GetProperty("completed").GetInt32().Should().Be(2);
            }

            // A failing assert fails the step with expected-vs-actual detail.
            string failJson = RunStepsTool.RunSteps(
                runner, diagnostics, target,
                new[]
                {
                    new BatchStep("assert", Label: "wrong",
                        AutomationId: CountLabelId, Operator: "equals", ExpectedValue: "Count: 999"),
                });
            using (JsonDocument failDoc = JsonDocument.Parse(failJson))
            {
                failDoc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
                failDoc.RootElement.GetProperty("failedAtIndex").GetInt32().Should().Be(0);
                string error = failDoc.RootElement.GetProperty("steps")[0].GetProperty("error").GetString()!;
                error.Should().Contain("Assertion failed").And.Contain("Count: 999");
            }

            // The standalone assert_element tool: the checkbox starts unchecked.
            string uncheckedJson = AssertElementTool.AssertElement(
                stateReader, target, "unchecked", automationId: FeatureToggleId);
            using (JsonDocument uncheckedDoc = JsonDocument.Parse(uncheckedJson))
            {
                uncheckedDoc.RootElement.GetProperty("success").GetBoolean()
                    .Should().BeTrue("the feature checkbox is unchecked at startup");
            }

            string checkedJson = AssertElementTool.AssertElement(
                stateReader, target, "checked", automationId: FeatureToggleId);
            using (JsonDocument checkedDoc = JsonDocument.Parse(checkedJson))
            {
                checkedDoc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
                checkedDoc.RootElement.GetProperty("error").GetString().Should().Contain("checked");
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
