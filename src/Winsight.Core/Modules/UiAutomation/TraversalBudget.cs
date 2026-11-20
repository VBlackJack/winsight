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

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Stable wire values explaining why a UIA traversal stopped early.
/// </summary>
public static class TraversalTruncationReasons
{
    public const string TimeBudget = "time_budget";
    public const string NodeLimit = "node_limit";
    public const string MaxResults = "max_results";
    public const string ComError = "com_error";
}

/// <summary>
/// Tracks wall-clock time and node-count limits for a UIA tree traversal.
/// </summary>
public sealed class TraversalBudget
{
    private readonly TimeProvider _timeProvider;
    private readonly long _startedTimestamp;
    private readonly TimeSpan _timeBudget;
    private readonly int _maxNodes;

    public TraversalBudget(
        TimeSpan timeBudget,
        int maxNodes = UiaDefaults.MaxTraversalNodes,
        TimeProvider? timeProvider = null)
    {
        if (timeBudget <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeBudget), "timeBudget must be positive.");
        }
        if (maxNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNodes), "maxNodes must be positive.");
        }

        _timeBudget = timeBudget;
        _maxNodes = maxNodes;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _startedTimestamp = _timeProvider.GetTimestamp();
    }

    public static TraversalBudget FromEnvironment(
        int maxNodes = UiaDefaults.MaxTraversalNodes,
        TimeProvider? timeProvider = null) =>
        new(TimeSpan.FromMilliseconds(UiaDefaults.ResolveSceneTraversalBudgetMs()), maxNodes, timeProvider);

    public int Visited { get; private set; }

    public string? TruncationReason { get; private set; }

    public bool IsTruncated => TruncationReason is not null;

    public bool IsTimeExceeded()
    {
        if (_timeProvider.GetElapsedTime(_startedTimestamp) >= _timeBudget)
        {
            TruncationReason = TraversalTruncationReasons.TimeBudget;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Consume one traversal step. Returns false when either the time budget
    /// or the node limit has been reached, setting <see cref="TruncationReason"/>.
    /// </summary>
    public bool TryStep()
    {
        if (IsTimeExceeded())
        {
            return false;
        }

        if (++Visited > _maxNodes)
        {
            TruncationReason ??= TraversalTruncationReasons.NodeLimit;
            return false;
        }

        return true;
    }

    public void MarkTruncated(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        TruncationReason ??= reason;
    }
}
