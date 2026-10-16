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

using System.Drawing;
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Ocr;
using Winsight.Core.Modules.Orchestration;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Core.Modules.VisualRegression;
using Xunit;

namespace Winsight.Core.Tests;

/// <summary>
/// Visual waits on scripted frames and a scripted clock, the in-memory SSIM
/// comparison, OCR rectangle mapping and text matching, and run_steps
/// validation of the new steps.
/// </summary>
public sealed class VisualObservationTests
{
    private static readonly WindowRegion Whole = new(0, 0, 32, 32);

    [Fact]
    public void Change_is_reported_at_the_first_differing_capture()
    {
        ScriptedClock clock = new();
        Queue<bool> passes = new([true, true, false]);

        OperationResult result = VisualWaiter.RunChange(
            Blank(), Blank, (_, _) => Diff(passes.Dequeue()), new VisualWaitOptions(1000, 100), Whole, clock);

        result.Success.Should().BeTrue();
        VisualWaitResult wait = (VisualWaitResult)result.Data!;
        wait.Met.Should().BeTrue();
        wait.Frames.Should().Be(3);
        clock.ElapsedMs.Should().Be(200);
    }

    [Fact]
    public void No_change_before_the_timeout_fails_with_timed_out()
    {
        ScriptedClock clock = new();

        OperationResult result = VisualWaiter.RunChange(
            Blank(), Blank, (_, _) => Diff(true), new VisualWaitOptions(300, 100), Whole, clock);

        result.Success.Should().BeFalse();
        result.Error.Should().StartWith("timed_out:");
        ((VisualWaitResult)result.Data!).Met.Should().BeFalse();
    }

    [Fact]
    public void Stable_needs_the_requested_unchanged_comparisons_in_a_row()
    {
        ScriptedClock clock = new();
        Queue<bool> passes = new([true, false, true, true, true]);

        OperationResult result = VisualWaiter.RunStable(
            Blank(), Blank, (_, _) => Diff(passes.Dequeue()),
            new VisualWaitOptions(5000, 100, StableComparisons: 3), Whole, clock);

        result.Success.Should().BeTrue();
        ((VisualWaitResult)result.Data!).Frames.Should().Be(6, "one first capture plus five comparisons");
    }

    [Fact]
    public void A_window_that_keeps_changing_times_out()
    {
        ScriptedClock clock = new();

        OperationResult result = VisualWaiter.RunStable(
            Blank(), Blank, (_, _) => Diff(false), new VisualWaitOptions(500, 100), Whole, clock);

        result.Error.Should().StartWith("timed_out:").And.Contain("still changing");
    }

    [Theory]
    [InlineData(-1, 100, 3)]
    [InlineData(VisualWaitOptions.MaxTimeoutMs + 1, 100, 3)]
    [InlineData(1000, VisualWaitOptions.MinPollMs - 1, 3)]
    [InlineData(1000, 100, 0)]
    public void Out_of_range_wait_options_are_refused(int timeoutMs, int pollMs, int stable)
    {
        new VisualWaitOptions(timeoutMs, pollMs, stable).Validate().Should().NotBeNull();
    }

    [Fact]
    public void In_memory_comparison_sees_a_small_block_change()
    {
        using Bitmap before = Solid(64, 64, Color.White);
        using Bitmap same = Solid(64, 64, Color.White);
        using Bitmap after = Solid(64, 64, Color.White);
        using (Graphics graphics = Graphics.FromImage(after))
        {
            graphics.FillRectangle(Brushes.Black, 20, 20, 12, 12);
        }
        VisualDiffer differ = new();
        VisualDiffOptions options = VisualDiffOptions.FromEnvironment(getEnvironmentVariable: _ => null);

        differ.Compare(before, same, options).Passed.Should().BeTrue();
        differ.Compare(before, after, options).Passed.Should().BeFalse();
    }

    [Fact]
    public void Ocr_rectangles_are_scaled_back_and_offset_by_the_region()
    {
        WindowRegion mapped = OcrGeometry.ToWindow(41, 20, 60, 31, scale: 2, new WindowRegion(100, 50, 200, 80));

        mapped.Should().Be(new WindowRegion(120, 60, 31, 16));
    }

    [Fact]
    public void A_line_box_is_the_union_of_its_words()
    {
        OcrGeometry.Union([new WindowRegion(10, 10, 20, 8), new WindowRegion(40, 8, 10, 12)])
            .Should().Be(new WindowRegion(10, 8, 40, 12));
    }

    [Theory]
    [InlineData(400, 300, 2, 10000, 2)]
    [InlineData(4000, 300, 4, 10000, 2)]
    [InlineData(12000, 300, 2, 10000, 1)]
    public void Enlargement_never_exceeds_the_engine_maximum(int width, int height, int requested, int max, int expected)
    {
        VisualTextReader.FitScale(width, height, requested, max).Should().Be(expected);
    }

    [Fact]
    public void Text_matching_ignores_case_and_spacing()
    {
        VisualTextResult read = Read("Status:  READY", "Clicked 3");

        VisualTextMatcher.Find(read, "status: ready")!.Text.Should().Be("Status:  READY");
        VisualTextMatcher.Find(read, "clicked 3").Should().NotBeNull();
        VisualTextMatcher.Find(read, "Clicked 4").Should().BeNull();
        VisualTextMatcher.Find(read, "   ").Should().BeNull();
    }

    [Fact]
    public void Text_split_over_two_lines_still_matches()
    {
        VisualTextResult read = Read("Connection to", "lab-a refused");

        VisualTextLine? line = VisualTextMatcher.Find(read, "to lab-a");

        line.Should().NotBeNull();
        line!.Bounds.Should().Be(new WindowRegion(0, 0, 100, 40));
    }

    [Fact]
    public void A_region_outside_the_window_is_refused()
    {
        WindowRegion.Validate(null, 100, 100).Should().BeNull();
        WindowRegion.Validate(new WindowRegion(0, 0, 100, 100), 100, 100).Should().BeNull();
        WindowRegion.Validate(new WindowRegion(50, 50, 51, 10), 100, 100).Should().Contain("inside");
        WindowRegion.Validate(new WindowRegion(0, 0, 0, 10), 100, 100).Should().Contain("positive");
    }

    [Theory]
    [InlineData("wait_visual_change", "timeoutMs")]
    [InlineData("wait_visual_stable", "timeoutMs")]
    public void Visual_wait_steps_validate_their_bounds(string action, string expected)
    {
        BatchResult result = NewRunner().Run(
            [new BatchStep(action, ProcessId: 1, TimeoutMs: -5)], defaultProcessId: null, stopOnError: true);

        result.Steps[0].Error.Should().ContainEquivalentOf(expected);
    }

    [Fact]
    public void Text_visible_needs_the_expected_text()
    {
        BatchResult result = NewRunner().Run(
            [new BatchStep("assert", ProcessId: 1, Operator: "text_visible")], defaultProcessId: null, stopOnError: true);

        result.Steps[0].Error.Should().Contain("expectedValue");
    }

    [Fact]
    public void Type_keys_steps_validate_text_and_mode()
    {
        BatchResult missing = NewRunner().Run(
            [new BatchStep("type_keys", ProcessId: 1)], defaultProcessId: null, stopOnError: true);
        BatchResult badMode = NewRunner().Run(
            [new BatchStep("type_keys", ProcessId: 1, Text: "x", Mode: "shout")], defaultProcessId: null, stopOnError: true);

        missing.Steps[0].Error.Should().Contain("text is required");
        badMode.Steps[0].Error.Should().Contain("Unknown mode");
    }

    private static StepRunner NewRunner() =>
        new(new ActionInvoker(), new ElementWaiter(), new ElementStateReader(), new WindowCapture(), new NoForeground());

    private static VisualTextResult Read(string first, string second)
    {
        VisualTextLine one = new(first, new WindowRegion(0, 0, 100, 20), []);
        VisualTextLine two = new(second, new WindowRegion(0, 20, 60, 20), []);
        return new VisualTextResult("en-US", first + "\n" + second, [one, two], new WindowRegion(0, 0, 200, 100), 2);
    }

    private static Bitmap Blank() => new(32, 32);

    private static Bitmap Solid(int width, int height, Color color)
    {
        Bitmap bitmap = new(width, height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }

    private static VisualDiffResult Diff(bool passed) => new(passed, 1, 1, 0, null, string.Empty);

    private sealed class ScriptedClock : IWaitClock
    {
        public long ElapsedMs { get; private set; }

        public void Sleep(int milliseconds) => ElapsedMs += milliseconds;
    }

    private sealed class NoForeground : IForegroundReader
    {
        public ForegroundSample Read() => default;

        public bool Exists(long handle) => true;
    }
}
