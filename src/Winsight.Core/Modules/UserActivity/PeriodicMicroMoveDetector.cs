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

namespace Winsight.Core.Modules.UserActivity;

/// <summary>
/// Configuration for opt-in hardware mouse jiggler filtering.
/// </summary>
public readonly record struct PeriodicMicroMoveConfig(
    bool Enabled,
    int MaxDisplacementPx = UserActivityPolicy.DefaultPeriodicMicroMoveMaxPx,
    int MinIntervals = 3,
    long MinPeriodMs = 250,
    long MaxPeriodMs = 60_000,
    double MaxRelativeJitter = 0.15,
    long MinJitterToleranceMs = 100)
{
    public static PeriodicMicroMoveConfig FromEnvironment() => new(
        Enabled: UserActivityPolicy.IsPeriodicMicroMoveFilterEnabled,
        MaxDisplacementPx: UserActivityPolicy.PeriodicMicroMoveThresholdPx);
}

/// <summary>
/// Opt-in classifier that recognizes periodic small mouse-only movements
/// typical of a USB mouse jiggler so they do not keep <c>humanIdleMs</c> near
/// zero forever. Any keyboard input, any click/wheel, any stationary event
/// without hook confirmation, or any larger mouse move always counts as human
/// and resets the streak.
/// </summary>
internal sealed class PeriodicMicroMoveDetector
{
    private readonly Lock _gate = new();
    private readonly Func<PeriodicMicroMoveConfig> _configProvider;
    private readonly List<uint> _candidateTicks = new(8);
    private CursorPoint? _lastCursor;
    private bool _hasLastEvaluatedTick;
    private uint _lastEvaluatedTick;
    private bool _lastEvaluatedResult;

    public PeriodicMicroMoveDetector(Func<PeriodicMicroMoveConfig>? configProvider = null)
    {
        _configProvider = configProvider ?? PeriodicMicroMoveConfig.FromEnvironment;
    }

    public PeriodicMicroMoveConfig CurrentConfig => _configProvider();

    public bool Evaluate(uint inputTick, CursorPoint? cursor, string inputKind, bool mouseMoveOnly)
    {
        PeriodicMicroMoveConfig config = _configProvider();
        lock (_gate)
        {
            if (!config.Enabled)
            {
                if (cursor is not null)
                {
                    _lastCursor = cursor;
                }
                _candidateTicks.Clear();
                _hasLastEvaluatedTick = false;
                _lastEvaluatedResult = false;
                return false;
            }

            if (_hasLastEvaluatedTick && inputTick == _lastEvaluatedTick)
            {
                if (cursor is not null)
                {
                    _lastCursor = cursor;
                }
                return _lastEvaluatedResult;
            }

            long distSq = -1;
            if (_lastCursor is not null && cursor is not null)
            {
                long dx = (long)cursor.X - _lastCursor.X;
                long dy = (long)cursor.Y - _lastCursor.Y;
                distSq = dx * dx + dy * dy;
            }

            if (cursor is not null)
            {
                _lastCursor = cursor;
            }
            _hasLastEvaluatedTick = true;
            _lastEvaluatedTick = inputTick;

            bool isDisallowedKind =
                string.Equals(inputKind, UserActivityMonitor.KindKeyboard, StringComparison.Ordinal) ||
                string.Equals(inputKind, UserActivityMonitor.KindOther, StringComparison.Ordinal);

            long maxDistSq = (long)config.MaxDisplacementPx * config.MaxDisplacementPx;
            bool isSmallMove = distSq > 0 && distSq <= maxDistSq;

            if (isDisallowedKind || !mouseMoveOnly || !isSmallMove)
            {
                _candidateTicks.Clear();
                _lastEvaluatedResult = false;
                return false;
            }

            int requiredSamples = Math.Max(2, config.MinIntervals + 1);
            if (_candidateTicks.Count >= requiredSamples)
            {
                _candidateTicks.RemoveAt(0);
            }
            _candidateTicks.Add(inputTick);

            if (_candidateTicks.Count < requiredSamples)
            {
                _lastEvaluatedResult = false;
                return false;
            }

            bool regular = AreIntervalsRegular(_candidateTicks, config);
            _lastEvaluatedResult = regular;
            return regular;
        }
    }

    private static bool AreIntervalsRegular(List<uint> ticks, PeriodicMicroMoveConfig config)
    {
        int intervalCount = ticks.Count - 1;
        if (intervalCount < config.MinIntervals)
        {
            return false;
        }

        Span<long> intervals = intervalCount <= 16
            ? stackalloc long[intervalCount]
            : new long[intervalCount];

        long sum = 0;
        for (int i = 0; i < intervalCount; i++)
        {
            long dt = InputAttributor.AgeMs(ticks[i + 1], ticks[i]);
            if (dt < config.MinPeriodMs || dt > config.MaxPeriodMs)
            {
                return false;
            }
            intervals[i] = dt;
            sum += dt;
        }

        double mean = (double)sum / intervalCount;
        double tolerance = Math.Max(config.MinJitterToleranceMs, mean * config.MaxRelativeJitter);

        for (int i = 0; i < intervalCount; i++)
        {
            if (Math.Abs(intervals[i] - mean) > tolerance)
            {
                return false;
            }
        }

        return true;
    }
}
