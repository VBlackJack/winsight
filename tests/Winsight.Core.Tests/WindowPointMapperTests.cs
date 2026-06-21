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
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public sealed class WindowPointMapperTests
{
    [Theory]
    [InlineData(0, 0, 10, 20, 10, 20)]
    [InlineData(750, 400, 96, 32, 846, 432)]
    [InlineData(-30, -15, 5, 5, -25, -10)]
    public void ToScreen_AddsWindowOriginToWindowRelativePoint(
        int windowLeft, int windowTop, int x, int y, int expectedScreenX, int expectedScreenY)
    {
        (int screenX, int screenY) = WindowPointMapper.ToScreen(windowLeft, windowTop, x, y);

        screenX.Should().Be(expectedScreenX);
        screenY.Should().Be(expectedScreenY);
    }

    [Theory]
    [InlineData(100, 50, 0, 0)]      // top-left corner is inside
    [InlineData(100, 50, 99, 49)]    // last addressable pixel is inside
    [InlineData(100, 50, 50, 25)]    // center is inside
    public void Contains_ReturnsTrue_ForPointsWithinBounds(int width, int height, int x, int y)
    {
        WindowPointMapper.Contains(width, height, x, y).Should().BeTrue();
    }

    [Theory]
    [InlineData(100, 50, -1, 0)]     // negative x
    [InlineData(100, 50, 0, -1)]     // negative y
    [InlineData(100, 50, 100, 25)]   // right edge is exclusive
    [InlineData(100, 50, 50, 50)]    // bottom edge is exclusive
    public void Contains_ReturnsFalse_ForPointsOutsideBounds(int width, int height, int x, int y)
    {
        WindowPointMapper.Contains(width, height, x, y).Should().BeFalse();
    }
}
