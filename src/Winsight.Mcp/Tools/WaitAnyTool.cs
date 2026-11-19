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

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.VisualRegression;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

public sealed record WaitAnyBranch(
    string Kind,
    string? Label = null,
    ElementLocator? Locator = null,
    string? WindowTitle = null,
    string? ExpectedText = null);

[McpServerToolType]
public static class WaitAnyTool
{
    private const int DefaultTimeoutMs = 5000;
    private const int MaxTimeoutMs = 60000;
    private const int DefaultPollIntervalMs = 150;
    private const int MaxBranches = 10;

    [McpServerTool(Name = "wait_any", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Wait until the FIRST of 1..10 competing conditions holds (e.g. success text OR error dialog OR window opened OR visual change). " +
                 "Supported branch kinds: element_exists, element_not_exists, value_contains, window_open, window_closed, ocr_contains, visual_change. " +
                 "Returns { matched: true, matchedIndex, label, kind, elapsedMs, evidence } as soon as any branch matches, or fails with timed_out. Read-only.")]
    public static Task<string> WaitAny(
        SceneStore store,
        SessionTarget target,
        [Description("1..10 candidate conditions to race in parallel on each poll tick.")]
        WaitAnyBranch[] conditions,
        [Description("Maximum wait in milliseconds (100..60000, default 5000).")]
        int timeoutMs = DefaultTimeoutMs,
        [Description("Polling interval in milliseconds (50..2000, default 150).")]
        int pollIntervalMs = DefaultPollIntervalMs,
        [Description("Target process id. Optional; falls back to set_target.")]
        int? processId = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
        => ToolSafety.SafeInvokeAsync(async () =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return Serialize(AgentFailure.Repairable("missing_target", targetError, "target_resolution", "set_target"));
            }
            if (conditions is null || conditions.Length is < 1 or > MaxBranches)
            {
                return Serialize(OperationResult.Failure($"conditions must contain 1..{MaxBranches} items."));
            }
            if (timeoutMs is < 100 or > MaxTimeoutMs)
            {
                return Serialize(OperationResult.Failure($"timeoutMs must be 100..{MaxTimeoutMs}."));
            }
            if (pollIntervalMs is < 50 or > 2000)
            {
                return Serialize(OperationResult.Failure("pollIntervalMs must be 50..2000."));
            }

            for (int i = 0; i < conditions.Length; i++)
            {
                string? error = ValidateBranch(conditions[i]);
                if (error is not null)
                {
                    return Serialize(OperationResult.Failure($"conditions[{i}]: {error}"));
                }
            }

            Dictionary<int, CapturedFrame> visualBaselines = new();
            try
            {
                for (int i = 0; i < conditions.Length; i++)
                {
                    if (conditions[i].Kind == "visual_change")
                    {
                        visualBaselines[i] = store.Services.Capture.CaptureFrame(pid, conditions[i].WindowTitle);
                    }
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                long lastProgressMs = 0;
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int i = 0; i < conditions.Length; i++)
                    {
                        (bool matched, object? evidence) = EvaluateBranch(
                            store.Services, pid, conditions[i], visualBaselines.GetValueOrDefault(i));
                        if (matched)
                        {
                            progress?.Report(new ProgressNotificationValue
                            {
                                Progress = timeoutMs,
                                Total = timeoutMs,
                                Message = $"Matched branch {i} ({conditions[i].Label ?? conditions[i].Kind}).",
                            });
                            return Serialize(OperationResult.OkWith(new
                            {
                                matched = true,
                                matchedIndex = i,
                                label = conditions[i].Label,
                                kind = conditions[i].Kind,
                                elapsedMs = stopwatch.ElapsedMilliseconds,
                                evidence,
                            }));
                        }
                    }

                    if (progress is not null && stopwatch.ElapsedMilliseconds - lastProgressMs >= 1000)
                    {
                        lastProgressMs = stopwatch.ElapsedMilliseconds;
                        progress.Report(new ProgressNotificationValue
                        {
                            Progress = Math.Min(timeoutMs, (float)stopwatch.ElapsedMilliseconds),
                            Total = timeoutMs,
                            Message = $"Waiting for any of {conditions.Length} conditions ({stopwatch.ElapsedMilliseconds}/{timeoutMs} ms)...",
                        });
                    }

                    await Task.Delay(Math.Min(pollIntervalMs, Math.Max(1, timeoutMs - (int)stopwatch.ElapsedMilliseconds)), cancellationToken)
                        .ConfigureAwait(false);
                } while (stopwatch.ElapsedMilliseconds < timeoutMs);

                return Serialize(OperationResult.FailureWith(
                    $"timed_out: none of the {conditions.Length} conditions held within {timeoutMs} ms.",
                    new { matched = false, elapsedMs = stopwatch.ElapsedMilliseconds, branchCount = conditions.Length }));
            }
            finally
            {
                foreach (CapturedFrame frame in visualBaselines.Values)
                {
                    frame.Dispose();
                }
            }
        });

    internal static string? ValidateBranch(WaitAnyBranch branch)
    {
        if (branch is null || string.IsNullOrWhiteSpace(branch.Kind)) return "kind is required.";
        return branch.Kind switch
        {
            "element_exists" or "element_not_exists" => branch.Locator is null ? "locator is required." : null,
            "value_contains" => branch.Locator is null || string.IsNullOrEmpty(branch.ExpectedText) ? "locator and expectedText are required." : null,
            "window_open" or "window_closed" => string.IsNullOrWhiteSpace(branch.WindowTitle) ? "windowTitle is required." : null,
            "ocr_contains" => string.IsNullOrWhiteSpace(branch.ExpectedText) ? "expectedText is required." : null,
            "visual_change" => null,
            _ => $"Unknown branch kind '{branch.Kind}'. Accepted: element_exists, element_not_exists, value_contains, window_open, window_closed, ocr_contains, visual_change.",
        };
    }

    private static (bool Matched, object? Evidence) EvaluateBranch(
        SceneServices services,
        int pid,
        WaitAnyBranch branch,
        CapturedFrame? visualBaseline)
    {
        switch (branch.Kind)
        {
            case "element_exists":
            {
                OperationResult read = services.ElementState.Read(pid, branch.Locator!, branch.WindowTitle);
                return (read.Success, read.Data);
            }
            case "element_not_exists":
            {
                OperationResult read = services.ElementState.Read(pid, branch.Locator!, branch.WindowTitle);
                return (!read.Success, read.Error);
            }
            case "value_contains":
            {
                OperationResult read = services.ElementState.Read(pid, branch.Locator!, branch.WindowTitle);
                if (!read.Success || read.Data is not ElementState state) return (false, null);
                bool hit = (state.Value?.Contains(branch.ExpectedText!, StringComparison.OrdinalIgnoreCase) ?? false) ||
                           state.Name.Contains(branch.ExpectedText!, StringComparison.OrdinalIgnoreCase);
                return (hit, state);
            }
            case "window_open":
            {
                IReadOnlyList<WindowDescriptor> windows = services.Windows.List(pid);
                WindowDescriptor? found = windows.FirstOrDefault(w => w.Title.Contains(branch.WindowTitle!, StringComparison.OrdinalIgnoreCase));
                return (found is not null, found);
            }
            case "window_closed":
            {
                IReadOnlyList<WindowDescriptor> windows = services.Windows.List(pid);
                bool stillOpen = windows.Any(w => w.Title.Contains(branch.WindowTitle!, StringComparison.OrdinalIgnoreCase));
                return (!stillOpen, windows);
            }
            case "ocr_contains":
            {
                OperationResult ocr = services.Ocr.Read(pid, branch.WindowTitle);
                if (!ocr.Success || ocr.Data is not VisualTextResult text) return (false, null);
                return (text.Text.Contains(branch.ExpectedText!, StringComparison.OrdinalIgnoreCase), text);
            }
            case "visual_change":
            {
                if (visualBaseline is null) return (false, null);
                using CapturedFrame current = services.Capture.CaptureFrame(pid, branch.WindowTitle);
                VisualDiffResult diff = services.VisualDiffer.Compare(visualBaseline.Bitmap, current.Bitmap, VisualDiffOptions.FromEnvironment());
                return (!diff.Passed, new { diff.Ssim, diff.LocalSsimMin, diff.PixelDiffRatio });
            }
            default:
                return (false, null);
        }
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions.Default);
}
