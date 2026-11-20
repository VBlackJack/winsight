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

namespace Winsight.Core.Modules.UserActivity;

/// <summary>
/// Evaluates whether an enabled low-level input hook is still healthy or has
/// been silently removed by Windows (for example after exceeding
/// <c>LowLevelHooksTimeout</c>).
/// </summary>
internal sealed class InputHookHealthTracker
{
    public const uint HeartbeatStaleMs = 15_000;
    public const int MissedCursorMovesThreshold = 2;
    public const int MissedAnyInputsThreshold = 3;

    public const string WarningInstallOrInactive =
        "WINSIGHT_INPUT_HOOK=1 is enabled, but the low-level input hook is not active " +
        "(installation failed or pump thread exited).";

    public const string WarningHeartbeatStalled =
        "WINSIGHT_INPUT_HOOK=1 low-level input hook message pump heartbeat stalled; " +
        "Windows may have removed the hook due to LowLevelHooksTimeout.";

    public const string WarningSilentlyRemoved =
        "WINSIGHT_INPUT_HOOK=1 low-level input hook appears to have been silently removed by Windows " +
        "(recent input events did not reach the hook callback; possible LowLevelHooksTimeout). " +
        "Restart Winsight to restore hook attribution.";

    private readonly Lock _gate = new();
    private bool _hasObservedTick;
    private uint _lastObservedTick;
    private int _missedCursorMoves;
    private int _missedAnyInputs;
    private bool _degraded;

    public (bool EffectiveActive, string? Warning) Evaluate(
        uint now,
        uint lastInputTick,
        bool cursorMoved,
        bool lastInputIsWinsight,
        HookInputState hook)
    {
        if (!hook.Active)
        {
            string? warning = hook.Warning ?? (hook.Enabled ? WarningInstallOrInactive : null);
            return (false, warning);
        }

        if (hook.HasHeartbeat && InputAttributor.AgeMs(now, hook.LastHeartbeatTick) > HeartbeatStaleMs)
        {
            return (false, WarningHeartbeatStalled);
        }

        bool hookMatchesLastInput = hook.HasEvent &&
            Math.Abs((long)InputAttributor.AgeMs(now, lastInputTick) - InputAttributor.AgeMs(now, hook.LastEventTick)) <=
            InputAttributor.InjectionMarginMs;

        lock (_gate)
        {
            if (hookMatchesLastInput)
            {
                _hasObservedTick = true;
                _lastObservedTick = lastInputTick;
                _missedCursorMoves = 0;
                _missedAnyInputs = 0;
                _degraded = false;
                return (true, null);
            }

            if (!lastInputIsWinsight && (!_hasObservedTick || lastInputTick != _lastObservedTick))
            {
                _hasObservedTick = true;
                _lastObservedTick = lastInputTick;
                _missedAnyInputs++;
                if (cursorMoved)
                {
                    _missedCursorMoves++;
                }

                if (_missedCursorMoves >= MissedCursorMovesThreshold ||
                    _missedAnyInputs >= MissedAnyInputsThreshold)
                {
                    _degraded = true;
                }
            }

            return _degraded
                ? (false, WarningSilentlyRemoved)
                : (true, null);
        }
    }
}
