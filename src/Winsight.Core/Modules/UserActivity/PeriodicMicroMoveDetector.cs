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
    double MaxRelativeJitter = UserActivityPolicy.DefaultPeriodicMicroMoveTolerancePct / 100.0,
    long MinJitterToleranceMs = 100)
{
    public static PeriodicMicroMoveConfig FromEnvironment() => new(
        Enabled: UserActivityPolicy.IsPeriodicMicroMoveFilterEnabled,
        MaxDisplacementPx: UserActivityPolicy.PeriodicMicroMoveThresholdPx,
        MaxRelativeJitter: UserActivityPolicy.PeriodicMicroMoveRelativeTolerance);
}

/// <summary>
/// Opt-in classifier that recognizes periodic small mouse-only movements
/// typical of a USB mouse jiggler so they do not keep <c>humanIdleMs</c> near
/// zero forever. Any keyboard input, any click/wheel, any stationary event
/// without hook confirmation, or any larger mouse move always counts as human.
/// Once a jiggler period is learned, Winsight-injected inputs preserve the
/// learned history and after a human interruption classification resumes as
/// soon as a micro-move matches the learned period and amplitude bound.
/// </summary>
internal sealed class PeriodicMicroMoveDetector
{
    private const int MaxHarmonicResumeCount = 4;

    private readonly Lock _gate = new();
    private readonly Func<PeriodicMicroMoveConfig> _configProvider;
    private readonly List<uint> _candidateTicks = new(8);
    private CursorPoint? _lastCursor;
    private bool _hasLastEvaluatedTick;
    private uint _lastEvaluatedTick;
    private bool _lastEvaluatedResult;
    private bool _hasLearnedPattern;
    private double _learnedPeriodMs;
    private uint _lastJigglerTick;

    public PeriodicMicroMoveDetector(Func<PeriodicMicroMoveConfig>? configProvider = null)
    {
        _configProvider = configProvider ?? PeriodicMicroMoveConfig.FromEnvironment;
    }

    public PeriodicMicroMoveConfig CurrentConfig => _configProvider();

    /// <summary>
    /// Update the reference cursor position after a Winsight-injected or
    /// other-injected input without resetting the learned pattern or candidate
    /// history, so the next jiggler micro-move is measured from the new cursor
    /// location rather than the pre-injection location.
    /// </summary>
    public void ObserveNonHumanCursor(CursorPoint? cursor)
    {
        if (cursor is null)
        {
            return;
        }
        lock (_gate)
        {
            _lastCursor = cursor;
        }
    }

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
                _hasLearnedPattern = false;
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
                // Real human input breaks the active candidate streak, but preserves
                // _hasLearnedPattern / _learnedPeriodMs / _lastJigglerTick so jiggler
                // classification resumes immediately once the jiggler ticks again.
                _candidateTicks.Clear();
                _lastEvaluatedResult = false;
                return false;
            }

            // Fast-path resume against the previously learned jiggler period.
            if (_hasLearnedPattern && MatchesLearnedPattern(inputTick, config, out double observedPeriod))
            {
                _learnedPeriodMs = (_learnedPeriodMs * 0.75) + (observedPeriod * 0.25);
                _lastJigglerTick = inputTick;
                AppendCandidate(inputTick, config);
                _lastEvaluatedResult = true;
                return true;
            }

            // If we have a learned pattern and a single prior candidate after a long
            // human interruption, check if this single interval matches the learned period.
            if (_hasLearnedPattern && _candidateTicks.Count >= 1)
            {
                long singleDt = InputAttributor.AgeMs(inputTick, _candidateTicks[^1]);
                if (MatchesSinglePeriod(singleDt, _learnedPeriodMs, config))
                {
                    _learnedPeriodMs = (_learnedPeriodMs * 0.75) + (singleDt * 0.25);
                    _lastJigglerTick = inputTick;
                    AppendCandidate(inputTick, config);
                    _lastEvaluatedResult = true;
                    return true;
                }
            }

            AppendCandidate(inputTick, config);

            int requiredSamples = Math.Max(2, config.MinIntervals + 1);
            if (_candidateTicks.Count < requiredSamples)
            {
                _lastEvaluatedResult = false;
                return false;
            }

            bool regular = TryComputeRegularPeriod(_candidateTicks, config, out double meanPeriod);
            if (regular)
            {
                _hasLearnedPattern = true;
                _learnedPeriodMs = meanPeriod;
                _lastJigglerTick = inputTick;
            }
            _lastEvaluatedResult = regular;
            return regular;
        }
    }

    private void AppendCandidate(uint inputTick, PeriodicMicroMoveConfig config)
    {
        int requiredSamples = Math.Max(2, config.MinIntervals + 1);
        if (_candidateTicks.Count >= requiredSamples)
        {
            _candidateTicks.RemoveAt(0);
        }
        _candidateTicks.Add(inputTick);
    }

    private bool MatchesLearnedPattern(uint inputTick, PeriodicMicroMoveConfig config, out double observedPeriod)
    {
        observedPeriod = _learnedPeriodMs;
        long dt = InputAttributor.AgeMs(inputTick, _lastJigglerTick);
        if (dt < config.MinPeriodMs)
        {
            return false;
        }

        if (MatchesSinglePeriod(dt, _learnedPeriodMs, config))
        {
            observedPeriod = dt;
            return true;
        }

        // Allow up to MaxHarmonicResumeCount missed jiggler ticks during an interruption
        // (e.g. a Winsight action or short human interaction).
        double ratio = dt / _learnedPeriodMs;
        int multiple = (int)Math.Round(ratio);
        if (multiple >= 2 && multiple <= MaxHarmonicResumeCount)
        {
            double expected = multiple * _learnedPeriodMs;
            double tolerance = Math.Max(config.MinJitterToleranceMs, _learnedPeriodMs * config.MaxRelativeJitter);
            if (Math.Abs(dt - expected) <= tolerance)
            {
                observedPeriod = (double)dt / multiple;
                return true;
            }
        }

        return false;
    }

    private static bool MatchesSinglePeriod(long dt, double referencePeriodMs, PeriodicMicroMoveConfig config)
    {
        if (dt < config.MinPeriodMs || dt > config.MaxPeriodMs)
        {
            return false;
        }
        double tolerance = Math.Max(config.MinJitterToleranceMs, referencePeriodMs * config.MaxRelativeJitter);
        return Math.Abs(dt - referencePeriodMs) <= tolerance;
    }

    private static bool TryComputeRegularPeriod(
        List<uint> ticks,
        PeriodicMicroMoveConfig config,
        out double meanPeriodMs)
    {
        meanPeriodMs = 0;
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

        meanPeriodMs = mean;
        return true;
    }
}
