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

using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Orchestration;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

/// <summary>
/// Covers the orchestration logic — sequencing, the journal, stop-on-error, and
/// pre-dispatch validation — using steps that fail before touching live UIA, so
/// no desktop is required.
/// </summary>
public sealed class StepRunnerTests
{
    private static StepRunner NewRunner() =>
        new(new ActionInvoker(), new ElementWaiter(), new ElementStateReader(), new WindowCapture());

    [Fact]
    public void Unknown_action_fails_the_step_with_an_actionable_error()
    {
        BatchResult result = NewRunner().Run(
            new[] { new BatchStep("teleport", ProcessId: 123) }, defaultProcessId: null, stopOnError: true);

        result.Success.Should().BeFalse();
        result.FailedAtIndex.Should().Be(0);
        result.Steps.Should().ContainSingle();
        result.Steps[0].Error.Should().Contain("Unknown action");
    }

    [Fact]
    public void Missing_process_id_fails_when_no_default_is_provided()
    {
        BatchResult result = NewRunner().Run(
            new[] { new BatchStep("click", AutomationId: "X") }, defaultProcessId: null, stopOnError: true);

        result.Steps[0].Success.Should().BeFalse();
        result.Steps[0].Error.Should().Contain("processId");
    }

    [Fact]
    public void Click_point_requires_coordinates()
    {
        BatchResult result = NewRunner().Run(
            new[] { new BatchStep("click_point", ProcessId: 1) }, defaultProcessId: null, stopOnError: true);

        result.Steps[0].Error.Should().Contain("x and y");
    }

    [Fact]
    public void Stop_on_error_halts_at_the_first_failure_and_skips_the_rest()
    {
        BatchStep[] steps =
        {
            new("teleport", Label: "bad", ProcessId: 1),
            new("click", AutomationId: "X", ProcessId: 1),
        };

        BatchResult result = NewRunner().Run(steps, defaultProcessId: null, stopOnError: true);

        result.Total.Should().Be(2);
        result.Completed.Should().Be(0);
        result.FailedAtIndex.Should().Be(0);
        result.Steps.Should().ContainSingle("the second step must not run after the first fails");
        result.Steps[0].Label.Should().Be("bad");
    }

    [Fact]
    public void Without_stop_on_error_every_step_runs_and_first_failure_is_reported()
    {
        BatchStep[] steps =
        {
            new("teleport", ProcessId: 1),
            new("warp", ProcessId: 1),
        };

        BatchResult result = NewRunner().Run(steps, defaultProcessId: null, stopOnError: false);

        result.Steps.Should().HaveCount(2, "no stop-on-error means all steps are attempted");
        result.FailedAtIndex.Should().Be(0, "the journal points at the first failure");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Batch_level_process_id_is_used_when_a_step_omits_its_own()
    {
        // The step omits processId; with a default present it passes validation
        // and reaches the unknown-action branch instead of the missing-pid one.
        BatchResult result = NewRunner().Run(
            new[] { new BatchStep("teleport") }, defaultProcessId: 999, stopOnError: true);

        result.Steps[0].Error.Should().Contain("Unknown action");
        result.Steps[0].Error.Should().NotContain("processId");
    }
}
