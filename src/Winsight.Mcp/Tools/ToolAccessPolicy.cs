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
using Winsight.Core.Models;
using Winsight.Core.Modules.UserActivity;

namespace Winsight.Mcp.Tools;

internal static class ToolAccessPolicy
{
    private static readonly char[] ListSeparators = { ';', ',' };

    public const string AllowedProcessIdsVariable = "WINSIGHT_ALLOWED_PROCESS_IDS";
    public const string AllowedProcessNamesVariable = "WINSIGHT_ALLOWED_PROCESS_NAMES";
    public const string AllowedExecutablePathsVariable = "WINSIGHT_ALLOWED_EXECUTABLES";
    public const string DryRunVariable = "WINSIGHT_DRY_RUN";

    public static bool IsDryRunEnabled =>
        IsTruthy(Environment.GetEnvironmentVariable(DryRunVariable));

    public static bool TryAuthorizeProcess(int processId, out string error)
    {
        error = string.Empty;

        string[] allowedIds = SplitList(Environment.GetEnvironmentVariable(AllowedProcessIdsVariable));
        if (allowedIds.Length > 0 &&
            !allowedIds.Any(id => int.TryParse(id, out int allowed) && allowed == processId))
        {
            error = $"Process {processId} is denied by {AllowedProcessIdsVariable}.";
            return false;
        }

        string[] allowedNames = SplitList(Environment.GetEnvironmentVariable(AllowedProcessNamesVariable));
        using Process? process = (allowedIds.Length > 0 || allowedNames.Length > 0)
            ? TryGetProcessById(processId)
            : null;
        if ((allowedIds.Length > 0 || allowedNames.Length > 0) && process is null)
        {
            error = $"Process {processId} is not running.";
            return false;
        }

        if (allowedNames.Length > 0)
        {
            string processName = process!.ProcessName;

            if (!allowedNames.Any(name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"Process {processId} ({processName}) is denied by {AllowedProcessNamesVariable}.";
                return false;
            }
        }

        return true;
    }

    private static Process? TryGetProcessById(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static OperationResult? DryRunMutation(string action, object? preview)
    {
        return IsDryRunEnabled
            ? OperationResult.OkWith(new { dryRun = true, action, preview })
            : null;
    }

    /// <summary>
    /// Gate for an action that changes the screen: the dry-run preview when dry-run
    /// is on, else a <c>user_active</c> refusal when the yield-to-user policy says
    /// a human is using the desktop, else null (proceed).
    /// </summary>
    public static OperationResult? GuardMutation(string action, object? preview) =>
        DryRunMutation(action, preview) ??
        (UserActivityPolicy.CheckYield() is { } userActive ? OperationResult.Failure(userActive) : null);

    public static bool TryAuthorizeLaunch(string executablePath, out string error)
    {
        error = string.Empty;
        string[] allowedPaths = SplitList(Environment.GetEnvironmentVariable(AllowedExecutablePathsVariable));
        if (allowedPaths.Length == 0)
        {
            return true;
        }

        string fullPath = Path.GetFullPath(executablePath);
        if (allowedPaths.Any(path =>
                string.Equals(Path.GetFullPath(path), fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        error = $"Executable \"{fullPath}\" is denied by {AllowedExecutablePathsVariable}.";
        return false;
    }

    private static string[] SplitList(string? raw)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split(ListSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsTruthy(string? raw)
    {
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "on", StringComparison.OrdinalIgnoreCase);
    }
}
