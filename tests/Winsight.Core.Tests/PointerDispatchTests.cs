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
using FlaUI.Core.Input;
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

/// <summary>
/// The click helper must move, wait, verify and only then press, without a
/// second move; a recorder stands in for the physical pointer.
/// </summary>
public sealed class PointerDispatchTests
{
    private static readonly Point Target = new(400, 300);

    [Fact]
    public void Moves_then_settles_then_presses_without_moving_again()
    {
        RecordingPointer pointer = new();

        OperationResult? refusal = PointerDispatch.Click(Target, ClickStyle.Primary, settleMs: 120, driver: pointer);

        refusal.Should().BeNull();
        pointer.Calls.Should().Equal("move 400,300", "wait 120", "click Left");
    }

    [Fact]
    public void Default_settle_is_used_when_none_is_given()
    {
        RecordingPointer pointer = new();

        PointerDispatch.Click(Target, ClickStyle.Primary, driver: pointer);

        pointer.Calls.Should().Contain($"wait {PointerDispatch.DefaultSettleMs}");
    }

    [Theory]
    [InlineData(ClickStyle.DoubleClick, "double Left")]
    [InlineData(ClickStyle.RightClick, "click Right")]
    public void Double_and_right_clicks_press_at_the_settled_position(ClickStyle style, string expectedPress)
    {
        RecordingPointer pointer = new();

        PointerDispatch.Click(Target, style, settleMs: 0, driver: pointer).Should().BeNull();

        pointer.Calls.Should().Equal("move 400,300", "wait 0", expectedPress);
    }

    [Fact]
    public void A_pointer_moved_during_the_settle_refuses_the_press()
    {
        RecordingPointer pointer = new() { Drift = new Size(40, 0) };

        OperationResult? refusal = PointerDispatch.Click(Target, ClickStyle.Primary, driver: pointer);

        refusal.Should().NotBeNull();
        refusal!.Error.Should().StartWith("cursor_moved:").And.Contain("(440, 300)");
        pointer.Calls.Should().NotContain(call => call.StartsWith("click", StringComparison.Ordinal));
    }

    [Fact]
    public void A_one_pixel_rounding_difference_is_tolerated()
    {
        RecordingPointer pointer = new() { Drift = new Size(1, -1) };

        PointerDispatch.Click(Target, ClickStyle.Primary, driver: pointer).Should().BeNull();
    }

    [Fact]
    public void A_revalidation_refusal_after_the_settle_stops_the_press()
    {
        RecordingPointer pointer = new();

        OperationResult? refusal = PointerDispatch.Click(
            Target, ClickStyle.Primary, revalidate: () => "occluded", driver: pointer);

        refusal!.Error.Should().Be("occluded");
        pointer.Calls.Should().Equal("move 400,300", $"wait {PointerDispatch.DefaultSettleMs}");
    }

    [Theory]
    [InlineData(false, "scroll -3")]
    [InlineData(true, "hscroll -3")]
    public void Scroll_moves_settles_then_turns_the_wheel_where_the_pointer_rests(bool horizontal, string expectedTurn)
    {
        RecordingPointer pointer = new();

        PointerDispatch.Scroll(Target, -3, horizontal, settleMs: 80, driver: pointer).Should().BeNull();

        pointer.Calls.Should().Equal("move 400,300", "wait 80", expectedTurn);
    }

    [Fact]
    public void Scroll_is_refused_when_the_pointer_was_moved()
    {
        RecordingPointer pointer = new() { Drift = new Size(0, 25) };

        PointerDispatch.Scroll(Target, 2, false, driver: pointer)!.Error.Should().StartWith("cursor_moved:");
        pointer.Calls.Should().NotContain(call => call.Contains("scroll", StringComparison.Ordinal));
    }

    [Fact]
    public void Hover_holds_the_pointer_and_presses_nothing()
    {
        RecordingPointer pointer = new();

        PointerDispatch.Hover(Target, holdMs: 500, settleMs: 100, driver: pointer).Should().BeNull();

        pointer.Calls.Should().Equal("move 400,300", "wait 100", "wait 500");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PointerDispatch.MaxScrollNotches + 1)]
    [InlineData(-PointerDispatch.MaxScrollNotches - 1)]
    public void Out_of_range_wheel_turns_are_refused(int notches)
    {
        PointerDispatch.ValidateScroll(notches).Should().Contain("delta");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(PointerDispatch.MaxHoverHoldMs + 1)]
    public void Out_of_range_hover_holds_are_refused(int holdMs)
    {
        PointerDispatch.ValidateHold(holdMs).Should().Contain("holdMs");
        PointerDispatch.ValidateHold(null).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(PointerDispatch.MaxSettleMs)]
    public void Settle_within_bounds_is_accepted(int? settleMs)
    {
        PointerDispatch.ValidateSettle(settleMs).Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(PointerDispatch.MaxSettleMs + 1)]
    public void Settle_out_of_bounds_is_refused(int settleMs)
    {
        PointerDispatch.ValidateSettle(settleMs).Should().Contain("settleMs");
    }

    [Fact]
    public void A_pointer_still_at_start_position_reports_move_not_applied_instead_of_cursor_moved()
    {
        Point start = new(1245, 890);
        Point target = new(1857, 44);
        RecordingPointer pointer = new(start)
        {
            ApplyMove = false,
            MoveOutcome = new PointerMoveResult(0, 5, 20286, 2004, new VirtualDesktopBounds(0, 0, 6000, 1440)),
        };
        IntegrityComparison integrity = ProcessIntegrity.Compare(
            new ProcessIntegrityInfo(ProcessIntegrity.RidMedium, "medium", false, null),
            new ProcessIntegrityInfo(ProcessIntegrity.RidHigh, "high", false, null));

        OperationResult? refusal = PointerDispatch.Click(
            target, ClickStyle.Primary, settleMs: 150, driver: pointer, integrity: integrity);

        refusal.Should().NotBeNull();
        refusal!.Error.Should().StartWith($"{PointerDispatch.MoveNotAppliedCode}:")
            .And.NotContain("other input moved it")
            .And.Contain("SendInput=0")
            .And.Contain("win32Error=5")
            .And.Contain("medium")
            .And.Contain("high")
            .And.Contain("(1245, 890)")
            .And.Contain("(1857, 44)")
            .And.Contain("6000x1440");
        pointer.Calls.Should().NotContain(call => call.StartsWith("click", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pointer_already_at_target_before_move_succeeds()
    {
        RecordingPointer pointer = new(Target) { ApplyMove = false };

        OperationResult? refusal = PointerDispatch.Click(Target, ClickStyle.Primary, settleMs: 0, driver: pointer);

        refusal.Should().BeNull();
        pointer.Calls.Should().Equal("move 400,300", "wait 0", "click Left");
    }

    [Fact]
    public void A_pointer_moved_elsewhere_from_both_start_and_target_reports_cursor_moved()
    {
        Point start = new(100, 100);
        RecordingPointer pointer = new(start) { PostMovePosition = new Point(700, 500) };

        OperationResult? refusal = PointerDispatch.Click(Target, ClickStyle.Primary, settleMs: 150, driver: pointer);

        refusal.Should().NotBeNull();
        refusal!.Error.Should().StartWith($"{PointerDispatch.CursorMovedCode}:")
            .And.Contain("(700, 500)")
            .And.Contain("other input moved it");
        pointer.Calls.Should().NotContain(call => call.StartsWith("click", StringComparison.Ordinal));
    }

    [Fact]
    public void Job_ui_restriction_returns_input_context_blocked_instead_of_move_not_applied()
    {
        InputContext.OverrideCapture = () => new InputContextSnapshot(
            SessionId: 1,
            ActiveConsoleSessionId: 1,
            SessionMatchesConsole: true,
            WindowStation: "WinSta0",
            WindowStationVisible: true,
            ThreadDesktop: "Default",
            InputDesktop: "Default",
            InputDesktopWin32Error: null,
            DesktopMatchesInput: true,
            InJob: true,
            JobUiRestrictionsMask: InputContext.JobUiLimitHandles | InputContext.JobUiLimitDesktop,
            JobUiRestrictions: InputContext.DecodeJobUiRestrictions(InputContext.JobUiLimitHandles | InputContext.JobUiLimitDesktop),
            JobQueryWin32Error: null,
            IsRestrictedToken: false,
            IsAppContainer: false,
            IntegrityLevel: "medium (0x2000)",
            ParentChain: [new ProcessChainEntry(65456, "Claude.exe")]);
        try
        {
            RecordingPointer pointer = new(new Point(100, 100)) { ApplyMove = false };

            OperationResult? refusal = PointerDispatch.Click(Target, ClickStyle.Primary, settleMs: 150, driver: pointer);

            refusal.Should().NotBeNull();
            refusal!.Error.Should().StartWith($"{InputContext.InputContextBlockedCode}:")
                .And.Contain("JOB_OBJECT_UILIMIT_HANDLES")
                .And.Contain("JOB_OBJECT_UILIMIT_DESKTOP")
                .And.NotContain(PointerDispatch.MoveNotAppliedCode);
            pointer.Calls.Should().BeEmpty("input must be refused before attempting a blocked move");
        }
        finally
        {
            InputContext.OverrideCapture = null;
        }
    }

    [Fact]
    public void Desktop_mismatch_attempts_set_thread_desktop_before_move_and_reports_outcome()
    {
        bool switchAttempted = false;
        InputContext.OverrideCapture = () => new InputContextSnapshot(
            SessionId: 1,
            ActiveConsoleSessionId: 1,
            SessionMatchesConsole: true,
            WindowStation: "WinSta0",
            WindowStationVisible: true,
            ThreadDesktop: "OtherDesktop",
            InputDesktop: "Default",
            InputDesktopWin32Error: null,
            DesktopMatchesInput: false,
            InJob: false,
            JobUiRestrictionsMask: 0,
            JobUiRestrictions: Array.Empty<string>(),
            JobQueryWin32Error: null,
            IsRestrictedToken: false,
            IsAppContainer: false,
            IntegrityLevel: "medium (0x2000)",
            ParentChain: Array.Empty<ProcessChainEntry>());
        InputContext.OverrideDesktopSwitch = ctx =>
        {
            switchAttempted = true;
            return new DesktopSwitchAttempt(Attempted: true, Succeeded: false, Win32Error: 170, TargetDesktop: ctx.InputDesktop);
        };
        try
        {
            RecordingPointer pointer = new(new Point(100, 100)) { ApplyMove = false };

            OperationResult? refusal = PointerDispatch.Click(Target, ClickStyle.Primary, settleMs: 0, driver: pointer);

            switchAttempted.Should().BeTrue();
            refusal.Should().NotBeNull();
            refusal!.Error.Should().StartWith($"{PointerDispatch.MoveNotAppliedCode}:")
                .And.Contain("setThreadDesktop=False(err=170)");
        }
        finally
        {
            InputContext.OverrideCapture = null;
            InputContext.OverrideDesktopSwitch = null;
        }
    }

    [Fact]
    public void Successful_click_reports_dispatch_thread_desktop_and_switch_result()
    {
        int calls = 0;
        InputContext.OverrideCapture = () =>
        {
            calls++;
            bool switched = calls > 1;
            return new InputContextSnapshot(
                SessionId: 1,
                ActiveConsoleSessionId: 1,
                SessionMatchesConsole: true,
                WindowStation: "WinSta0",
                WindowStationVisible: true,
                ThreadDesktop: switched ? "Default" : "WorkerDesktop",
                InputDesktop: "Default",
                InputDesktopWin32Error: null,
                DesktopMatchesInput: switched,
                InJob: false,
                JobUiRestrictionsMask: 0,
                JobUiRestrictions: Array.Empty<string>(),
                JobQueryWin32Error: null,
                IsRestrictedToken: false,
                IsAppContainer: false,
                IntegrityLevel: "medium (0x2000)",
                ParentChain: Array.Empty<ProcessChainEntry>(),
                ThreadId: 4242);
        };
        InputContext.OverrideDesktopSwitch = ctx =>
            new DesktopSwitchAttempt(Attempted: true, Succeeded: true, Win32Error: null, TargetDesktop: ctx.InputDesktop);
        try
        {
            RecordingPointer pointer = new(new Point(100, 100));

            OperationResult? refusal = PointerDispatch.Click(
                Target, ClickStyle.Primary, out InputDispatchInfo? dispatch, settleMs: 0, driver: pointer);

            refusal.Should().BeNull();
            dispatch.Should().NotBeNull();
            dispatch!.ThreadId.Should().Be(4242);
            dispatch.ThreadDesktop.Should().Be("Default");
            dispatch.DesktopSwitchAttempted.Should().BeTrue();
            dispatch.DesktopSwitchSucceeded.Should().BeTrue();
            dispatch.DesktopSwitchWin32Error.Should().BeNull();
            dispatch.MoveMethod.Should().Be(PointerMoveMethods.SendInputVirtualDesk);
        }
        finally
        {
            InputContext.OverrideCapture = null;
            InputContext.OverrideDesktopSwitch = null;
        }
    }

    [Fact]
    public void Falls_back_to_first_working_method_when_primary_move_is_not_applied()
    {
        RecordingPointer pointer = new(new Point(100, 100))
        {
            ApplyMove = false,
            WorkingFallbackMethod = PointerMoveMethods.SetCursorPos,
        };

        OperationResult? refusal = PointerDispatch.Click(
            Target, ClickStyle.Primary, out InputDispatchInfo? dispatch, settleMs: 0, driver: pointer);

        refusal.Should().BeNull();
        dispatch.Should().NotBeNull();
        dispatch!.MoveMethod.Should().Be(PointerMoveMethods.SetCursorPos);
        pointer.Calls.Should().Contain($"fallback {PointerMoveMethods.SendInputPrimaryAbsolute} 400,300")
            .And.Contain($"fallback {PointerMoveMethods.SendInputRelative} 400,300")
            .And.Contain($"fallback {PointerMoveMethods.SetCursorPos} 400,300")
            .And.Contain("click Left");
    }

    [Fact]
    public void Reports_all_attempted_methods_when_primary_and_all_fallbacks_fail()
    {
        RecordingPointer pointer = new(new Point(100, 100)) { ApplyMove = false };

        OperationResult? refusal = PointerDispatch.Click(Target, ClickStyle.Primary, settleMs: 0, driver: pointer);

        refusal.Should().NotBeNull();
        refusal!.Error.Should().StartWith($"{PointerDispatch.MoveNotAppliedCode}:")
            .And.Contain(PointerMoveMethods.SendInputVirtualDesk)
            .And.Contain(PointerMoveMethods.SendInputPrimaryAbsolute)
            .And.Contain(PointerMoveMethods.SendInputRelative)
            .And.Contain(PointerMoveMethods.SetCursorPos);
    }

    private sealed class RecordingPointer : IPointerDriver
    {
        private Point _position;

        public RecordingPointer(Point? initial = null)
        {
            _position = initial ?? Point.Empty;
        }

        public List<string> Calls { get; } = new();

        public Size Drift { get; init; }

        public bool ApplyMove { get; init; } = true;

        public string? WorkingFallbackMethod { get; init; }

        public Point? PostMovePosition { get; init; }

        public PointerMoveResult? MoveOutcome { get; init; }

        public Point Position => _position + Drift;

        public PointerMoveResult MoveTo(Point point)
        {
            if (PostMovePosition is Point overridden)
            {
                _position = overridden;
            }
            else if (ApplyMove)
            {
                _position = point;
            }
            Calls.Add($"move {point.X},{point.Y}");
            return MoveOutcome ?? new PointerMoveResult(1, 0, point.X, point.Y, new VirtualDesktopBounds(0, 0, 1920, 1080));
        }

        public PointerMoveResult MoveToWithMethod(Point point, string method)
        {
            if (string.Equals(method, WorkingFallbackMethod, StringComparison.Ordinal))
            {
                _position = point;
            }
            Calls.Add($"fallback {method} {point.X},{point.Y}");
            return new PointerMoveResult(1, 0, point.X, point.Y, new VirtualDesktopBounds(0, 0, 1920, 1080), method);
        }

        public void Wait(int milliseconds) => Calls.Add($"wait {milliseconds}");

        public void Click(MouseButton button) => Calls.Add($"click {button}");

        public void DoubleClick(MouseButton button) => Calls.Add($"double {button}");

        public void Scroll(int notches, bool horizontal) =>
            Calls.Add($"{(horizontal ? "hscroll" : "scroll")} {notches}");
    }
}
