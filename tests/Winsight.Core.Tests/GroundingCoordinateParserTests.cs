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
using Winsight.Core.Modules.Vision;
using Xunit;

namespace Winsight.Core.Tests;

public class GroundingCoordinateParserTests
{
    [Fact]
    public void Parses_a_bare_point()
    {
        (double x, double y) = GroundingCoordinateParser.ParseCenter("[149, 205]");
        x.Should().Be(149);
        y.Should().Be(205);
    }

    [Fact]
    public void Collapses_a_box_to_its_center()
    {
        (double x, double y) = GroundingCoordinateParser.ParseCenter("[147, 205, 195, 220]");
        x.Should().Be(171);
        y.Should().Be(212.5);
    }

    [Fact]
    public void Extracts_coordinates_wrapped_in_prose()
    {
        (double x, double y) = GroundingCoordinateParser.ParseCenter(
            "The increment button is located at [10, 20] in the screenshot.");
        x.Should().Be(10);
        y.Should().Be(20);
    }

    [Fact]
    public void Parses_floating_point_coordinates()
    {
        (double x, double y) = GroundingCoordinateParser.ParseCenter("[1.5, 2.5]");
        x.Should().Be(1.5);
        y.Should().Be(2.5);
    }

    [Fact]
    public void Uses_the_first_coordinate_list_when_several_are_present()
    {
        (double x, double y) = GroundingCoordinateParser.ParseCenter("[3, 4] then [9, 9]");
        x.Should().Be(3);
        y.Should().Be(4);
    }

    [Fact]
    public void Throws_when_no_coordinate_list_is_present()
    {
        Action act = () => GroundingCoordinateParser.ParseCenter("I cannot find the element.");
        act.Should().Throw<FormatException>();
    }
}
