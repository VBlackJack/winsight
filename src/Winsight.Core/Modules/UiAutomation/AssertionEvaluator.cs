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

using System.Globalization;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Operators an assertion can apply to a located element's state.
/// </summary>
public enum AssertionOperator
{
    Equals,
    NotEquals,
    Contains,
    NotContains,
    GreaterThan,
    LessThan,
    Enabled,
    Disabled,
    Visible,
    Hidden,
    Checked,
    Unchecked,
    Selected,
    Exists,
}

/// <summary>Result of evaluating an assertion: whether it held, with a human-readable detail.</summary>
public readonly record struct AssertionResult(bool Passed, string Detail);

/// <summary>
/// Evaluates an assertion against an <see cref="ElementState"/>. Pure, so the
/// pass/fail semantics behind the <c>assert</c> step and <c>assert_element</c>
/// tool are unit-tested without live UI Automation. Text comparisons use the
/// element's Value when it has one, otherwise its Name, so both inputs and
/// labels can be asserted.
/// </summary>
public static class AssertionEvaluator
{
    /// <summary>Comma-separated wire-format operator names, for error messages.</summary>
    public static string AcceptedOperators =>
        "equals, not_equals, contains, not_contains, greater_than, less_than, enabled, " +
        "disabled, visible, hidden, checked, unchecked, selected, exists";

    public static bool TryParseOperator(string? raw, out AssertionOperator op)
    {
        switch ((raw ?? string.Empty).Trim().Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant())
        {
            case "equals": op = AssertionOperator.Equals; return true;
            case "notequals": op = AssertionOperator.NotEquals; return true;
            case "contains": op = AssertionOperator.Contains; return true;
            case "notcontains": op = AssertionOperator.NotContains; return true;
            case "greaterthan": op = AssertionOperator.GreaterThan; return true;
            case "lessthan": op = AssertionOperator.LessThan; return true;
            case "enabled": op = AssertionOperator.Enabled; return true;
            case "disabled": op = AssertionOperator.Disabled; return true;
            case "visible": op = AssertionOperator.Visible; return true;
            case "hidden": op = AssertionOperator.Hidden; return true;
            case "checked": op = AssertionOperator.Checked; return true;
            case "unchecked": op = AssertionOperator.Unchecked; return true;
            case "selected": op = AssertionOperator.Selected; return true;
            case "exists": op = AssertionOperator.Exists; return true;
            default: op = AssertionOperator.Equals; return false;
        }
    }

    public static AssertionResult Evaluate(AssertionOperator op, string? expected, ElementState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        string text = state.Value ?? state.Name;

        return op switch
        {
            AssertionOperator.Equals => TextResult(
                string.Equals(text, expected, StringComparison.Ordinal), "equal", expected, text),
            AssertionOperator.NotEquals => TextResult(
                !string.Equals(text, expected, StringComparison.Ordinal), "not equal", expected, text),
            AssertionOperator.Contains => TextResult(
                expected is not null && text.Contains(expected, StringComparison.OrdinalIgnoreCase),
                "contain", expected, text),
            AssertionOperator.NotContains => TextResult(
                expected is null || !text.Contains(expected, StringComparison.OrdinalIgnoreCase),
                "not contain", expected, text),
            AssertionOperator.GreaterThan => NumericResult(op, expected, text),
            AssertionOperator.LessThan => NumericResult(op, expected, text),
            AssertionOperator.Enabled => FlagResult(state.IsEnabled, "enabled", "disabled"),
            AssertionOperator.Disabled => FlagResult(!state.IsEnabled, "disabled", "enabled"),
            AssertionOperator.Visible => FlagResult(state.IsVisible, "visible", "hidden"),
            AssertionOperator.Hidden => FlagResult(!state.IsVisible, "hidden", "visible"),
            AssertionOperator.Checked => FlagResult(
                string.Equals(state.ToggleState, "On", StringComparison.Ordinal),
                "checked", $"toggleState={state.ToggleState ?? "none"}"),
            AssertionOperator.Unchecked => FlagResult(
                string.Equals(state.ToggleState, "Off", StringComparison.Ordinal),
                "unchecked", $"toggleState={state.ToggleState ?? "none"}"),
            AssertionOperator.Selected => FlagResult(
                state.IsSelected == true, "selected", $"isSelected={Describe(state.IsSelected)}"),
            AssertionOperator.Exists => new AssertionResult(true, "element exists"),
            _ => new AssertionResult(false, $"Unsupported operator '{op}'."),
        };
    }

    private static AssertionResult TextResult(bool passed, string verb, string? expected, string actual)
    {
        return passed
            ? new AssertionResult(true, $"value does {verb} \"{expected}\"")
            : new AssertionResult(false, $"expected value to {verb} \"{expected}\" but was \"{actual}\"");
    }

    private static AssertionResult NumericResult(AssertionOperator op, string? expected, string actual)
    {
        if (!TryParseNumber(actual, out double actualValue) || !TryParseNumber(expected, out double expectedValue))
        {
            return new AssertionResult(false,
                $"cannot compare numerically: value \"{actual}\" vs expected \"{expected}\"");
        }
        bool passed = op == AssertionOperator.GreaterThan
            ? actualValue > expectedValue
            : actualValue < expectedValue;
        string verb = op == AssertionOperator.GreaterThan ? "greater than" : "less than";
        return passed
            ? new AssertionResult(true, $"{actualValue} is {verb} {expectedValue}")
            : new AssertionResult(false, $"expected {actualValue} to be {verb} {expectedValue}");
    }

    private static AssertionResult FlagResult(bool passed, string expectedState, string actualState)
    {
        return passed
            ? new AssertionResult(true, $"element is {expectedState}")
            : new AssertionResult(false, $"expected element to be {expectedState} but was {actualState}");
    }

    private static bool TryParseNumber(string? raw, out double value) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Describe(bool? value) => value switch
    {
        true => "true",
        false => "false",
        null => "not a selectable element",
    };
}
