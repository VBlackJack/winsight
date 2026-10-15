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

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Polls the UIA tree until a condition on a locator is satisfied or a
/// timeout elapses. This is the synchronization primitive that removes
/// "clicked before the UI was ready" flakiness from agent-driven tests.
/// </summary>
public sealed class ElementWaiter
{
    /// <summary>
    /// Wait until <paramref name="condition"/> holds for the locator.
    /// Null timeout/poll interval fall back to <see cref="UiaDefaults"/>.
    /// Returns a <see cref="WaitOutcome"/> payload either way; the result is
    /// a failure when the timeout elapses first. <paramref name="expectedValue"/>
    /// carries the text for value_* and name_* conditions (a regular expression
    /// for name_matches); <paramref name="expectedCount"/> and
    /// <paramref name="childControlType"/> drive child_count.
    /// </summary>
    public OperationResult Wait(
        int processId,
        ElementLocator locator,
        WaitCondition condition,
        int? timeoutMs = null,
        int? pollIntervalMs = null,
        string? windowTitle = null,
        string? expectedValue = null,
        int? stableForMs = null,
        int? expectedCount = null,
        string? childControlType = null)
    {
        if (locator.IsEmpty)
        {
            return OperationResult.Failure(
                "Locator is empty: provide at least one of automationId, name, nameContains, descendantText, className, or controlType.");
        }

        int timeout = timeoutMs ?? UiaDefaults.ResolveWaitTimeoutMs();
        if (timeout <= 0 || timeout > UiaDefaults.MaxWaitTimeoutMs)
        {
            return OperationResult.Failure(
                $"timeoutMs must be between 1 and {UiaDefaults.MaxWaitTimeoutMs}.");
        }

        int pollInterval = pollIntervalMs ?? UiaDefaults.DefaultPollIntervalMs;
        if (pollInterval < UiaDefaults.MinPollIntervalMs)
        {
            return OperationResult.Failure(
                $"pollIntervalMs must be at least {UiaDefaults.MinPollIntervalMs}.");
        }
        if (condition is WaitCondition.ValueEquals or WaitCondition.ValueContains
                or WaitCondition.NameEquals or WaitCondition.NameContains or WaitCondition.NameMatches &&
            expectedValue is null)
        {
            return OperationResult.Failure(
                $"expectedValue is required for condition '{condition}'.");
        }
        Regex? namePattern = null;
        if (condition == WaitCondition.NameMatches)
        {
            try
            {
                namePattern = new Regex(
                    expectedValue!, RegexOptions.CultureInvariant, WaitConditionEvaluator.NamePatternTimeout);
            }
            catch (ArgumentException ex)
            {
                return OperationResult.Failure(
                    $"expectedValue is not a valid regular expression for name_matches: {ex.Message}");
            }
        }
        if (condition == WaitCondition.ChildCount && expectedCount is not >= 0)
        {
            return OperationResult.Failure(
                "expectedCount (0 or more) is required for condition 'ChildCount'.");
        }
        WaitExpectation expectation = new(expectedValue, namePattern, expectedCount, childControlType);
        if (condition == WaitCondition.StableFor)
        {
            if (stableForMs is null)
            {
                return OperationResult.Failure(
                    "stableForMs is required for condition 'StableFor'.");
            }
            if (stableForMs <= 0 || stableForMs > timeout)
            {
                return OperationResult.Failure(
                    $"stableForMs must be between 1 and timeoutMs ({timeout}).");
            }
        }

        using UIA3Automation automation = new();
        Stopwatch stopwatch = Stopwatch.StartNew();
        ElementStateStabilityTracker stabilityTracker = new();

        while (true)
        {
            PollResult poll = EvaluateOnce(
                automation,
                processId,
                locator,
                condition,
                windowTitle,
                expectation);
            if (poll.StrictError is not null)
            {
                return OperationResult.FailureWith(poll.StrictError, poll.Candidates);
            }
            if (condition == WaitCondition.StableFor)
            {
                if (stabilityTracker.Observe(poll.State, stopwatch.ElapsedMilliseconds, stableForMs!.Value))
                {
                    return OperationResult.OkWith(new WaitOutcome(
                        condition.ToString(), ConditionMet: true, stopwatch.ElapsedMilliseconds, poll.MatchCount, poll.State));
                }
            }
            else if (poll.ConditionMet)
            {
                return OperationResult.OkWith(new WaitOutcome(
                    condition.ToString(), ConditionMet: true, stopwatch.ElapsedMilliseconds, poll.MatchCount, poll.State,
                    poll.ChildCount));
            }
            if (stopwatch.ElapsedMilliseconds >= timeout)
            {
                return OperationResult.FailureWith(
                    $"Condition '{condition}' not met for locator {locator} in process {processId} " +
                    $"after {stopwatch.ElapsedMilliseconds} ms ({poll.MatchCount} match(es) on last poll).",
                    new WaitOutcome(
                        condition.ToString(), ConditionMet: false, stopwatch.ElapsedMilliseconds, poll.MatchCount, poll.State,
                        poll.ChildCount));
            }
            Thread.Sleep(pollInterval);
        }
    }

    private sealed record PollResult(
        bool ConditionMet,
        int MatchCount,
        ElementState? State,
        string? StrictError = null,
        IReadOnlyList<ElementCandidate>? Candidates = null,
        int? ChildCount = null);

    private sealed record WaitExpectation(
        string? Text,
        Regex? NamePattern,
        int? Count,
        string? ChildControlType);

    private static PollResult EvaluateOnce(
        UIA3Automation automation,
        int processId,
        ElementLocator locator,
        WaitCondition condition,
        string? windowTitle,
        WaitExpectation expectation)
    {
        List<ElementMatch> matches;
        try
        {
            IReadOnlyList<WindowRoot> windows =
                ElementFinder.ResolveWindows(automation, processId, windowTitle);
            matches = ElementFinder.CollectMatches(windows, locator);
        }
        catch (COMException)
        {
            // The UI mutated mid-walk (window closing, popup tearing down).
            // Treat as an unmet condition and let the next poll retry.
            return new PollResult(ConditionMet: false, MatchCount: 0, State: null);
        }

        if (locator.Index is int index)
        {
            matches = index >= 0 && index < matches.Count
                ? new List<ElementMatch> { matches[index] }
                : new List<ElementMatch>();
        }

        switch (condition)
        {
            case WaitCondition.Exists:
                return matches.Count >= 1
                    ? new PollResult(true, matches.Count, ElementStateReader.BuildState(matches[0]))
                    : new PollResult(false, 0, null);

            case WaitCondition.NotExists:
                return new PollResult(matches.Count == 0, matches.Count, null);

            case WaitCondition.Enabled:
            case WaitCondition.Visible:
            case WaitCondition.ValueEquals:
            case WaitCondition.ValueContains:
            case WaitCondition.StableFor:
            case WaitCondition.NameEquals:
            case WaitCondition.NameContains:
            case WaitCondition.NameMatches:
            case WaitCondition.ChildCount:
                if (matches.Count > 1)
                {
                    return new PollResult(
                        false, matches.Count, null,
                        $"Locator {locator} matched {matches.Count} elements in process {processId}; " +
                        $"'{condition}' requires a unique match. Add 'index' or narrow the locator.",
                        matches.Take(UiaDefaults.MaxReportedCandidates).Select(m => m.Candidate).ToList());
                }
                if (matches.Count == 0)
                {
                    return new PollResult(false, 0, null);
                }
                ElementState state;
                int? childCount = null;
                try
                {
                    state = ElementStateReader.BuildState(matches[0]);
                    if (condition == WaitCondition.ChildCount)
                    {
                        childCount = WaitConditionEvaluator.CountChildren(
                            matches[0].Element.FindAllChildren()
                                .Select(c => c.Properties.ControlType.ValueOrDefault.ToString()),
                            expectation.ChildControlType);
                    }
                }
                catch (COMException)
                {
                    // The element changed while it was read; retry on the next poll.
                    return new PollResult(false, 1, null);
                }
                bool met = condition switch
                {
                    WaitCondition.Enabled => state.IsEnabled,
                    WaitCondition.Visible => state.IsVisible,
                    WaitCondition.ValueEquals or WaitCondition.ValueContains =>
                        WaitConditionEvaluator.ValueMatches(condition, state.Value, expectation.Text!),
                    WaitCondition.NameEquals or WaitCondition.NameContains or WaitCondition.NameMatches =>
                        WaitConditionEvaluator.NameMatches(condition, state.Name, expectation.Text!, expectation.NamePattern),
                    WaitCondition.ChildCount => childCount == expectation.Count,
                    WaitCondition.StableFor => true,
                    _ => false,
                };
                return new PollResult(met, 1, state, ChildCount: childCount);

            default:
                return new PollResult(false, matches.Count, null,
                    $"Unsupported wait condition '{condition}'.");
        }
    }
}
