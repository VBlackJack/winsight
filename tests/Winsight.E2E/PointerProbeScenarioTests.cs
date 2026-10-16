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
using Winsight.Core.Modules.Orchestration;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// Coordinate clicks on the lab's pointer probe, a raw Win32 window that acts
/// like iced on winit: no UI Automation children, hover resolved once per frame
/// from the last WM_MOUSEMOVE, and presses that act on that frame-latched hover.
/// A click whose press outruns the pointer move acts on the previous position;
/// these scenarios require the press to land where the pointer was sent.
/// </summary>
[Trait("Category", "E2E")]
public class PointerProbeScenarioTests
{
    private const string ProbeArgument = "--pointer-probe";
    private const string ProbeTitle = "Pointer Probe";
    private const string LabTitle = "Winsight WPF Lab";
    private const string FocusInputId = "FocusInput";
    private const string PressKindId = "ProbePressKind";
    private const string PressPointId = "ProbePressPoint";
    private const string HoverPointId = "ProbeHoverPoint";
    private const string PressCountId = "ProbePressCount";
    private const int ProbeOpenTimeoutMs = 5000;
    private const int ProbePollMs = 50;

    // Window-relative points inside the probe's client area (below its title bar).
    private static readonly (int X, int Y) First = (120, 140);
    private static readonly (int X, int Y) Second = (300, 220);
    private static readonly (int X, int Y) Third = (180, 260);

    [Fact]
    public void Left_click_presses_where_the_pointer_was_sent()
    {
        using WpfLabSession lab = StartWithProbe();
        ActionInvoker invoker = new();

        ClickAndExpect(lab, invoker, First, ClickStyle.Primary, expectedCount: 1, expectedKind: "Left");
        ClickAndExpect(lab, invoker, Second, ClickStyle.Primary, expectedCount: 2, expectedKind: "Left");
    }

    [Fact]
    public void Double_and_right_clicks_press_where_the_pointer_was_sent()
    {
        using WpfLabSession lab = StartWithProbe();
        ActionInvoker invoker = new();

        ClickAndExpect(lab, invoker, First, ClickStyle.Primary, expectedCount: 1, expectedKind: "Left");
        // A double click reports its first press as Left and its second as Double.
        ClickAndExpect(lab, invoker, Second, ClickStyle.DoubleClick, expectedCount: 3, expectedKind: "Double");
        ClickAndExpect(lab, invoker, Third, ClickStyle.RightClick, expectedCount: 4, expectedKind: "Right");
    }

    [Fact]
    public void Run_steps_click_point_presses_where_the_pointer_was_sent()
    {
        using WpfLabSession lab = StartWithProbe();
        StepRunner runner = new(new ActionInvoker(), new ElementWaiter(), new ElementStateReader(), new WindowCapture());
        BatchStep[] steps =
        {
            new("click_point", X: First.X, Y: First.Y, WindowTitle: ProbeTitle),
            new("click_point", X: Second.X, Y: Second.Y, WindowTitle: ProbeTitle),
        };

        BatchResult result = runner.Run(steps, lab.ProcessId, stopOnError: true);

        result.Success.Should().BeTrue(string.Join("; ", result.Steps.Select(s => s.Error)));
        lab.WaitForName(PressCountId, "2").Should().BeTrue(lab.ReadKeyLog());
        lab.ReadName(HoverPointId).Should().Be(
            lab.ReadName(PressPointId),
            "the second press must act where the pointer was sent, not where the first click left it");
    }

    [Fact]
    public void Focus_window_raises_a_covered_probe_before_the_click()
    {
        using WpfLabSession lab = StartWithProbe();
        ActionInvoker invoker = new();
        // The lab window is larger than the probe and centered on it: raising it covers the probe.
        OperationResult cover = invoker.Type(
            lab.ProcessId, new ElementLocator(AutomationId: FocusInputId), "x", LabTitle, focusWindow: true);
        cover.Success.Should().BeTrue(cover.Error);

        OperationResult refused = invoker.ClickPoint(lab.ProcessId, First.X, First.Y, windowTitle: ProbeTitle);
        refused.Success.Should().BeFalse("a covered point must not be clicked without focusWindow");
        refused.Error.Should().Contain("occluded");

        OperationResult click = invoker.ClickPoint(
            lab.ProcessId, First.X, First.Y, windowTitle: ProbeTitle, focusWindow: true);
        click.Success.Should().BeTrue(click.Error);
        lab.WaitForName(PressCountId, "1").Should().BeTrue(lab.ReadKeyLog());
        lab.ReadName(HoverPointId).Should().Be(lab.ReadName(PressPointId));
    }

    internal static WpfLabSession StartWithProbe()
    {
        WpfLabSession lab = WpfLabSession.Start(ProbeArgument);
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!WindowGeometry.TryResolveWindow(lab.ProcessId, ProbeTitle, out _, out _))
        {
            if (elapsed.ElapsedMilliseconds > ProbeOpenTimeoutMs)
            {
                lab.Dispose();
                throw new TimeoutException($"The pointer probe did not open within {ProbeOpenTimeoutMs} ms.");
            }
            Thread.Sleep(ProbePollMs);
        }
        return lab;
    }

    private static void ClickAndExpect(
        WpfLabSession lab,
        ActionInvoker invoker,
        (int X, int Y) point,
        ClickStyle style,
        int expectedCount,
        string expectedKind)
    {
        OperationResult click = invoker.ClickPoint(lab.ProcessId, point.X, point.Y, style, ProbeTitle);
        click.Success.Should().BeTrue(click.Error);
        ClickPointResult clicked = (ClickPointResult)click.Data!;

        lab.WaitForName(PressCountId, expectedCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Should().BeTrue(lab.ReadKeyLog());
        lab.ReadName(PressKindId).Should().Be(expectedKind);
        string press = lab.ReadName(PressPointId);
        press.Should().Be($"{clicked.ScreenX},{clicked.ScreenY}", "the button message must carry the requested point");
        lab.ReadName(HoverPointId).Should().Be(
            press,
            "an immediate-mode UI acts on the hover latched at its last frame; the pointer must settle before the press");
    }
}
