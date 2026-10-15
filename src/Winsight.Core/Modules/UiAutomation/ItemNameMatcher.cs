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

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// How select_item recognizes the item to select: the exact accessible name,
/// a case-insensitive substring, or a regular expression. Substring and
/// pattern matching survive names that carry live data, such as a tree row
/// named "lab-c, protocol SFTP, state Reachable (1 ms) · 13:03:33" whose
/// clock changes every second.
/// </summary>
public sealed class ItemNameMatcher
{
    /// <summary>Upper bound on one regular-expression evaluation.</summary>
    public static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    private readonly string? _exact;
    private readonly string? _contains;
    private readonly Regex? _pattern;

    private ItemNameMatcher(string? exact, string? contains, Regex? pattern, string description)
    {
        _exact = exact;
        _contains = contains;
        _pattern = pattern;
        Description = description;
    }

    /// <summary>Human-readable criterion, for error messages.</summary>
    public string Description { get; }

    /// <summary>The exact name, when the matcher is exact; used by containers that select by text.</summary>
    public string? ExactName => _exact;

    public static ItemNameMatcher Exact(string itemName)
    {
        ArgumentException.ThrowIfNullOrEmpty(itemName);
        return new ItemNameMatcher(itemName, null, null, $"named \"{itemName}\"");
    }

    /// <summary>
    /// Build a matcher from exactly one of the three criteria. Returns false
    /// with an error when none or several are given, or when the pattern does
    /// not compile.
    /// </summary>
    public static bool TryCreate(
        string? itemName,
        string? itemNameContains,
        string? itemNamePattern,
        out ItemNameMatcher? matcher,
        out string? error)
    {
        matcher = null;
        error = null;
        int provided = new[] { itemName, itemNameContains, itemNamePattern }.Count(v => !string.IsNullOrEmpty(v));
        if (provided != 1)
        {
            error = "Provide exactly one of itemName, itemNameContains, or itemNamePattern.";
            return false;
        }
        if (!string.IsNullOrEmpty(itemName))
        {
            matcher = Exact(itemName);
            return true;
        }
        if (!string.IsNullOrEmpty(itemNameContains))
        {
            matcher = new ItemNameMatcher(null, itemNameContains, null, $"whose name contains \"{itemNameContains}\"");
            return true;
        }
        try
        {
            Regex regex = new(itemNamePattern!, RegexOptions.CultureInvariant, PatternTimeout);
            matcher = new ItemNameMatcher(null, null, regex, $"whose name matches /{itemNamePattern}/");
            return true;
        }
        catch (ArgumentException ex)
        {
            error = $"itemNamePattern is not a valid regular expression: {ex.Message}";
            return false;
        }
    }

    /// <summary>True when the accessible name satisfies the criterion.</summary>
    public bool Matches(string? name)
    {
        if (name is null)
        {
            return false;
        }
        if (_exact is not null)
        {
            return string.Equals(name, _exact, StringComparison.Ordinal);
        }
        if (_contains is not null)
        {
            return name.Contains(_contains, StringComparison.OrdinalIgnoreCase);
        }
        try
        {
            return _pattern!.IsMatch(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
