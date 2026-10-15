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

namespace Winsight.Core.Models;

/// <summary>
/// Condition polled by the element waiter until satisfied or timed out.
/// </summary>
public enum WaitCondition
{
    /// <summary>At least one element matches the locator.</summary>
    Exists,

    /// <summary>No element matches the locator (e.g. a dialog has closed).</summary>
    NotExists,

    /// <summary>Exactly one element matches and is enabled.</summary>
    Enabled,

    /// <summary>Exactly one element matches and is on-screen.</summary>
    Visible,

    /// <summary>Exactly one element matches and its Value equals the expected value.</summary>
    ValueEquals,

    /// <summary>Exactly one element matches and its Value contains the expected value.</summary>
    ValueContains,

    /// <summary>Exactly one element matches and its full state remains unchanged for a duration.</summary>
    StableFor,

    /// <summary>Exactly one element matches and its Name equals the expected text (ordinal).</summary>
    NameEquals,

    /// <summary>Exactly one element matches and its Name contains the expected text (case-insensitive).</summary>
    NameContains,

    /// <summary>Exactly one element matches and its Name matches the expected regular expression.</summary>
    NameMatches,

    /// <summary>
    /// Exactly one element matches and it has the expected number of direct
    /// children, optionally counting only children of one control type
    /// (e.g. the Tab named "Sessions" has 3 TabItem children).
    /// </summary>
    ChildCount,
}

/// <summary>
/// Parses wire-format condition names ("exists", "not_exists", "enabled",
/// "visible", "value_equals", "value_contains", "stable_for", "name_equals",
/// "name_contains", "name_matches", "child_count") into
/// <see cref="WaitCondition"/> values. Tolerates casing and underscores so
/// agent-produced input round-trips reliably.
/// </summary>
public static class WaitConditionParser
{
    /// <summary>Try to parse a wire-format condition name.</summary>
    public static bool TryParse(string? raw, out WaitCondition condition)
    {
        string normalized = (raw ?? string.Empty)
            .Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal);
        if (string.Equals(normalized, "exists", StringComparison.OrdinalIgnoreCase))
        {
            condition = WaitCondition.Exists;
            return true;
        }
        if (string.Equals(normalized, "notexists", StringComparison.OrdinalIgnoreCase))
        {
            condition = WaitCondition.NotExists;
            return true;
        }
        if (string.Equals(normalized, "enabled", StringComparison.OrdinalIgnoreCase))
        {
            condition = WaitCondition.Enabled;
            return true;
        }
        if (string.Equals(normalized, "visible", StringComparison.OrdinalIgnoreCase))
        {
            condition = WaitCondition.Visible;
            return true;
        }
        if (string.Equals(normalized, "valueequals", StringComparison.OrdinalIgnoreCase))
        {
            condition = WaitCondition.ValueEquals;
            return true;
        }
        if (string.Equals(normalized, "valuecontains", StringComparison.OrdinalIgnoreCase))
        {
            condition = WaitCondition.ValueContains;
            return true;
        }
        if (string.Equals(normalized, "stablefor", StringComparison.OrdinalIgnoreCase))
        {
            condition = WaitCondition.StableFor;
            return true;
        }
        if (string.Equals(normalized, "nameequals", StringComparison.OrdinalIgnoreCase))
        {
            condition = WaitCondition.NameEquals;
            return true;
        }
        if (string.Equals(normalized, "namecontains", StringComparison.OrdinalIgnoreCase))
        {
            condition = WaitCondition.NameContains;
            return true;
        }
        if (string.Equals(normalized, "namematches", StringComparison.OrdinalIgnoreCase))
        {
            condition = WaitCondition.NameMatches;
            return true;
        }
        if (string.Equals(normalized, "childcount", StringComparison.OrdinalIgnoreCase))
        {
            condition = WaitCondition.ChildCount;
            return true;
        }
        condition = WaitCondition.Exists;
        return false;
    }

    /// <summary>Comma-separated list of accepted wire-format names, for error messages.</summary>
    public static string AcceptedValues =>
        "exists, not_exists, enabled, visible, value_equals, value_contains, stable_for, " +
        "name_equals, name_contains, name_matches, child_count";
}
