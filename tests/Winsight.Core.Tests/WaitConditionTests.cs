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
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public class WaitConditionTests
{
    [Theory]
    [InlineData("value_equals", WaitCondition.ValueEquals)]
    [InlineData("VALUE_CONTAINS", WaitCondition.ValueContains)]
    [InlineData("stable_for", WaitCondition.StableFor)]
    public void Parser_accepts_new_wait_conditions(string raw, WaitCondition expected)
    {
        bool parsed = WaitConditionParser.TryParse(raw, out WaitCondition condition);

        parsed.Should().BeTrue();
        condition.Should().Be(expected);
    }

    [Theory]
    [InlineData("name_equals", WaitCondition.NameEquals)]
    [InlineData("name_contains", WaitCondition.NameContains)]
    [InlineData("NAME_MATCHES", WaitCondition.NameMatches)]
    [InlineData("child_count", WaitCondition.ChildCount)]
    public void Parser_accepts_name_and_child_count_conditions(string raw, WaitCondition expected)
    {
        WaitConditionParser.TryParse(raw, out WaitCondition condition).Should().BeTrue();
        condition.Should().Be(expected);
        WaitConditionParser.AcceptedValues.Should().Contain(raw.ToLowerInvariant());
    }

    [Fact]
    public void Name_conditions_follow_their_comparison_rules()
    {
        const string name = "lab-c, protocol SFTP, state Reachable (1 ms) · 13:03:33";
        System.Text.RegularExpressions.Regex clock = new(@"· \d{2}:\d{2}:\d{2}$");

        WaitConditionEvaluator.NameMatches(WaitCondition.NameEquals, name, name, null).Should().BeTrue();
        WaitConditionEvaluator.NameMatches(WaitCondition.NameEquals, name, name.ToUpperInvariant(), null).Should().BeFalse();
        WaitConditionEvaluator.NameMatches(WaitCondition.NameContains, name, "REACHABLE", null).Should().BeTrue();
        WaitConditionEvaluator.NameMatches(WaitCondition.NameMatches, name, "unused", clock).Should().BeTrue();
        WaitConditionEvaluator.NameMatches(WaitCondition.NameContains, null, "lab", null).Should().BeFalse();
    }

    [Fact]
    public void Child_count_filters_by_control_type_when_asked()
    {
        string[] children = { "TabItem", "TabItem", "Pane", "tabitem" };

        WaitConditionEvaluator.CountChildren(children, "TabItem").Should().Be(3);
        WaitConditionEvaluator.CountChildren(children, null).Should().Be(4);
    }

    [Fact]
    public void Value_equals_is_case_sensitive()
    {
        WaitConditionEvaluator.ValueMatches(
                WaitCondition.ValueEquals,
                "Ready",
                "ready")
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Value_contains_is_case_insensitive()
    {
        WaitConditionEvaluator.ValueMatches(
                WaitCondition.ValueContains,
                "Server Ready",
                "ready")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Stability_tracker_resets_when_state_changes()
    {
        ElementState first = BuildState("one");
        ElementState second = BuildState("two");
        ElementStateStabilityTracker tracker = new();

        tracker.Observe(first, elapsedMs: 0, stableForMs: 300).Should().BeFalse();
        tracker.Observe(first, elapsedMs: 250, stableForMs: 300).Should().BeFalse();
        tracker.Observe(second, elapsedMs: 260, stableForMs: 300).Should().BeFalse();
        tracker.Observe(second, elapsedMs: 559, stableForMs: 300).Should().BeFalse();
        tracker.Observe(second, elapsedMs: 560, stableForMs: 300).Should().BeTrue();
    }

    private static ElementState BuildState(string value) =>
        new(
            AutomationId: "Status",
            Name: "Status",
            ControlType: "Edit",
            ClassName: "TextBox",
            WindowTitle: "Window",
            IsEnabled: true,
            IsVisible: true,
            HasKeyboardFocus: false,
            Value: value,
            ToggleState: null,
            IsSelected: null,
            SelectedItem: null,
            X: 1,
            Y: 2,
            Width: 3,
            Height: 4);
}
