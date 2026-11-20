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

public sealed class BackgroundActivitySamplerTests
{
    private sealed class FakeTimerProvider : TimeProvider
    {
        private readonly List<FakeTimer> _timers = new();

        public int TimerCount => _timers.Count;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            FakeTimer timer = new(callback, state, (long)period.TotalMilliseconds);
            _timers.Add(timer);
            return timer;
        }

        public void TickAll()
        {
            foreach (FakeTimer timer in _timers)
            {
                timer.Fire();
            }
        }

        private sealed class FakeTimer(TimerCallback callback, object? state, long periodMs) : ITimer
        {
            private bool _disposed;

            public long PeriodMs => periodMs;

            public void Fire()
            {
                if (!_disposed)
                {
                    callback(state);
                }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                _disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }

    [Theory]
    [InlineData(null, UserActivityPolicy.DefaultActivitySampleMs)]
    [InlineData("", UserActivityPolicy.DefaultActivitySampleMs)]
    [InlineData("invalid", UserActivityPolicy.DefaultActivitySampleMs)]
    [InlineData("-1", UserActivityPolicy.DefaultActivitySampleMs)]
    [InlineData("10", UserActivityPolicy.DefaultActivitySampleMs)]
    [InlineData("60000", UserActivityPolicy.DefaultActivitySampleMs)]
    [InlineData("0", 0)]
    [InlineData("50", 50)]
    [InlineData("250", 250)]
    [InlineData("10000", 10000)]
    public void ParseActivitySampleMs_validates_interval_and_allows_zero_to_disable(string? raw, int expected)
    {
        UserActivityPolicy.ParseActivitySampleMs(raw).Should().Be(expected);
    }

    [Fact]
    public void Background_sampler_captures_jiggler_moves_between_tool_calls_into_recentInputs_and_detector()
    {
        const uint startTick = 500_000;
        UserActivityTests.FakeUserActivityBackend clock = new(startTick, startTick)
        {
            Cursor = new CursorPoint(500, 500),
        };
        FakeTimerProvider timerProvider = new();

        using UserActivityMonitor monitor = new(
            clock,
            () => new PeriodicMicroMoveConfig(Enabled: true, MaxDisplacementPx: 15),
            backgroundSampleMs: 250,
            timerProvider: timerProvider);

        timerProvider.TimerCount.Should().Be(1);

        // Simulate 12 seconds elapsing between MCP tool calls, with a 2400 ms USB jiggler
        // while the background sampler ticks every 250 ms.
        int jigglerMoves = 0;
        for (int elapsed = 250; elapsed <= 12_000; elapsed += 250)
        {
            clock.Sleep(250);
            if (elapsed % 2400 == 0 || (elapsed > 0 && (elapsed / 2400) > ((elapsed - 250) / 2400)))
            {
                jigglerMoves++;
                clock.Cursor = new CursorPoint(500 + (jigglerMoves % 2 == 0 ? 0 : 2), 500);
                clock.RecordExternalInput();
            }
            timerProvider.TickAll();
        }

        // First MCP tool call after 12 seconds of silence: jiggler is ALREADY learned and classified!
        UserActivitySnapshot snap = monitor.Read();

        snap.LastInputSource.Should().Be(UserActivityMonitor.SourcePeriodicMicroMove);
        snap.Diagnostics!.LastInputIgnoredAsPeriodicMicroMove.Should().BeTrue();
        snap.Diagnostics.RecentInputs.Count.Should().BeGreaterThanOrEqualTo(4);
        snap.Diagnostics.RecentInputs[^1].Source.Should().Be(UserActivityMonitor.SourcePeriodicMicroMove);
        snap.Diagnostics.RecentInputs[^1].DeltaFromPreviousMs.Should().BeInRange(2200, 2600);
        snap.HumanIdleMs.Should().BeGreaterThanOrEqualTo(2400);
    }

    [Fact]
    public void Background_sampler_is_disabled_when_interval_is_zero()
    {
        UserActivityTests.FakeUserActivityBackend clock = new(100_000, 100_000);
        FakeTimerProvider timerProvider = new();

        using UserActivityMonitor monitor = new(
            clock,
            backgroundSampleMs: 0,
            timerProvider: timerProvider);

        timerProvider.TimerCount.Should().Be(0);
    }
}
