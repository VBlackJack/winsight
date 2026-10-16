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

namespace Winsight.E2E;

/// <summary>
/// Wheel and hover at a coordinate on the pointer probe, which sums wheel
/// turns per axis and reports its frame-latched hover, like a drawn list or
/// canvas that only reacts to where the pointer rests.
/// </summary>
[Trait("Category", "E2E")]
public class PointerWheelScenarioTests
{
    private const string ProbeTitle = "Pointer Probe";
    private const string WheelId = "ProbeWheel";
    private const string HoverNowId = "ProbeHoverNow";
    private const int WheelNotch = 120;
    private static readonly (int X, int Y) Point = (200, 180);

    [Fact]
    public void Scroll_point_turns_the_wheel_over_the_probe_on_both_axes()
    {
        using WpfLabSession lab = PointerProbeScenarioTests.StartWithProbe();
        ActionInvoker invoker = new();

        OperationResult down = invoker.ScrollPoint(
            lab.ProcessId, Point.X, Point.Y, -3, windowTitle: ProbeTitle, focusWindow: true);
        down.Success.Should().BeTrue(down.Error);
        OperationResult right = invoker.ScrollPoint(
            lab.ProcessId, Point.X, Point.Y, 2, horizontal: true, windowTitle: ProbeTitle);
        right.Success.Should().BeTrue(right.Error);

        lab.WaitForName(WheelId, $"v={-3 * WheelNotch} h={2 * WheelNotch}").Should().BeTrue(lab.ReadName(WheelId));
    }

    [Fact]
    public void Hover_point_leaves_the_pointer_where_the_next_frame_sees_it()
    {
        using WpfLabSession lab = PointerProbeScenarioTests.StartWithProbe();

        OperationResult hover = new ActionInvoker().HoverPoint(
            lab.ProcessId, Point.X, Point.Y, holdMs: 200, windowTitle: ProbeTitle, focusWindow: true);

        hover.Success.Should().BeTrue(hover.Error);
        ClickPointResult rested = (ClickPointResult)hover.Data!;
        lab.WaitForName(HoverNowId, $"{rested.ScreenX},{rested.ScreenY}").Should().BeTrue(lab.ReadName(HoverNowId));
    }

    [Fact]
    public void Run_steps_scrolls_and_hovers_by_coordinates()
    {
        using WpfLabSession lab = PointerProbeScenarioTests.StartWithProbe();
        StepRunner runner = new(new ActionInvoker(), new ElementWaiter(), new ElementStateReader(), new WindowCapture());
        BatchStep[] steps =
        {
            new("hover_point", X: Point.X, Y: Point.Y, WindowTitle: ProbeTitle, FocusWindow: true, HoldMs: 100),
            new("scroll_point", X: Point.X, Y: Point.Y, WindowTitle: ProbeTitle, Delta: 1),
        };

        BatchResult result = runner.Run(steps, lab.ProcessId, stopOnError: true);

        result.Success.Should().BeTrue(string.Join("; ", result.Steps.Select(s => s.Error)));
        lab.WaitForName(WheelId, $"v={WheelNotch} h=0").Should().BeTrue(lab.ReadName(WheelId));
    }
}
