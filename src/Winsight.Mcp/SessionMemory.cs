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

using Winsight.Core.Models;

namespace Winsight.Mcp;

public sealed record LocatorMemoryEntry(
    string Key,
    string AffordanceId,
    int ProcessId,
    string? WindowTitle,
    string ControlType,
    string? AutomationId,
    string Name,
    string RecommendedLocator,
    DateTime LastSeenUtc,
    int SeenCount);

public sealed record SessionFailure(
    DateTime TimestampUtc,
    string ToolName,
    string Code,
    string Message,
    string? SuggestedTool);

public sealed record SessionMemorySnapshot(
    int? TargetProcessId,
    string? LastWindowTitle,
    DateTime? LastObservationUtc,
    IReadOnlyList<LocatorMemoryEntry> Locators,
    IReadOnlyList<SessionFailure> Failures);

public sealed record LocatorMemoryMatch(
    int Score,
    string Reason,
    LocatorMemoryEntry Entry);

public sealed class SessionMemory
{
    private const int MaxEntries = 200;
    private const int MaxFailures = 50;
    private static readonly char[] TermSeparators = { ' ', '-', '_', '.', ':' };

    private readonly object _gate = new();
    private readonly Dictionary<string, LocatorMemoryEntry> _locators = new(StringComparer.Ordinal);
    private readonly List<SessionFailure> _failures = new();
    private string? _lastWindowTitle;
    private DateTime? _lastObservationUtc;

    public void RecordObservation(
        int processId,
        string? windowTitle,
        IReadOnlyList<UiInteractable> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        lock (_gate)
        {
            DateTime now = DateTime.UtcNow;
            _lastWindowTitle = windowTitle;
            _lastObservationUtc = now;

            foreach (UiInteractable element in elements)
            {
                if (string.IsNullOrWhiteSpace(element.RecommendedLocator))
                {
                    continue;
                }

                string key = BuildKey(processId, windowTitle, element);
                int seenCount = _locators.TryGetValue(key, out LocatorMemoryEntry? existing)
                    ? existing.SeenCount + 1
                    : 1;
                _locators[key] = new LocatorMemoryEntry(
                    key,
                    element.AffordanceId,
                    processId,
                    windowTitle,
                    element.ControlType,
                    element.AutomationId,
                    element.Name,
                    element.RecommendedLocator,
                    now,
                    seenCount);
            }

            PruneLocators();
        }
    }

    public void RecordFailure(string toolName, string code, string message, string? suggestedTool)
    {
        lock (_gate)
        {
            _failures.Add(new SessionFailure(DateTime.UtcNow, toolName, code, message, suggestedTool));
            if (_failures.Count > MaxFailures)
            {
                _failures.RemoveRange(0, _failures.Count - MaxFailures);
            }
        }
    }

    public IReadOnlyList<LocatorMemoryMatch> Find(
        string query,
        int? processId = null,
        string? windowTitle = null,
        int maxResults = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<LocatorMemoryMatch>();
        }

        string[] terms = SplitTerms(query);
        lock (_gate)
        {
            return _locators.Values
                .Where(entry => processId is null || entry.ProcessId == processId)
                .Where(entry => string.IsNullOrEmpty(windowTitle) ||
                    string.Equals(entry.WindowTitle, windowTitle, StringComparison.OrdinalIgnoreCase) ||
                    (entry.WindowTitle?.Contains(windowTitle, StringComparison.OrdinalIgnoreCase) ?? false))
                .Select(entry => Score(entry, query, terms))
                .Where(match => match.Score > 0)
                .OrderByDescending(match => match.Score)
                .ThenByDescending(match => match.Entry.LastSeenUtc)
                .Take(Math.Max(1, maxResults))
                .ToList();
        }
    }

    public SessionMemorySnapshot Snapshot(int? targetProcessId)
    {
        lock (_gate)
        {
            return new SessionMemorySnapshot(
                targetProcessId,
                _lastWindowTitle,
                _lastObservationUtc,
                _locators.Values
                    .OrderByDescending(entry => entry.LastSeenUtc)
                    .ThenByDescending(entry => entry.SeenCount)
                    .Take(50)
                    .ToList(),
                _failures
                    .OrderByDescending(failure => failure.TimestampUtc)
                    .ToList());
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _locators.Clear();
            _failures.Clear();
            _lastWindowTitle = null;
            _lastObservationUtc = null;
        }
    }

    private static string BuildKey(int processId, string? windowTitle, UiInteractable element)
    {
        return string.Join(
            "|",
            processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            windowTitle ?? string.Empty,
            element.ControlType,
            element.AffordanceId,
            element.AutomationId ?? string.Empty,
            element.Name,
            element.RecommendedLocator);
    }

    private static LocatorMemoryMatch Score(
        LocatorMemoryEntry entry,
        string query,
        IReadOnlyList<string> terms)
    {
        var score = 0;
        List<string> reasons = new();
        if (string.Equals(entry.Name, query, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
            reasons.Add("exactName");
        }
        if (string.Equals(entry.AutomationId, query, StringComparison.OrdinalIgnoreCase))
        {
            score += 90;
            reasons.Add("exactAutomationId");
        }
        if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
            reasons.Add("nameContains");
        }
        if (entry.AutomationId?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
        {
            score += 50;
            reasons.Add("automationIdContains");
        }
        if (entry.RecommendedLocator.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
            reasons.Add("locatorContains");
        }

        foreach (string term in terms)
        {
            if (entry.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                entry.RecommendedLocator.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                entry.ControlType.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }
        }

        return new LocatorMemoryMatch(score, string.Join(", ", reasons.Distinct()), entry);
    }

    private static string[] SplitTerms(string query)
    {
        return query.Split(
            TermSeparators,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private void PruneLocators()
    {
        if (_locators.Count <= MaxEntries)
        {
            return;
        }

        foreach (string key in _locators.Values
            .OrderBy(entry => entry.LastSeenUtc)
            .Take(_locators.Count - MaxEntries)
            .Select(entry => entry.Key)
            .ToList())
        {
            _locators.Remove(key);
        }
    }
}
