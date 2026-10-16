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
}
