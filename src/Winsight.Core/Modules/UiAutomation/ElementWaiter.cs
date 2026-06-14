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
    /// a failure when the timeout elapses first.
    /// </summary>
    public OperationResult Wait(
        int processId,
        ElementLocator locator,
        WaitCondition condition,
        int? timeoutMs = null,
        int? pollIntervalMs = null,
        string? windowTitle = null,
        string? expectedValue = null,
        int? stableForMs = null)
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
        if (condition is WaitCondition.ValueEquals or WaitCondition.ValueContains &&
            expectedValue is null)
        {
            return OperationResult.Failure(
                $"expectedValue is required for condition '{condition}'.");
        }
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
                expectedValue);
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
                    condition.ToString(), ConditionMet: true, stopwatch.ElapsedMilliseconds, poll.MatchCount, poll.State));
            }
            if (stopwatch.ElapsedMilliseconds >= timeout)
            {
                return OperationResult.FailureWith(
                    $"Condition '{condition}' not met for locator {locator} in process {processId} " +
                    $"after {stopwatch.ElapsedMilliseconds} ms ({poll.MatchCount} match(es) on last poll).",
                    new WaitOutcome(
                        condition.ToString(), ConditionMet: false, stopwatch.ElapsedMilliseconds, poll.MatchCount, poll.State));
            }
            Thread.Sleep(pollInterval);
        }
    }

    private sealed record PollResult(
        bool ConditionMet,
        int MatchCount,
        ElementState? State,
        string? StrictError = null,
        IReadOnlyList<ElementCandidate>? Candidates = null);

    private static PollResult EvaluateOnce(
        UIA3Automation automation,
        int processId,
        ElementLocator locator,
        WaitCondition condition,
        string? windowTitle,
        string? expectedValue)
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
                ElementState state = ElementStateReader.BuildState(matches[0]);
                bool met = condition switch
                {
                    WaitCondition.Enabled => state.IsEnabled,
                    WaitCondition.Visible => state.IsVisible,
                    WaitCondition.ValueEquals or WaitCondition.ValueContains =>
                        WaitConditionEvaluator.ValueMatches(condition, state.Value, expectedValue!),
                    WaitCondition.StableFor => true,
                    _ => false,
                };
                return new PollResult(met, 1, state);

            default:
                return new PollResult(false, matches.Count, null,
                    $"Unsupported wait condition '{condition}'.");
        }
    }
}
