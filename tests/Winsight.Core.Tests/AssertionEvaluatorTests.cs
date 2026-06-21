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

public sealed class AssertionEvaluatorTests
{
    [Theory]
    [InlineData("equals", true)]
    [InlineData("not_equals", true)]
    [InlineData("greater_than", true)]
    [InlineData("checked", true)]
    [InlineData("nonsense", false)]
    public void TryParseOperator_accepts_wire_names(string raw, bool expected)
    {
        AssertionEvaluator.TryParseOperator(raw, out _).Should().Be(expected);
    }

    [Fact]
    public void Equals_compares_the_value_then_name()
    {
        // Value present: compared against the value.
        Evaluate(AssertionOperator.Equals, "5", State(value: "5")).Passed.Should().BeTrue();
        Evaluate(AssertionOperator.Equals, "5", State(value: "3")).Passed.Should().BeFalse();
        // No value: falls back to the name (e.g. a label).
        Evaluate(AssertionOperator.Equals, "Count: 5", State(name: "Count: 5")).Passed.Should().BeTrue();
    }

    [Fact]
    public void Contains_is_case_insensitive_and_reports_actual_on_failure()
    {
        Evaluate(AssertionOperator.Contains, "count", State(name: "Count: 5")).Passed.Should().BeTrue();

        AssertionResult fail = Evaluate(AssertionOperator.Contains, "nope", State(name: "Count: 5"));
        fail.Passed.Should().BeFalse();
        fail.Detail.Should().Contain("Count: 5");
    }

    [Fact]
    public void Numeric_operators_compare_as_numbers()
    {
        Evaluate(AssertionOperator.GreaterThan, "4", State(value: "5")).Passed.Should().BeTrue();
        Evaluate(AssertionOperator.LessThan, "4", State(value: "5")).Passed.Should().BeFalse();
        Evaluate(AssertionOperator.GreaterThan, "4", State(value: "not-a-number")).Passed.Should().BeFalse();
    }

    [Fact]
    public void Flag_operators_read_element_state()
    {
        Evaluate(AssertionOperator.Enabled, null, State(isEnabled: true)).Passed.Should().BeTrue();
        Evaluate(AssertionOperator.Disabled, null, State(isEnabled: true)).Passed.Should().BeFalse();
        Evaluate(AssertionOperator.Hidden, null, State(isVisible: false)).Passed.Should().BeTrue();
        Evaluate(AssertionOperator.Checked, null, State(toggleState: "On")).Passed.Should().BeTrue();
        Evaluate(AssertionOperator.Unchecked, null, State(toggleState: "On")).Passed.Should().BeFalse();
        Evaluate(AssertionOperator.Selected, null, State(isSelected: true)).Passed.Should().BeTrue();
    }

    [Fact]
    public void Exists_passes_for_any_located_element()
    {
        Evaluate(AssertionOperator.Exists, null, State()).Passed.Should().BeTrue();
    }

    private static AssertionResult Evaluate(AssertionOperator op, string? expected, ElementState state) =>
        AssertionEvaluator.Evaluate(op, expected, state);

    private static ElementState State(
        string? value = null,
        string name = "",
        bool isEnabled = true,
        bool isVisible = true,
        string? toggleState = null,
        bool? isSelected = null) =>
        new(
            AutomationId: "X",
            Name: name,
            ControlType: "Text",
            ClassName: "Text",
            WindowTitle: "Main",
            IsEnabled: isEnabled,
            IsVisible: isVisible,
            HasKeyboardFocus: false,
            Value: value,
            ToggleState: toggleState,
            IsSelected: isSelected,
            SelectedItem: null,
            X: 0, Y: 0, Width: 10, Height: 10);
}
