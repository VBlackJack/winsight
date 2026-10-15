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

using System.Text.RegularExpressions;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

internal static class WaitConditionEvaluator
{
    internal static bool ValueMatches(
        WaitCondition condition,
        string? actualValue,
        string expectedValue)
    {
        return condition switch
        {
            WaitCondition.ValueEquals =>
                string.Equals(actualValue, expectedValue, StringComparison.Ordinal),
            WaitCondition.ValueContains =>
                actualValue is not null &&
                actualValue.Contains(expectedValue, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    /// <summary>Upper bound on one name_matches evaluation.</summary>
    internal static readonly TimeSpan NamePatternTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// name_equals is ordinal, name_contains is case-insensitive, and
    /// name_matches applies <paramref name="pattern"/>, which the caller
    /// compiles once from the expected text.
    /// </summary>
    internal static bool NameMatches(
        WaitCondition condition,
        string? actualName,
        string expectedText,
        Regex? pattern)
    {
        if (actualName is null)
        {
            return false;
        }
        try
        {
            return condition switch
            {
                WaitCondition.NameEquals => string.Equals(actualName, expectedText, StringComparison.Ordinal),
                WaitCondition.NameContains => actualName.Contains(expectedText, StringComparison.OrdinalIgnoreCase),
                WaitCondition.NameMatches => pattern is not null && pattern.IsMatch(actualName),
                _ => false,
            };
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Count the children whose control type equals <paramref name="childControlType"/>
    /// (case-insensitive), or every child when it is null or empty.
    /// </summary>
    internal static int CountChildren(IEnumerable<string> childControlTypes, string? childControlType) =>
        string.IsNullOrEmpty(childControlType)
            ? childControlTypes.Count()
            : childControlTypes.Count(t => string.Equals(t, childControlType, StringComparison.OrdinalIgnoreCase));
}
