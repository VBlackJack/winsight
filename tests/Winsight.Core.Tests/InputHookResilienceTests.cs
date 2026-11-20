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

public sealed class InputHookResilienceTests
{
    private const uint StartTick = 200_000;

    [Fact]
    public void Disabled_hook_reports_inactive_without_warning()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(StartTick, StartTick - 1_000)
        {
            HookState = default,
        };
        UserActivityMonitor monitor = new(clock);

        UserActivitySnapshot snap = monitor.Read();
        snap.Diagnostics!.InputHookActive.Should().BeFalse();
        snap.Diagnostics.InputHookWarning.Should().BeNull();
    }

    [Fact]
    public void Enabled_hook_that_failed_to_install_reports_inactive_with_warning()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(StartTick, StartTick - 1_000)
        {
            HookState = new HookInputState(
                Active: false,
                HasEvent: false,
                LastEventTick: 0,
                LastInputKind: UserActivityMonitor.KindUnknown,
                LastEventInjected: false,
                HasHardwareEvent: false,
                LastHardwareTick: 0,
                LastHardwareKind: UserActivityMonitor.KindUnknown,
                Enabled: true),
        };
        UserActivityMonitor monitor = new(clock);

        UserActivitySnapshot snap = monitor.Read();
        snap.Diagnostics!.InputHookActive.Should().BeFalse();
        snap.Diagnostics.InputHookWarning.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Stale_heartbeat_reports_inputHookActive_false_and_warning()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(StartTick, StartTick - 1_000)
        {
            HookState = new HookInputState(
                Active: true,
                HasEvent: true,
                LastEventTick: StartTick - 1_000,
                LastInputKind: UserActivityMonitor.KindMouse,
                LastEventInjected: false,
                HasHardwareEvent: true,
                LastHardwareTick: StartTick - 1_000,
                LastHardwareKind: UserActivityMonitor.KindMouse,
                Enabled: true,
                HasHeartbeat: true,
                LastHeartbeatTick: StartTick - InputHookHealthTracker.HeartbeatStaleMs - 500),
        };
        UserActivityMonitor monitor = new(clock);

        UserActivitySnapshot snap = monitor.Read();
        snap.Diagnostics!.InputHookActive.Should().BeFalse();
        snap.Diagnostics.InputHookWarning.Should().Contain("LowLevelHooksTimeout");
    }

    [Fact]
    public void Silent_os_removal_detected_after_missed_cursor_moving_inputs()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(StartTick, StartTick)
        {
            Cursor = new CursorPoint(100, 100),
            HookState = new HookInputState(
                Active: true,
                HasEvent: true,
                LastEventTick: StartTick,
                LastInputKind: UserActivityMonitor.KindMouse,
                LastEventInjected: false,
                HasHardwareEvent: true,
                LastHardwareTick: StartTick,
                LastHardwareKind: UserActivityMonitor.KindMouse,
                Enabled: true,
                HasHeartbeat: true,
                LastHeartbeatTick: StartTick),
        };
        UserActivityMonitor monitor = new(clock);

        // Healthy initial sample
        UserActivitySnapshot healthy = monitor.Read();
        healthy.Diagnostics!.InputHookActive.Should().BeTrue();
        healthy.Diagnostics.InputHookWarning.Should().BeNull();

        // Now Windows silently removes the hook: GetLastInputInfo and cursor advance,
        // pump heartbeat still ticks, but hook.LastEventTick stops updating.
        clock.Sleep(500);
        clock.Cursor = new CursorPoint(150, 120);
        clock.RecordExternalInput();
        clock.HookState = clock.HookState with { LastHeartbeatTick = clock.TickNow() };
        UserActivitySnapshot miss1 = monitor.Read();
        miss1.Diagnostics!.InputHookActive.Should().BeTrue("a single missed event could be non-hook HID input");

        clock.Sleep(500);
        clock.Cursor = new CursorPoint(220, 160);
        clock.RecordExternalInput();
        clock.HookState = clock.HookState with { LastHeartbeatTick = clock.TickNow() };
        UserActivitySnapshot miss2 = monitor.Read();
        miss2.Diagnostics!.InputHookActive.Should().BeFalse();
        miss2.Diagnostics.InputHookWarning.Should().Contain("silently removed").And.Contain("LowLevelHooksTimeout");

        // If hook starts receiving matching events again, it recovers cleanly.
        clock.Sleep(500);
        clock.Cursor = new CursorPoint(260, 180);
        clock.RecordExternalInput();
        clock.HookState = clock.HookState with
        {
            LastEventTick = clock.TickNow(),
            LastHardwareTick = clock.TickNow(),
            LastHeartbeatTick = clock.TickNow(),
        };
        UserActivitySnapshot recovered = monitor.Read();
        recovered.Diagnostics!.InputHookActive.Should().BeTrue();
        recovered.Diagnostics.InputHookWarning.Should().BeNull();
    }
}
