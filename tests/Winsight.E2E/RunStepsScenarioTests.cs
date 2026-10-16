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
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.ManagedState;
using Winsight.Core.Modules.Orchestration;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// The README's canonical example — "click Increment five times, then confirm
/// the count" — as one run_steps call instead of six round trips: five clicks
/// plus an inline assertion via wait value_equals, with a single journal back.
/// </summary>
[Trait("Category", "E2E")]
public class RunStepsScenarioTests
{
    private const string CounterTypeName = "Winsight.Samples.SimpleCounter.ViewModels.CounterViewModel";
    private const string CounterFieldName = "_internalCount";
    private const string IncrementButtonId = "IncrementButton";
    private const string CountLabelId = "CountLabel";
    private const string OpenDialogButtonId = "OpenDialogButton";
    private const string NoteInputId = "NoteInput";
    private const string ConfirmNoteButtonId = "ConfirmNoteButton";
    private const string LastNoteLabelId = "LastNoteLabel";
    private const string DialogTitleFragment = "Note";
    private const string NoteText = "dialog inside a guarded batch";

    [Fact]
    public void One_batch_clicks_five_times_and_asserts_the_count()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        try
        {
            var waiter = new ElementWaiter();
            OperationResult appeared = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: CountLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);

            var runner = new StepRunner(
                new ActionInvoker(), waiter, new ElementStateReader(), new WindowCapture());

            // Five clicks, then assert the on-screen label reached "Count: 5".
            List<BatchStep> steps = new();
            for (int i = 1; i <= 5; i++)
            {
                steps.Add(new BatchStep("click", Label: $"increment-{i}", AutomationId: IncrementButtonId));
            }
            // CountLabel is a TextBlock: its text is exposed via Name, not the
            // Value pattern, so assert the count through a NameContains locator.
            steps.Add(new BatchStep(
                "wait",
                Label: "assert-count",
                NameContains: "Count: 5",
                Condition: "exists"));

            BatchResult result = runner.Run(steps, process.Id, stopOnError: true);

            result.Success.Should().BeTrue(
                "every step should pass; failure: " + (result.FailedAtIndex is int f
                    ? result.Steps[f].Error
                    : "none"));
            result.Completed.Should().Be(6);
            result.Total.Should().Be(6);
            result.FailedAtIndex.Should().BeNull();

            // Cross-check against the managed counter for good measure.
            OperationResult state = new ManagedStateReader().Read(
                process.Id, CounterTypeName, CounterFieldName);
            state.Success.Should().BeTrue(state.Error);
            state.Data.Should().Be(5);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    [Fact]
    public void Stop_on_error_reports_the_failing_step_and_skips_the_rest()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        try
        {
            var waiter = new ElementWaiter();
            OperationResult appeared = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: CountLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);

            var runner = new StepRunner(
                new ActionInvoker(), waiter, new ElementStateReader(), new WindowCapture());

            List<BatchStep> steps = new()
            {
                new BatchStep("click", Label: "ok", AutomationId: IncrementButtonId),
                new BatchStep("click", Label: "missing", AutomationId: "NoSuchButton"),
                new BatchStep("click", Label: "never-runs", AutomationId: IncrementButtonId),
            };

            BatchResult result = runner.Run(steps, process.Id, stopOnError: true);

            result.Success.Should().BeFalse();
            result.FailedAtIndex.Should().Be(1);
            result.Completed.Should().Be(1);
            result.Steps.Should().HaveCount(2, "the third step must be skipped after the failure");
            result.Steps[1].Error.Should().NotBeNullOrEmpty();
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    [Fact]
    public void A_dialog_opened_by_the_target_does_not_stop_a_guarded_batch()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        try
        {
            var waiter = new ElementWaiter();
            OperationResult appeared = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: CountLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);

            var runner = new StepRunner(
                new ActionInvoker(), waiter, new ElementStateReader(), new WindowCapture());
            List<BatchStep> steps = new()
            {
                new BatchStep("click", Label: "open", AutomationId: OpenDialogButtonId),
                new BatchStep("wait", AutomationId: NoteInputId, WindowTitle: DialogTitleFragment, Condition: "exists"),
                new BatchStep("type", Label: "note", AutomationId: NoteInputId, WindowTitle: DialogTitleFragment, Text: NoteText),
                new BatchStep("click", Label: "confirm", AutomationId: ConfirmNoteButtonId, WindowTitle: DialogTitleFragment),
                new BatchStep("wait", AutomationId: NoteInputId, Condition: "not_exists"),
                new BatchStep("click", Label: "after-dialog", AutomationId: IncrementButtonId),
                new BatchStep("wait", AutomationId: LastNoteLabelId, NameContains: NoteText, Condition: "exists"),
            };

            BatchResult result = runner.Run(steps, process.Id, stopOnError: true);

            result.ForegroundChanged.Should().BeFalse(
                "the dialog belongs to the target: " + string.Join("; ", result.Steps.Select(s => s.Error)));
            result.Success.Should().BeTrue(string.Join("; ", result.Steps.Select(s => s.Error)));
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
