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
/// Centralized constants for the UI Automation module so no timeout, poll
/// interval, or result cap is hardcoded at call sites. The wait timeout can
/// be overridden per call via tool parameters and globally via the
/// <c>WINSIGHT_UIA_WAIT_TIMEOUT_MS</c> environment variable.
/// </summary>
public static class UiaDefaults
{
    /// <summary>Name of the environment variable overriding the default wait timeout.</summary>
    public const string WaitTimeoutEnvironmentVariable = "WINSIGHT_UIA_WAIT_TIMEOUT_MS";

    /// <summary>Fallback wait timeout when no override is provided.</summary>
    public const int FallbackWaitTimeoutMs = 10_000;

    /// <summary>Upper bound accepted for a single wait call.</summary>
    public const int MaxWaitTimeoutMs = 120_000;

    /// <summary>Default delay between condition polls.</summary>
    public const int DefaultPollIntervalMs = 250;

    /// <summary>Lower bound accepted for the poll interval.</summary>
    public const int MinPollIntervalMs = 50;

    /// <summary>Maximum number of candidates reported on ambiguous or failed lookups.</summary>
    public const int MaxReportedCandidates = 10;

    /// <summary>Maximum descendant depth inspected for semantic descendant text locators.</summary>
    public const int MaxDescendantTextDepth = 5;

    /// <summary>Name of the environment variable overriding the UIA scene traversal time budget.</summary>
    public const string SceneTraversalBudgetEnvironmentVariable = "WINSIGHT_SCENE_TRAVERSAL_BUDGET_MS";

    /// <summary>Default wall-clock budget for a single UIA tree traversal.</summary>
    public const int DefaultSceneTraversalBudgetMs = 5_000;

    /// <summary>Lower bound accepted for the UIA traversal time budget.</summary>
    public const int MinSceneTraversalBudgetMs = 100;

    /// <summary>Upper bound accepted for the UIA traversal time budget.</summary>
    public const int MaxSceneTraversalBudgetMs = 120_000;

    /// <summary>Hard cap for a single UIA breadth-first walk.</summary>
    public const int MaxTraversalNodes = 10_000;

    /// <summary>Hard cap for descendant text collection below one candidate element.</summary>
    public const int MaxDescendantTextNodes = 500;

    /// <summary>Upper bound for describe_ui output.</summary>
    public const int MaxDescribeResults = 1_000;

    /// <summary>
    /// Resolve the effective default wait timeout: environment override when
    /// set and valid, fallback constant otherwise.
    /// </summary>
    public static int ResolveWaitTimeoutMs()
    {
        string? raw = Environment.GetEnvironmentVariable(WaitTimeoutEnvironmentVariable);
        if (int.TryParse(raw, out int parsed) && parsed > 0 && parsed <= MaxWaitTimeoutMs)
        {
            return parsed;
        }
        return FallbackWaitTimeoutMs;
    }

    /// <summary>
    /// Resolve the effective UIA traversal time budget in milliseconds:
    /// environment override when set and within [MinSceneTraversalBudgetMs, MaxSceneTraversalBudgetMs],
    /// default constant otherwise.
    /// </summary>
    public static int ResolveSceneTraversalBudgetMs(Func<string, string?>? getEnvironmentVariable = null)
    {
        string? raw = (getEnvironmentVariable ?? Environment.GetEnvironmentVariable)(
            SceneTraversalBudgetEnvironmentVariable);
        if (int.TryParse(raw, out int parsed) &&
            parsed >= MinSceneTraversalBudgetMs &&
            parsed <= MaxSceneTraversalBudgetMs)
        {
            return parsed;
        }
        return DefaultSceneTraversalBudgetMs;
    }
}
