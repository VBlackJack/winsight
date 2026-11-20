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
using Winsight.Core.Modules.UserActivity;
using Xunit;

namespace Winsight.Core.Tests;

public sealed class PeriodicMicroMoveTests
{
    private const uint StartTick = 100_000;

    [Fact]
    public void Jiggler_with_2400ms_period_is_ignored_and_does_not_reset_humanIdleMs()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(StartTick, StartTick)
        {
            Cursor = new CursorPoint(500, 500),
        };
        UserActivityMonitor monitor = new(clock, () => new PeriodicMicroMoveConfig(Enabled: true, MaxDisplacementPx: 15));

        // Initial baseline at t=0
        monitor.Read().LastInputSource.Should().Be(UserActivityMonitor.SourceHuman);

        // 2.4s jiggler alternating +/-2 px: first 3 ticks establish the periodic pattern
        for (int i = 1; i <= 3; i++)
        {
            clock.Sleep(2400);
            clock.Cursor = new CursorPoint(500 + (i % 2 == 0 ? 0 : 2), 500);
            clock.RecordExternalInput();
            monitor.Read();
        }

        // 4th jiggler tick (t = 9600): 3 regular 2400 ms intervals => classified as periodic_micro_move
        clock.Sleep(2400);
        clock.Cursor = new CursorPoint(500, 500);
        clock.RecordExternalInput();
        UserActivitySnapshot fourth = monitor.Read();

        fourth.LastInputSource.Should().Be(UserActivityMonitor.SourcePeriodicMicroMove);
        fourth.HumanIdleMs.Should().Be(2400);
        fourth.Diagnostics.Should().NotBeNull();
        fourth.Diagnostics!.PeriodicMicroMoveFilterEnabled.Should().BeTrue();
        fourth.Diagnostics.PeriodicMicroMoveThresholdPx.Should().Be(15);
        fourth.Diagnostics.LastInputIgnoredAsPeriodicMicroMove.Should().BeTrue();

        // 5th jiggler tick (t = 12000): humanIdleMs continues to grow to 4800 ms
        clock.Sleep(2400);
        clock.Cursor = new CursorPoint(502, 500);
        clock.RecordExternalInput();
        UserActivitySnapshot fifth = monitor.Read();

        fifth.LastInputSource.Should().Be(UserActivityMonitor.SourcePeriodicMicroMove);
        fifth.HumanIdleMs.Should().Be(4800);
    }

    [Fact]
    public void Jiggler_is_not_ignored_when_filter_is_disabled()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(StartTick, StartTick)
        {
            Cursor = new CursorPoint(500, 500),
        };
        UserActivityMonitor monitor = new(clock, () => new PeriodicMicroMoveConfig(Enabled: false));

        monitor.Read();
        for (int i = 1; i <= 5; i++)
        {
            clock.Sleep(2400);
            clock.Cursor = new CursorPoint(500 + (i % 2 == 0 ? 0 : 2), 500);
            clock.RecordExternalInput();
            UserActivitySnapshot snap = monitor.Read();
            snap.LastInputSource.Should().Be(UserActivityMonitor.SourceHuman);
            snap.HumanIdleMs.Should().Be(0);
            snap.Diagnostics!.PeriodicMicroMoveFilterEnabled.Should().BeFalse();
            snap.Diagnostics.LastInputIgnoredAsPeriodicMicroMove.Should().BeFalse();
        }
    }

    [Fact]
    public void Human_moving_mouse_beyond_threshold_counts_as_human_and_resets_idle()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(StartTick, StartTick)
        {
            Cursor = new CursorPoint(500, 500),
        };
        UserActivityMonitor monitor = new(clock, () => new PeriodicMicroMoveConfig(Enabled: true, MaxDisplacementPx: 15));

        monitor.Read();
        for (int i = 1; i <= 4; i++)
        {
            clock.Sleep(2400);
            clock.Cursor = new CursorPoint(500 + (i % 2 == 0 ? 0 : 2), 500);
            clock.RecordExternalInput();
            monitor.Read();
        }

        // Human moves mouse by 40 px
        clock.Sleep(2400);
        clock.Cursor = new CursorPoint(540, 500);
        clock.RecordExternalInput();
        UserActivitySnapshot snap = monitor.Read();

        snap.LastInputSource.Should().Be(UserActivityMonitor.SourceHuman);
        snap.HumanIdleMs.Should().Be(0);
        snap.Diagnostics!.LastInputIgnoredAsPeriodicMicroMove.Should().BeFalse();
    }

    [Fact]
    public void Human_moving_mouse_continuously_with_small_steps_counts_as_human()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(StartTick, StartTick)
        {
            Cursor = new CursorPoint(500, 500),
        };
        UserActivityMonitor monitor = new(clock, () => new PeriodicMicroMoveConfig(Enabled: true, MaxDisplacementPx: 15));

        monitor.Read();
        for (int i = 1; i <= 8; i++)
        {
            clock.Sleep(50);
            clock.Cursor = new CursorPoint(500 + i * 3, 500);
            clock.RecordExternalInput();
            UserActivitySnapshot snap = monitor.Read();
            snap.LastInputSource.Should().Be(UserActivityMonitor.SourceHuman);
            snap.HumanIdleMs.Should().Be(0);
        }
    }

    [Fact]
    public void Human_moving_mouse_irregularly_with_small_steps_counts_as_human()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(StartTick, StartTick)
        {
            Cursor = new CursorPoint(500, 500),
        };
        UserActivityMonitor monitor = new(clock, () => new PeriodicMicroMoveConfig(Enabled: true, MaxDisplacementPx: 15));

        monitor.Read();
        int[] irregularIntervals = [400, 1800, 700, 3100, 500];
        for (int i = 0; i < irregularIntervals.Length; i++)
        {
            clock.Sleep(irregularIntervals[i]);
            clock.Cursor = new CursorPoint(500 + ((i + 1) * 3), 500);
            clock.RecordExternalInput();
            UserActivitySnapshot snap = monitor.Read();
            snap.LastInputSource.Should().Be(UserActivityMonitor.SourceHuman);
            snap.HumanIdleMs.Should().Be(0);
        }
    }

    [Fact]
    public void Keyboard_input_and_mouse_click_always_count_as_human_even_with_regular_timing()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(StartTick, StartTick)
        {
            Cursor = new CursorPoint(500, 500),
        };
        UserActivityMonitor monitor = new(clock, () => new PeriodicMicroMoveConfig(Enabled: true, MaxDisplacementPx: 15));

        monitor.Read();

        // Establish jiggler streak with hook active
        for (int i = 1; i <= 4; i++)
        {
            clock.Sleep(2400);
            clock.Cursor = new CursorPoint(500 + (i % 2 == 0 ? 0 : 2), 500);
            clock.RecordExternalInput();
            clock.HookState = new HookInputState(
                Active: true,
                HasEvent: true,
                LastEventTick: clock.TickNow(),
                LastInputKind: UserActivityMonitor.KindMouse,
                LastEventInjected: false,
                HasHardwareEvent: true,
                LastHardwareTick: clock.TickNow(),
                LastHardwareKind: UserActivityMonitor.KindMouse,
                LastMouseWasMoveOnly: true);
            monitor.Read();
        }

        // Mouse click at exact same 2400ms period and 1px move => must count as human
        clock.Sleep(2400);
        clock.Cursor = new CursorPoint(501, 500);
        clock.RecordExternalInput();
        clock.HookState = new HookInputState(
            Active: true,
            HasEvent: true,
            LastEventTick: clock.TickNow(),
            LastInputKind: UserActivityMonitor.KindMouse,
            LastEventInjected: false,
            HasHardwareEvent: true,
            LastHardwareTick: clock.TickNow(),
            LastHardwareKind: UserActivityMonitor.KindMouse,
            LastMouseWasMoveOnly: false);
        UserActivitySnapshot clickSnap = monitor.Read();
        clickSnap.LastInputSource.Should().Be(UserActivityMonitor.SourceHuman);
        clickSnap.HumanIdleMs.Should().Be(0);

        // Keyboard press => must count as human
        clock.Sleep(2400);
        clock.RecordExternalInput();
        clock.HookState = new HookInputState(
            Active: true,
            HasEvent: true,
            LastEventTick: clock.TickNow(),
            LastInputKind: UserActivityMonitor.KindKeyboard,
            LastEventInjected: false,
            HasHardwareEvent: true,
            LastHardwareTick: clock.TickNow(),
            LastHardwareKind: UserActivityMonitor.KindKeyboard,
            LastMouseWasMoveOnly: false);
        UserActivitySnapshot keySnap = monitor.Read();
        keySnap.LastInputSource.Should().Be(UserActivityMonitor.SourceHuman);
        keySnap.HumanIdleMs.Should().Be(0);
    }

    [Theory]
    [InlineData(null, UserActivityPolicy.DefaultPeriodicMicroMoveMaxPx)]
    [InlineData("", UserActivityPolicy.DefaultPeriodicMicroMoveMaxPx)]
    [InlineData("0", UserActivityPolicy.DefaultPeriodicMicroMoveMaxPx)]
    [InlineData("-5", UserActivityPolicy.DefaultPeriodicMicroMoveMaxPx)]
    [InlineData("500", UserActivityPolicy.DefaultPeriodicMicroMoveMaxPx)]
    [InlineData("1", 1)]
    [InlineData("25", 25)]
    [InlineData("200", 200)]
    public void ParsePeriodicMicroMoveMaxPx_validates_threshold(string? raw, int expected)
    {
        UserActivityPolicy.ParsePeriodicMicroMoveMaxPx(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, UserActivityPolicy.DefaultPeriodicMicroMoveTolerancePct)]
    [InlineData("", UserActivityPolicy.DefaultPeriodicMicroMoveTolerancePct)]
    [InlineData("1", UserActivityPolicy.DefaultPeriodicMicroMoveTolerancePct)]
    [InlineData("80", UserActivityPolicy.DefaultPeriodicMicroMoveTolerancePct)]
    [InlineData("5", 5)]
    [InlineData("25", 25)]
    [InlineData("50", 50)]
    public void ParsePeriodicMicroMoveTolerancePct_validates_tolerance(string? raw, int expected)
    {
        UserActivityPolicy.ParsePeriodicMicroMoveTolerancePct(raw).Should().Be(expected);
    }

    [Fact]
    public void Jiggler_running_then_Winsight_click_classifies_next_jiggler_move_immediately_as_periodic_micro_move()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(StartTick, StartTick)
        {
            Cursor = new CursorPoint(500, 500),
        };
        UserActivityMonitor monitor = new(clock, () => new PeriodicMicroMoveConfig(Enabled: true, MaxDisplacementPx: 15));

        monitor.Read();

        // Learn the 2400 ms jiggler pattern
        for (int i = 1; i <= 4; i++)
        {
            clock.Sleep(2400);
            clock.Cursor = new CursorPoint(500 + (i % 2 == 0 ? 0 : 2), 500);
            clock.RecordExternalInput();
            monitor.Read();
        }

        // Winsight moves cursor far away to (180, 220) and clicks (takes 200 ms)
        uint injectStart = clock.TickNow() + 400;
        clock.Sleep(600);
        clock.Cursor = new CursorPoint(180, 220);
        clock.RecordWinsightInjection(injectStart, clock.TickNow());
        UserActivitySnapshot winsightSnap = monitor.Read();
        winsightSnap.LastInputSource.Should().Be(UserActivityMonitor.SourceWinsight);

        // Next jiggler move arrives 1800 ms later (2400 ms since previous jiggler tick) at (182, 220)
        clock.Sleep(1800);
        clock.Cursor = new CursorPoint(182, 220);
        clock.RecordExternalInput();
        UserActivitySnapshot afterClick = monitor.Read();

        afterClick.LastInputSource.Should().Be(UserActivityMonitor.SourcePeriodicMicroMove);
        afterClick.Diagnostics!.LastInputIgnoredAsPeriodicMicroMove.Should().BeTrue();
        afterClick.HumanIdleMs.Should().Be(4800, "neither the Winsight click nor the next jiggler move should reset humanIdleMs");
    }

    [Fact]
    public void Jiggler_resumes_immediately_after_human_move_on_first_matching_micro_move()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(StartTick, StartTick)
        {
            Cursor = new CursorPoint(500, 500),
        };
        UserActivityMonitor monitor = new(clock, () => new PeriodicMicroMoveConfig(Enabled: true, MaxDisplacementPx: 15));

        monitor.Read();
        for (int i = 1; i <= 4; i++)
        {
            clock.Sleep(2400);
            clock.Cursor = new CursorPoint(500 + (i % 2 == 0 ? 0 : 2), 500);
            clock.RecordExternalInput();
            monitor.Read();
        }

        // Human moves cursor 120 px at +600 ms
        clock.Sleep(600);
        clock.Cursor = new CursorPoint(620, 500);
        clock.RecordExternalInput();
        UserActivitySnapshot humanSnap = monitor.Read();
        humanSnap.LastInputSource.Should().Be(UserActivityMonitor.SourceHuman);
        humanSnap.HumanIdleMs.Should().Be(0);

        // Next jiggler tick arrives 1800 ms later (2400 ms since previous jiggler tick) with 2 px move
        clock.Sleep(1800);
        clock.Cursor = new CursorPoint(622, 500);
        clock.RecordExternalInput();
        UserActivitySnapshot resumedSnap = monitor.Read();

        resumedSnap.LastInputSource.Should().Be(UserActivityMonitor.SourcePeriodicMicroMove);
        resumedSnap.HumanIdleMs.Should().Be(1800, "humanIdleMs stays anchored at the human move, not reset by the jiggler");
    }

    [Fact]
    public void Jiggler_with_alternating_2047_and_2406ms_intervals_is_recognized()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(StartTick, StartTick)
        {
            Cursor = new CursorPoint(400, 400),
        };
        UserActivityMonitor monitor = new(clock, () => new PeriodicMicroMoveConfig(Enabled: true, MaxDisplacementPx: 15));

        monitor.Read();
        int[] intervals = [2047, 2406, 2047, 2406, 2047];
        UserActivitySnapshot? last = null;
        for (int i = 0; i < intervals.Length; i++)
        {
            clock.Sleep(intervals[i]);
            clock.Cursor = new CursorPoint(400 + (i % 2 == 0 ? 2 : 0), 400);
            clock.RecordExternalInput();
            last = monitor.Read();
        }

        last!.LastInputSource.Should().Be(UserActivityMonitor.SourcePeriodicMicroMove);
        last.Diagnostics!.LastInputIgnoredAsPeriodicMicroMove.Should().BeTrue();
        last.HumanIdleMs.Should().BeGreaterThan(0);
    }
}
