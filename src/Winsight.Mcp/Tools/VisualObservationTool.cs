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
using System.Drawing;
using System.Text.Json;
using ModelContextProtocol.Server;
using Winsight.Core.Models;
using Winsight.Core.Modules.Ocr;
using Winsight.Core.Modules.VisualRegression;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

/// <summary>
/// Synchronize on and read what a window draws, for UIs with no UI Automation
/// tree. All three tools only capture; none sends input.
/// </summary>
[McpServerToolType]
public static class VisualObservationTool
{
    private const string RegionDescription =
        "Optional area { x, y, width, height } in window coordinates (same origin as a full-window " +
        "screenshot and window-relative click_point). Default: the whole window.";
    private const string WindowTitleDescription =
        "Restrict the window to those whose title contains this text.";
    private const string ProcessIdDescription =
        "Target process id. Optional: falls back to the session default set by set_target.";
    private const string ThresholdDescription =
        "SSIM below which two captures differ (0 to 1). Default: WINSIGHT_VISUAL_SSIM_THRESHOLD or 0.98.";
    private const string LocalThresholdDescription =
        "Worst 16x16 block SSIM below which two captures differ (0 to 1); catches small changes such as " +
        "one status word. Default: WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD or 0.85.";

    [McpServerTool(Name = "wait_for_visual_change", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Wait until the window, or a region of it, looks different, instead of sleeping. The " +
                 "reference is a capture taken when the call starts, or baselinePath. To wait for the effect " +
                 "of an action, prefer a run_steps 'wait_visual_change' step right after the action: the " +
                 "runner captures the reference before the action, so a fast change is not missed. Change " +
                 "means global SSIM, worst-block SSIM or pixel ratio crossing the compare_visual_baseline " +
                 "thresholds. Returns { condition, met, elapsedMs, frames, ssim, localSsimMin, " +
                 "pixelDiffRatio, region }; fails with timed_out and the same data. Read-only.")]
    public static string WaitForVisualChange(
        VisualWaiter waiter,
        SessionTarget target,
        [Description(RegionDescription)]
        WindowRegion? region = null,
        [Description("Maximum wait in milliseconds (0 to 60000, default 5000).")]
        int timeoutMs = VisualWaitOptions.DefaultTimeoutMs,
        [Description("Milliseconds between captures (20 to 5000, default 100).")]
        int pollMs = VisualWaitOptions.DefaultPollMs,
        [Description(ThresholdDescription)]
        double? threshold = null,
        [Description(LocalThresholdDescription)]
        double? localThreshold = null,
        [Description("Optional PNG to compare against instead of a capture taken now; its size must match " +
                     "the region (or the window).")]
        string? baselinePath = null,
        [Description(WindowTitleDescription)]
        string? windowTitle = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return Serialize(OperationResult.Failure(targetError));
            }
            VisualWaitOptions options = new(timeoutMs, pollMs, SsimThreshold: threshold, LocalSsimThreshold: localThreshold);
            if (string.IsNullOrWhiteSpace(baselinePath))
            {
                return Serialize(waiter.WaitForChange(pid, windowTitle, region, options));
            }
            string fullPath = Path.GetFullPath(baselinePath);
            if (!File.Exists(fullPath))
            {
                return Serialize(OperationResult.Failure($"Baseline image not found: {fullPath}"));
            }
            using Bitmap reference = new(fullPath);
            return Serialize(waiter.WaitForChange(pid, windowTitle, region, options, reference));
        });
    }

    [McpServerTool(Name = "wait_for_visual_stable", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Wait until the window, or a region of it, stops changing (an animation, a redraw or a " +
                 "load has settled), instead of sleeping. Captures every pollMs and succeeds after " +
                 "stableComparisons consecutive comparisons show no change. Returns { condition, met, " +
                 "elapsedMs, frames, ssim, localSsimMin, pixelDiffRatio, region }; fails with timed_out and " +
                 "the same data. Read-only.")]
    public static string WaitForVisualStable(
        VisualWaiter waiter,
        SessionTarget target,
        [Description(RegionDescription)]
        WindowRegion? region = null,
        [Description("Unchanged comparisons in a row required (1 to 50, default 3).")]
        int stableComparisons = VisualWaitOptions.DefaultStableComparisons,
        [Description("Maximum wait in milliseconds (0 to 60000, default 5000).")]
        int timeoutMs = VisualWaitOptions.DefaultTimeoutMs,
        [Description("Milliseconds between captures (20 to 5000, default 100).")]
        int pollMs = VisualWaitOptions.DefaultPollMs,
        [Description(ThresholdDescription)]
        double? threshold = null,
        [Description(LocalThresholdDescription)]
        double? localThreshold = null,
        [Description(WindowTitleDescription)]
        string? windowTitle = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return Serialize(OperationResult.Failure(targetError));
            }
            VisualWaitOptions options = new(timeoutMs, pollMs, stableComparisons, threshold, localThreshold);
            return Serialize(waiter.WaitForStable(pid, windowTitle, region, options));
        });
    }

    [McpServerTool(Name = "read_text_visually", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read the text a window draws with the OCR engine built into Windows (offline, nothing " +
                 "sent anywhere), for UIs that expose no UI Automation text: a status line, a table cell, an " +
                 "error message. Returns { language, text, lines:[{ text, bounds, words:[{ text, bounds }] }], " +
                 "region, scale, requestedLanguage?, warning? } with every rectangle in window coordinates, ready " +
                 "for click_point. When the requested language has no OCR component, an installed one of the " +
                 "same script is used: language is then the recognizer actually used, requestedLanguage the " +
                 "requested tag, and warning starts with ocr_language_fallback. warning also starts with " +
                 "region_uniform when the read area is a single color (check the application, for example a " +
                 "stale session, before concluding nothing is displayed); the check covers the whole captured " +
                 "area, so on a full-window read the title bar usually prevents it: pass a region to check a " +
                 "specific pane. Fails with ocr_unavailable, listing " +
                 "the installed OCR languages, when no recognizer of the same script is installed. To assert " +
                 "inside a batch, use a run_steps 'assert' step with operator text_visible. Read-only.")]
    public static string ReadTextVisually(
        VisualTextReader reader,
        SessionTarget target,
        [Description(RegionDescription)]
        WindowRegion? region = null,
        [Description("BCP-47 language of the text, for example en-US or fr-FR. Default: the user profile languages.")]
        string? language = null,
        [Description("Enlargement before recognition (1 to 4, default 2); raise it for very small text.")]
        int scale = VisualTextReader.DefaultScale,
        [Description(WindowTitleDescription)]
        string? windowTitle = null,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return Serialize(OperationResult.Failure(targetError));
            }
            return Serialize(reader.Read(pid, windowTitle, region, language, scale));
        });
    }

    private static string Serialize(OperationResult result) => JsonSerializer.Serialize(result, JsonOptions.Default);
}
