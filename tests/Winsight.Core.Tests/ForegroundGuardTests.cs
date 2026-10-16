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
/// A batch stops when someone else takes the foreground between its steps,
/// and only then. Steps target a process id that does not exist, so they fail
/// before any input reaches the desktop.
/// </summary>
public sealed class ForegroundGuardTests
{
    private const int TargetProcess = int.MaxValue - 7;
    private const int OtherProcess = 4242;
    private static readonly ForegroundSample Target = new(0x1000, TargetProcess);
    private static readonly ForegroundSample TargetDialog = new(0x1001, TargetProcess);
    private static readonly ForegroundSample Browser = new(0x2000, OtherProcess);
    private static readonly ForegroundSample Editor = new(0x3000, OtherProcess + 1);
    private static readonly HashSet<int> Batch = new() { TargetProcess };

    [Fact]
    public void The_same_window_is_accepted()
    {
        ForegroundGuard.Accepts(Target, Target, true, Batch).Should().BeTrue();
    }

    [Fact]
    public void A_window_of_a_batch_process_is_accepted()
    {
        ForegroundGuard.Accepts(Target, TargetDialog, true, Batch).Should().BeTrue();
    }

    [Fact]
    public void Another_process_taking_the_foreground_is_refused()
    {
        ForegroundGuard.Accepts(Target, Browser, true, Batch).Should().BeFalse();
        ForegroundGuard.Accepts(Browser, Editor, true, Batch).Should().BeFalse(
            "a person moving between their own windows while the batch works in the background is still activity");
    }

    [Fact]
    public void Transitions_and_a_closed_baseline_are_accepted()
    {
        ForegroundGuard.Accepts(Target, default, true, Batch).Should().BeTrue("no foreground is a transition");
        ForegroundGuard.Accepts(default, Browser, true, Batch).Should().BeTrue("an unknown baseline cannot be compared");
        ForegroundGuard.Accepts(Target, Browser, false, Batch).Should().BeTrue(
            "the application closed the baseline window and Windows chose another");
    }

    [Fact]
    public void The_message_names_both_windows_without_titles()
    {
        string message = ForegroundGuard.Describe(Target, Browser);

        message.Should().StartWith("foreground_changed:").And.Contain("0x1000").And.Contain("0x2000");
    }

    [Fact]
    public void A_batch_stops_before_the_next_screen_step_when_the_foreground_moves_away()
    {
        ScriptedForeground foreground = new(Target, Target, Target, Browser);
        BatchStep[] steps =
        {
            new("click", AutomationId: "A", ProcessId: TargetProcess),
            new("click", AutomationId: "B", ProcessId: TargetProcess),
            new("click", AutomationId: "C", ProcessId: TargetProcess),
        };

        BatchResult result = NewRunner(foreground).Run(steps, defaultProcessId: null, stopOnError: false);

        result.ForegroundChanged.Should().BeTrue();
        result.Steps.Should().HaveCount(2, "the third step must not run");
        result.Steps[1].Error.Should().StartWith("foreground_changed:");
        result.FailedAtIndex.Should().Be(0, "the first step failed on its own before the guard stopped the batch");
    }

    [Fact]
    public void Non_screen_steps_are_not_guarded()
    {
        ScriptedForeground foreground = new(Target, Browser);
        BatchStep[] steps =
        {
            new("get_state", AutomationId: "A", ProcessId: TargetProcess),
            new("assert", AutomationId: "A", Operator: "exists", ProcessId: TargetProcess),
        };

        BatchResult result = NewRunner(foreground).Run(steps, defaultProcessId: null, stopOnError: false);

        result.ForegroundChanged.Should().BeFalse();
        result.Steps.Should().HaveCount(2);
    }

    [Fact]
    public void The_guard_can_be_turned_off()
    {
        ScriptedForeground foreground = new(Target, Browser);
        BatchStep[] steps =
        {
            new("click", AutomationId: "A", ProcessId: TargetProcess),
            new("click", AutomationId: "B", ProcessId: TargetProcess),
        };

        BatchResult result = NewRunner(foreground).Run(
            steps, defaultProcessId: null, stopOnError: false, stopOnForegroundChange: false);

        result.ForegroundChanged.Should().BeFalse();
        result.Steps.Should().HaveCount(2);
        foreground.Reads.Should().Be(0);
    }

    private static StepRunner NewRunner(IForegroundReader foreground) =>
        new(new ActionInvoker(), new ElementWaiter(), new ElementStateReader(), new WindowCapture(), foreground);

    /// <summary>Returns the scripted samples in order, then repeats the last one.</summary>
    private sealed class ScriptedForeground : IForegroundReader
    {
        private readonly ForegroundSample[] _samples;

        public ScriptedForeground(params ForegroundSample[] samples) => _samples = samples;

        public int Reads { get; private set; }

        public ForegroundSample Read() => _samples[Math.Min(Reads++, _samples.Length - 1)];

        public bool Exists(long handle) => true;
    }
}
