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

    private sealed class RecordingPointer : IPointerDriver
    {
        private Point _position;

        public List<string> Calls { get; } = new();

        public Size Drift { get; init; }

        public Point Position => _position + Drift;

        public void MoveTo(Point point)
        {
            _position = point;
            Calls.Add($"move {point.X},{point.Y}");
        }

        public void Wait(int milliseconds) => Calls.Add($"wait {milliseconds}");

        public void Click(MouseButton button) => Calls.Add($"click {button}");

        public void DoubleClick(MouseButton button) => Calls.Add($"double {button}");

        public void Scroll(int notches, bool horizontal) =>
            Calls.Add($"{(horizontal ? "hscroll" : "scroll")} {notches}");
    }
}
