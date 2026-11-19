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
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.VisualRegression;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class CompareVisualBaselineTool
{
    public const string BaselineDirectoryVariable = "WINSIGHT_VISUAL_BASELINE_DIR";

    private const string CandidatePrefix = "visual-candidate-";
    private const string DiffPrefix = "visual-diff-";
    private const string DefaultBaselineSubdirectory = "winsight\\visual-baselines";
    private const string PngExtension = ".png";
    private const string MediaTypePng = "image/png";

    [McpServerTool(Name = "compare_visual_baseline", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Capture the target process window, compare it to an approved PNG baseline, " +
                 "and return an OperationResult JSON wrapping a VisualDiffResult. Baselines are " +
                 "resolved under WINSIGHT_VISUAL_BASELINE_DIR; when unset, they live under " +
                 "%LOCALAPPDATA%\\winsight\\visual-baselines. If the baseline key does not exist, " +
                 "or updateBaseline=true, the current capture is copied as the baseline and the " +
                 "result verdict is baseline_created or baseline_updated instead of pass/fail. " +
                 "The capture is the window CLIENT AREA only (no title bar/borders) for determinism. " +
                 "Comparison uses pure .NET SSIM (Gaussian 11x11, sigma 1.5), global and worst-block " +
                 "local, plus a pixelmatch-style YIQ pixel diff with anti-aliased-edge detection. " +
                 "Thresholds default from WINSIGHT_VISUAL_SSIM_THRESHOLD (0.98), " +
                 "WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD (0.85), WINSIGHT_VISUAL_PIXEL_THRESHOLD (0.005), " +
                 "and WINSIGHT_VISUAL_COLOR_THRESHOLD (0.1). Diff heatmaps are written under " +
                 "WINSIGHT_OUTPUT_DIR. Ignore regions are rectangles in screenshot pixel coordinates " +
                 "for clocks, cursors, animations, or other dynamic UI.")]
    public static CallToolResult CompareVisualBaseline(
        WindowCapture capture,
        VisualDiffer differ,
        SessionTarget target,
        [Description("Relative logical baseline key, for example 'counter-initial.png' or " +
                     "'SampleWpfApp/counter-initial.png'. The .png extension is added when omitted.")]
        string baselineKey,
        [Description("When true, replace the baseline with the current capture and return " +
                     "verdict=baseline_updated.")]
        bool updateBaseline = false,
        [Description("Optional SSIM threshold override. Default comes from WINSIGHT_VISUAL_SSIM_THRESHOLD, " +
                     "falling back to 0.98.")]
        double? ssimThreshold = null,
        [Description("Optional local (worst-block) SSIM threshold override, 0..1. Default comes from " +
                     "WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD, falling back to 0.85.")]
        double? localSsimThreshold = null,
        [Description("Optional pixel-diff ratio threshold override. Default comes from " +
                     "WINSIGHT_VISUAL_PIXEL_THRESHOLD, falling back to 0.005.")]
        double? pixelDiffThreshold = null,
        [Description("Optional pixelmatch color threshold override, 0..1. Default comes from " +
                     "WINSIGHT_VISUAL_COLOR_THRESHOLD, falling back to 0.1.")]
        double? colorThreshold = null,
        [Description("Optional rectangles to exclude from SSIM and pixel diff, in screenshot pixels.")]
        IReadOnlyList<VisualIgnoreRegion>? ignoreRegions = null,
        [Description("Attach the diff heatmap PNG as an inline image when a comparison runs (default " +
                     "true). Lets you see where the UI changed without opening the file.")]
        bool includeImage = true,
        [Description("Target process id (from list_windows). Optional; falls back to the session default set by set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvokeContent(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content =
                    {
                        new TextContentBlock
                        {
                            Text = JsonSerializer.Serialize(
                                OperationResult.Failure(targetError), JsonOptions.Default),
                        },
                    },
                };
            }
            string baselinePath = ResolveBaselinePath(baselineKey);
            if (ToolAccessPolicy.DryRunMutation(
                    "compare_visual_baseline",
                    new { processId = pid, baselinePath, updateBaseline, baselineExists = File.Exists(baselinePath) })
                is { } dryRun)
            {
                return new CallToolResult
                {
                    Content = { new TextContentBlock { Text = JsonSerializer.Serialize(dryRun, JsonOptions.Default) } },
                };
            }
            string candidatePath = ScreenshotOutput.BuildTimestampedPath(CandidatePrefix);
            capture.CaptureClientArea(pid, candidatePath);

            if (!File.Exists(baselinePath) || updateBaseline)
            {
                var baselineDirectory = Path.GetDirectoryName(baselinePath);
                if (!string.IsNullOrEmpty(baselineDirectory))
                {
                    Directory.CreateDirectory(baselineDirectory);
                }

                File.Copy(candidatePath, baselinePath, overwrite: true);
                string verdict = updateBaseline
                    ? VisualDiffVerdicts.BaselineUpdated
                    : VisualDiffVerdicts.BaselineCreated;
                string action = updateBaseline ? "updated" : "created";
                var created = new VisualDiffResult(
                    Passed: true,
                    Ssim: 1.0,
                    LocalSsimMin: 1.0,
                    PixelDiffRatio: 0.0,
                    DiffMapPath: null,
                    Reasoning: $"Visual baseline {action} at {baselinePath}.",
                    Verdict: verdict,
                    BaselinePath: baselinePath,
                    CandidatePath: candidatePath);
                return BuildResult(created, includeImage);
            }

            string diffMapPath = ScreenshotOutput.BuildTimestampedPath(DiffPrefix);
            IReadOnlyList<Rectangle> rectangles = ignoreRegions is null
                ? Array.Empty<Rectangle>()
                : ignoreRegions.Select(region => region.ToRectangle()).ToArray();

            VisualDiffOptions options = VisualDiffOptions.FromEnvironment(
                rectangles,
                diffMapPath,
                ssimThreshold,
                localSsimThreshold,
                pixelDiffThreshold,
                colorThreshold);
            VisualDiffResult result = differ.Compare(baselinePath, candidatePath, options);
            return BuildResult(result, includeImage);
        });
    }

    private static CallToolResult BuildResult(VisualDiffResult diff, bool includeImage)
    {
        string json = JsonSerializer.Serialize(OperationResult.OkWith(diff), JsonOptions.Default);
        CallToolResult result = new()
        {
            Content = { new TextContentBlock { Text = json } },
        };
        if (includeImage && !string.IsNullOrEmpty(diff.DiffMapPath) && File.Exists(diff.DiffMapPath))
        {
            result.Content.Add(ImageContentBlock.FromBytes(File.ReadAllBytes(diff.DiffMapPath), MediaTypePng));
        }
        return result;
    }

    internal static string ResolveBaselineDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable(BaselineDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, DefaultBaselineSubdirectory);
    }

    internal static string ResolveBaselinePath(string baselineKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineKey);

        string normalizedKey = baselineKey
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Trim();
        if (Path.IsPathRooted(normalizedKey))
        {
            throw new ArgumentException("baselineKey must be relative, not rooted.", nameof(baselineKey));
        }

        if (string.IsNullOrEmpty(Path.GetExtension(normalizedKey)))
        {
            normalizedKey += PngExtension;
        }

        if (!string.Equals(Path.GetExtension(normalizedKey), PngExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("baselineKey must resolve to a .png file.", nameof(baselineKey));
        }

        string baselineDirectory = Path.GetFullPath(ResolveBaselineDirectory());
        string baselinePath = Path.GetFullPath(Path.Combine(baselineDirectory, normalizedKey));
        string directoryWithSeparator = baselineDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? baselineDirectory
            : baselineDirectory + Path.DirectorySeparatorChar;

        if (!baselinePath.StartsWith(directoryWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "baselineKey must stay inside WINSIGHT_VISUAL_BASELINE_DIR.",
                nameof(baselineKey));
        }

        return baselinePath;
    }
}
