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
    public const string UserActiveCode = "user_active";

    public static int? YieldToUserMs => Parse(Environment.GetEnvironmentVariable(YieldToUserVariable));

    internal static int? Parse(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : null;

    /// <summary>True when a human input is more recent than the yield threshold.</summary>
    public static bool IsActive(long? humanIdleMs, int? yieldToUserMs) =>
        yieldToUserMs is int threshold && humanIdleMs is long idle && idle < threshold;

    /// <summary>The refusal for a screen-changing action, or null when it may proceed.</summary>
    public static string? CheckYield() => CheckYield(YieldToUserMs, UserActivityMonitor.ReadHumanIdleMs);

    internal static string? CheckYield(int? yieldToUserMs, Func<long?> readHumanIdleMs)
    {
        if (yieldToUserMs is not int threshold)
        {
            return null;
        }
        long? idle = readHumanIdleMs();
        return IsActive(idle, threshold)
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{UserActiveCode}: human input {idle} ms ago, under {YieldToUserVariable}={threshold}. " +
                $"Someone is using this desktop: call wait_for_user_idle, then observe again before acting.")
            : null;
    }
}
