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
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public class DragPathTests
{
    private const int Threshold = 4;

    [Fact]
    public void First_move_leaves_the_drag_rectangle_towards_the_target()
    {
        IReadOnlyList<Point> moves = DragPath.Plan(new Point(100, 100), new Point(300, 100), 10, Threshold);

        moves[0].X.Should().BeGreaterThan(100 + Threshold);
        moves[0].Y.Should().Be(100);
    }

    [Fact]
    public void Path_ends_on_the_target_after_a_one_pixel_settle()
    {
        Point target = new(300, 220);
        IReadOnlyList<Point> moves = DragPath.Plan(new Point(100, 100), target, 10, Threshold);

        moves[^1].Should().Be(target);
        moves[^2].Should().NotBe(target);
        Math.Abs(moves[^2].X - target.X).Should().Be(1);
    }

    [Fact]
    public void Intermediate_moves_follow_the_requested_step_count()
    {
        IReadOnlyList<Point> moves = DragPath.Plan(new Point(0, 0), new Point(500, 0), 20, Threshold);

        // threshold exit + 20 steps + overshoot + final.
        moves.Should().HaveCount(23);
        moves.Skip(1).Take(20).Select(p => p.X).Should().BeInAscendingOrder();
    }

    [Fact]
    public void A_drop_on_the_press_point_still_leaves_the_drag_rectangle()
    {
        Point point = new(50, 50);
        IReadOnlyList<Point> moves = DragPath.Plan(point, point, 3, Threshold);

        moves[0].Should().NotBe(point);
        moves[^1].Should().Be(point);
    }

    [Fact]
    public void Endpoint_needs_exactly_one_of_locator_or_point()
    {
        new DragEndpoint().Validate("source").Should().Contain("exactly one");
        new DragEndpoint(new ElementLocator(Name: "a"), 1, 2).Validate("source").Should().Contain("exactly one");
        DragEndpoint.ForPoint(1, 2).Validate("source").Should().BeNull();
        DragEndpoint.ForElement(new ElementLocator(Name: "a")).Validate("source").Should().BeNull();
    }

    [Fact]
    public void Options_reject_out_of_range_pacing()
    {
        new DragOptions(Steps: 0).Validate().Should().Contain("steps");
        new DragOptions(HoldMs: -1).Validate().Should().Contain("holdMs");
        new DragOptions().Validate().Should().BeNull();
    }
}
