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

using System.Globalization;

namespace Winsight.Core.Modules.UserActivity;

/// <summary>
/// Optional politeness on a shared desktop: when
/// <see cref="YieldToUserVariable"/> holds a positive number of milliseconds,
/// every action that changes the screen fails with <c>user_active</c> while the
/// last human input is more recent than that. Unset, zero or invalid disables it.
/// </summary>
public static class UserActivityPolicy
{
    public const string YieldToUserVariable = "WINSIGHT_YIELD_TO_USER_MS";
    public const string YieldOverrideVariable = "WINSIGHT_YIELD_OVERRIDE";
    public const string InputHookVariable = LowLevelInputHook.InputHookVariable;
    public const string IgnorePeriodicMicroMovesVariable = "WINSIGHT_IGNORE_PERIODIC_MICRO_MOVES";
    public const string PeriodicMicroMoveMaxPxVariable = "WINSIGHT_PERIODIC_MICRO_MOVE_MAX_PX";
    public const string PeriodicMicroMoveTolerancePctVariable = "WINSIGHT_PERIODIC_MICRO_MOVE_TOLERANCE_PCT";
    public const string ActivitySampleMsVariable = "WINSIGHT_ACTIVITY_SAMPLE_MS";
    public const int DefaultPeriodicMicroMoveMaxPx = 15;
    public const int MaxPeriodicMicroMoveMaxPx = 200;
    public const int DefaultPeriodicMicroMoveTolerancePct = 25;
    public const int MinPeriodicMicroMoveTolerancePct = 5;
    public const int MaxPeriodicMicroMoveTolerancePct = 50;
    public const int DefaultActivitySampleMs = 250;
    public const int MinActivitySampleMs = 50;
    public const int MaxActivitySampleMs = 10_000;
    public const string UserActiveCode = "user_active";
    public const string SessionLockedCode = "session_locked";

    public static int? YieldToUserMs => Parse(Environment.GetEnvironmentVariable(YieldToUserVariable));

    public static bool IsInputHookEnabled => LowLevelInputHook.IsEnabledByEnvironment;

    public static bool IsPeriodicMicroMoveFilterEnabled =>
        ParseBool(Environment.GetEnvironmentVariable(IgnorePeriodicMicroMovesVariable));

    public static int PeriodicMicroMoveThresholdPx =>
        ParsePeriodicMicroMoveMaxPx(Environment.GetEnvironmentVariable(PeriodicMicroMoveMaxPxVariable));

    public static double PeriodicMicroMoveRelativeTolerance =>
        ParsePeriodicMicroMoveTolerancePct(Environment.GetEnvironmentVariable(PeriodicMicroMoveTolerancePctVariable)) / 100.0;

    public static int ActivitySampleMs =>
        ParseActivitySampleMs(Environment.GetEnvironmentVariable(ActivitySampleMsVariable));

    internal static int ParsePeriodicMicroMoveMaxPx(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) &&
        value >= 1 &&
        value <= MaxPeriodicMicroMoveMaxPx
            ? value
            : DefaultPeriodicMicroMoveMaxPx;

    internal static int ParsePeriodicMicroMoveTolerancePct(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) &&
        value >= MinPeriodicMicroMoveTolerancePct &&
        value <= MaxPeriodicMicroMoveTolerancePct
            ? value
            : DefaultPeriodicMicroMoveTolerancePct;

    internal static int ParseActivitySampleMs(string? raw)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            if (value == 0)
            {
                return 0;
            }
            if (value >= MinActivitySampleMs && value <= MaxActivitySampleMs)
            {
                return value;
            }
        }
        return DefaultActivitySampleMs;
    }

    /// <summary>
    /// Host-configured per-session override enabled exclusively by the human
    /// through the server environment (<see cref="YieldOverrideVariable"/>).
    /// Never exposed as a tool argument.
    /// </summary>
    public static bool IsYieldOverridden =>
        ParseBool(Environment.GetEnvironmentVariable(YieldOverrideVariable));

    internal static int? Parse(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : null;

    internal static bool ParseBool(string? raw) =>
        string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(raw, "on", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a human input is more recent than the yield threshold.</summary>
    public static bool IsActive(long? humanIdleMs, int? yieldToUserMs) =>
        yieldToUserMs is int threshold && humanIdleMs is long idle && idle < threshold;

    /// <summary>The refusal for a screen-changing action, or null when it may proceed.</summary>
    public static string? CheckYield() =>
        CheckYield(YieldToUserMs, UserActivityMonitor.ReadHumanIdleMs, IsYieldOverridden, Console.Error.WriteLine);

    internal static string? CheckYield(int? yieldToUserMs, Func<long?> readHumanIdleMs) =>
        CheckYield(yieldToUserMs, readHumanIdleMs, IsYieldOverridden, Console.Error.WriteLine);

    internal static string? CheckYield(
        int? yieldToUserMs,
        Func<long?> readHumanIdleMs,
        bool yieldOverridden,
        Action<string>? logOverride = null)
    {
        if (yieldToUserMs is not int threshold)
        {
            return null;
        }
        long? idle = readHumanIdleMs();
        if (!IsActive(idle, threshold))
        {
            return null;
        }
        if (yieldOverridden)
        {
            logOverride?.Invoke(string.Create(
                CultureInfo.InvariantCulture,
                $"[winsight] yield_override: bypassed {UserActiveCode} (humanIdleMs={idle}, " +
                $"{YieldToUserVariable}={threshold}) because {YieldOverrideVariable} is enabled by host configuration."));
            return null;
        }
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{UserActiveCode}: human input {idle} ms ago, under {YieldToUserVariable}={threshold}. " +
            $"Someone is using this desktop: call wait_for_user_idle with a real timeout, then observe_scene again before acting.");
    }
}
