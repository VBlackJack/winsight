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

public sealed class VirtualDesktopCoordinatesTests
{
    [Fact]
    public void Normalizes_corners_of_a_single_monitor()
    {
        VirtualDesktopBounds desktop = new(0, 0, 1920, 1080);

        VirtualDesktopCoordinates.Normalize(0, 0, desktop)
            .Should().Be(new NormalizedAbsolutePoint(0, 0));
        VirtualDesktopCoordinates.Normalize(1919, 1079, desktop)
            .Should().Be(new NormalizedAbsolutePoint(65535, 65535));
    }

    [Fact]
    public void Normalizes_ultrawide_primary_plus_right_monitor_virtual_desktop()
    {
        // Matches field-test host: 3440x1440 primary + 2560x1440 monitor to the right (6000x1440 virtual desktop).
        VirtualDesktopBounds desktop = new(0, 0, 6000, 1440);

        // Field-test point (1857, 44) on primary monitor.
        NormalizedAbsolutePoint primaryPoint = VirtualDesktopCoordinates.Normalize(1857, 44, desktop);
        primaryPoint.Dx.Should().Be((int)Math.Round(1857 * 65535.0 / 5999.0, MidpointRounding.AwayFromZero));
        primaryPoint.Dy.Should().Be((int)Math.Round(44 * 65535.0 / 1439.0, MidpointRounding.AwayFromZero));

        // Point on the secondary monitor to the right (x > 3440).
        NormalizedAbsolutePoint rightMonitorPoint = VirtualDesktopCoordinates.Normalize(4500, 720, desktop);
        rightMonitorPoint.Dx.Should().Be((int)Math.Round(4500 * 65535.0 / 5999.0, MidpointRounding.AwayFromZero));
        rightMonitorPoint.Dx.Should().BeGreaterThan(37000).And.BeLessThan(65535);
        rightMonitorPoint.Dy.Should().Be((int)Math.Round(720 * 65535.0 / 1439.0, MidpointRounding.AwayFromZero));

        // Far right edge of second monitor maps to 65535.
        VirtualDesktopCoordinates.Normalize(5999, 1439, desktop)
            .Should().Be(new NormalizedAbsolutePoint(65535, 65535));
    }

    [Fact]
    public void Normalizes_multi_monitor_layout_with_negative_virtual_origin()
    {
        // Secondary 1920x1080 monitor to the left of a 3440x1440 primary monitor.
        VirtualDesktopBounds desktop = new(-1920, -200, 5360, 1640);

        VirtualDesktopCoordinates.Normalize(-1920, -200, desktop)
            .Should().Be(new NormalizedAbsolutePoint(0, 0));
        VirtualDesktopCoordinates.Normalize(3439, 1439, desktop)
            .Should().Be(new NormalizedAbsolutePoint(65535, 65535));

        // Primary monitor origin (0, 0) lies inside the virtual desktop, not at 0.
        NormalizedAbsolutePoint primaryOrigin = VirtualDesktopCoordinates.Normalize(0, 0, desktop);
        primaryOrigin.Dx.Should().Be((int)Math.Round(1920 * 65535.0 / 5359.0, MidpointRounding.AwayFromZero));
        primaryOrigin.Dy.Should().Be((int)Math.Round(200 * 65535.0 / 1639.0, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Clamps_out_of_bounds_coordinates_to_valid_absolute_range()
    {
        VirtualDesktopBounds desktop = new(0, 0, 6000, 1440);

        VirtualDesktopCoordinates.Normalize(-500, -100, desktop)
            .Should().Be(new NormalizedAbsolutePoint(0, 0));
        VirtualDesktopCoordinates.Normalize(9000, 3000, desktop)
            .Should().Be(new NormalizedAbsolutePoint(65535, 65535));
    }

    [Fact]
    public void Rejects_non_positive_virtual_desktop_dimensions()
    {
        Action invalid = () => VirtualDesktopCoordinates.Normalize(10, 10, new VirtualDesktopBounds(0, 0, 0, 1440));
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }
}
