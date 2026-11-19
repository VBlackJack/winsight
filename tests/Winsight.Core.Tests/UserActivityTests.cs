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

using System.Runtime.InteropServices;
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.UserActivity;
using Xunit;

namespace Winsight.Core.Tests;

/// <summary>
/// Telling Winsight's input from a person's by time alone, and the yield
/// policy built on it. Ticks are the 32-bit counter of GetLastInputInfo.
/// </summary>
public sealed class UserActivityTests
{
    private const uint Now = 1_000_000;

    private static LedgerState Injection(uint start, uint end, bool active = false, uint? human = null) =>
        new(true, active, start, end, human is not null, human ?? 0);

    [Fact]
    public void Without_any_injection_the_last_input_is_human()
    {
        InputAttribution attribution = InputAttributor.Attribute(Now, Now - 300, default);

        attribution.LastInputIsWinsight.Should().BeFalse();
        attribution.HumanTick.Should().Be(Now - 300);
    }

    [Fact]
    public void Input_inside_the_last_injection_window_is_winsight_and_keeps_the_previous_human_tick()
    {
        LedgerState ledger = Injection(start: Now - 900, end: Now - 500, human: Now - 5_000);

        InputAttribution attribution = InputAttributor.Attribute(Now, Now - 520, ledger);

        attribution.LastInputIsWinsight.Should().BeTrue();
        attribution.HumanTick.Should().Be(Now - 5_000);
    }

    [Fact]
    public void Input_stamped_within_the_margin_after_the_injection_is_still_winsight()
    {
        LedgerState ledger = Injection(start: Now - 900, end: Now - 500);

        InputAttributor.Attribute(Now, Now - 500 + InputAttributor.InjectionMarginMs, ledger)
            .LastInputIsWinsight.Should().BeTrue();
    }

    [Fact]
    public void Input_after_the_injection_ended_is_human()
    {
        LedgerState ledger = Injection(start: Now - 900, end: Now - 500);

        InputAttribution attribution = InputAttributor.Attribute(Now, Now - 200, ledger);

        attribution.LastInputIsWinsight.Should().BeFalse();
        attribution.HumanTick.Should().Be(Now - 200);
    }

    [Fact]
    public void Input_before_the_injection_started_is_human()
    {
        LedgerState ledger = Injection(start: Now - 900, end: Now - 500);

        InputAttributor.Attribute(Now, Now - 2_000, ledger).LastInputIsWinsight.Should().BeFalse();
    }

    [Fact]
    public void Input_during_an_injection_in_progress_is_winsight()
    {
        LedgerState ledger = Injection(start: Now - 300, end: Now - 10_000, active: true);

        InputAttributor.Attribute(Now, Now - 10, ledger).LastInputIsWinsight.Should().BeTrue();
    }

    [Fact]
    public void An_injection_left_active_by_a_crashed_holder_stops_masking_input()
    {
        uint start = Now - InputAttributor.StaleInjectionMs - 1_000;
        LedgerState ledger = Injection(start, end: start - 1, active: true);

        InputAttributor.Attribute(Now, Now - 10, ledger).LastInputIsWinsight.Should().BeFalse();
    }

    [Fact]
    public void Attribution_survives_the_tick_counter_wrap()
    {
        const uint wrappedNow = 200;
        uint start = unchecked(wrappedNow - 600);
        uint end = unchecked(wrappedNow - 400);

        InputAttributor.AgeMs(wrappedNow, start).Should().Be(600);
        InputAttributor.Attribute(wrappedNow, unchecked(wrappedNow - 450), Injection(start, end))
            .LastInputIsWinsight.Should().BeTrue();
        InputAttributor.Attribute(wrappedNow, wrappedNow - 100, Injection(start, end))
            .LastInputIsWinsight.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("0", null)]
    [InlineData("-5", null)]
    [InlineData("soon", null)]
    [InlineData("1500", 1500)]
    public void Yield_threshold_is_a_positive_integer_or_disabled(string? raw, int? expected)
    {
        UserActivityPolicy.Parse(raw).Should().Be(expected);
    }

    [Fact]
    public void A_disabled_policy_never_reads_activity()
    {
        bool read = false;

        UserActivityPolicy.CheckYield(null, () =>
        {
            read = true;
            return 0;
        }).Should().BeNull();
        read.Should().BeFalse();
    }

    [Fact]
    public void Recent_human_input_refuses_with_user_active()
    {
        string? refusal = UserActivityPolicy.CheckYield(2_000, () => 350);

        refusal.Should().StartWith("user_active:")
            .And.Contain("350 ms")
            .And.Contain($"{UserActivityPolicy.YieldToUserVariable}=2000")
            .And.Contain("wait_for_user_idle");
    }

    [Theory]
    [InlineData(2_000L)]
    [InlineData(9_000L)]
    [InlineData(null)]
    public void Idle_long_enough_or_unknown_lets_the_action_proceed(long? humanIdleMs)
    {
        UserActivityPolicy.CheckYield(2_000, () => humanIdleMs).Should().BeNull();
    }

    [Fact]
    public void WaitForIdle_with_continuous_activity_polls_until_full_timeoutMs_elapses()
    {
        // Simulates phantom or real human input every 500 ms with idleMs=5000 and timeoutMs=60000.
        FakeUserActivityBackend clock = new(startTick: Now, lastInputTick: Now);
        clock.OnAdvance = elapsed =>
        {
            if (elapsed % 500 == 0)
            {
                clock.RecordExternalInput();
            }
        };
        UserActivityMonitor monitor = new(clock);

        OperationResult result = monitor.WaitForIdle(idleMs: 5_000, timeoutMs: 60_000);

        result.Success.Should().BeFalse();
        result.Error.Should().StartWith("timed_out:").And.Contain("after 60000 ms");
        UserIdleWaitResult wait = result.Data.Should().BeOfType<UserIdleWaitResult>().Subject;
        wait.Idle.Should().BeFalse();
        wait.WaitedMs.Should().Be(60_000);
        wait.IdleMs.Should().Be(5_000);
        clock.SleepCalls.Should().BeGreaterThan(500);
    }

    [Fact]
    public void WaitForIdle_succeeds_once_quiet_period_is_reached_after_activity_stops()
    {
        FakeUserActivityBackend clock = new(startTick: Now, lastInputTick: Now);
        clock.OnAdvance = elapsed =>
        {
            // User keeps interacting during the first 2000 ms, then stops.
            if (elapsed <= 2_000 && elapsed % 400 == 0)
            {
                clock.RecordExternalInput();
            }
        };
        UserActivityMonitor monitor = new(clock);

        OperationResult result = monitor.WaitForIdle(idleMs: 5_000, timeoutMs: 60_000);

        result.Success.Should().BeTrue();
        UserIdleWaitResult wait = result.Data.Should().BeOfType<UserIdleWaitResult>().Subject;
        wait.Idle.Should().BeTrue();
        wait.WaitedMs.Should().Be(7_000);
        wait.Activity.HumanIdleMs.Should().Be(5_000);
    }

    [Fact]
    public async Task WaitForIdleAsync_reports_total_waitedMs_and_progress_across_full_timeout()
    {
        FakeUserActivityBackend clock = new(startTick: Now, lastInputTick: Now);
        clock.OnAdvance = elapsed =>
        {
            if (elapsed % 500 == 0)
            {
                clock.RecordExternalInput();
            }
        };
        UserActivityMonitor monitor = new(clock);
        List<(long Waited, int Total, bool? Completed)> reports = new();

        OperationResult result = await monitor.WaitForIdleAsync(
            idleMs: 5_000,
            timeoutMs: 60_000,
            onProgress: (waited, total, completed) => reports.Add((waited, total, completed)),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().StartWith("timed_out:").And.Contain("after 60000 ms");
        UserIdleWaitResult wait = result.Data.Should().BeOfType<UserIdleWaitResult>().Subject;
        wait.Idle.Should().BeFalse();
        wait.WaitedMs.Should().Be(60_000);
        reports.Should().HaveCountGreaterThanOrEqualTo(59);
        reports[^1].Should().Be((60_000, 60_000, false));
    }

    [Fact]
    public async Task WaitForIdleAsync_validates_bounds_and_honors_cancellation()
    {
        FakeUserActivityBackend clock = new(startTick: Now, lastInputTick: Now);
        UserActivityMonitor monitor = new(clock);

        OperationResult badTimeout = await monitor.WaitForIdleAsync(
            idleMs: 1_000,
            timeoutMs: UserActivityMonitor.MaxWaitTimeoutMs + 1,
            cancellationToken: TestContext.Current.CancellationToken);
        badTimeout.Success.Should().BeFalse();
        badTimeout.Error.Should().Contain("timeoutMs");

        using CancellationTokenSource cts = new();
        cts.Cancel();
        Func<Task> cancelled = () => monitor.WaitForIdleAsync(
            idleMs: 5_000,
            timeoutMs: 60_000,
            cancellationToken: cts.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void WtsSessionFlagsOffset_accounts_for_8_byte_alignment_of_WTSINFOEX_LEVEL1()
    {
        // WTSINFOEXW: DWORD Level (0..3) + 4-byte pad (4..7) + SessionId (8..11) +
        // SessionState (12..15) + SessionFlags (16..19).
        UserActivityMonitor.WtsSessionFlagsOffset.Should().Be(16);
    }

    [Theory]
    [InlineData(1, 0, 1, false)]
    [InlineData(1, 0, 0, true)]
    [InlineData(1, 0, -1, null)]
    [InlineData(2, 0, 1, null)]
    public void ParseWtsSessionLocked_reads_SessionFlags_not_SessionState(
        int level,
        int sessionStateAtOffset12,
        int sessionFlagsAtOffset16,
        bool? expected)
    {
        const int BufferSize = 32;
        IntPtr buffer = Marshal.AllocHGlobal(BufferSize);
        try
        {
            for (int i = 0; i < BufferSize; i++)
            {
                Marshal.WriteByte(buffer, i, 0);
            }
            Marshal.WriteInt32(buffer, 0, level);
            Marshal.WriteInt32(buffer, 12, sessionStateAtOffset12);
            Marshal.WriteInt32(buffer, 16, sessionFlagsAtOffset16);

            UserActivityMonitor.ParseWtsSessionLocked(buffer, BufferSize).Should().Be(expected);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [Fact]
    public void ParseWtsSessionLocked_returns_null_for_truncated_or_null_buffer()
    {
        UserActivityMonitor.ParseWtsSessionLocked(IntPtr.Zero, 32).Should().BeNull();

        IntPtr buffer = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.WriteInt32(buffer, 0, 1);
            UserActivityMonitor.ParseWtsSessionLocked(buffer, 16).Should().BeNull();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal sealed class FakeUserActivityBackend : IUserActivityBackend
    {
        private uint _tick;
        private long _elapsedMs;
        private uint _lastInputTick;
        private LedgerState _ledger;

        public FakeUserActivityBackend(uint startTick, uint lastInputTick, bool? sessionLocked = false)
        {
            _tick = startTick;
            _lastInputTick = lastInputTick;
            SessionLocked = sessionLocked;
        }

        public bool? SessionLocked { get; set; }

        public int SleepCalls { get; private set; }

        public Action<long>? OnAdvance { get; set; }

        public void RecordExternalInput() => _lastInputTick = _tick;

        public uint TickNow() => _tick;

        public long ElapsedMs() => _elapsedMs;

        public uint LastInputTick(uint now) => _lastInputTick;

        public LedgerState ReadLedger() => _ledger;

        public void RecordHuman(uint tick) => _ledger = _ledger with { HasHuman = true, HumanTick = tick };

        public ForegroundWindowInfo? ReadForeground(IReadOnlyCollection<int>? revealTitleFor) => null;

        public bool? ReadSessionLocked() => SessionLocked;

        public void Sleep(int milliseconds)
        {
            SleepCalls++;
            _elapsedMs += milliseconds;
            _tick = unchecked(_tick + (uint)milliseconds);
            OnAdvance?.Invoke(_elapsedMs);
        }

        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sleep(milliseconds);
            return Task.CompletedTask;
        }
    }
}
