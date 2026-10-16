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
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Server;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Core.Modules.Vision;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class FindElementVisuallyTool
{
    public const string MinimumConfidenceVariable = "WINSIGHT_VISION_MIN_CONFIDENCE";

    private const string ScreenshotPrefix = "vision-";
    private const double DefaultMinimumConfidence = 0.5;

    [McpServerTool(Name = "find_element_visually")]
    [Description("Locate a UI element from a natural-language description by capturing a screenshot " +
                 "of the target window and grounding it via a vision model. Use windowTitle to target " +
                 "modal dialogs, popups, and secondary windows. Use this when " +
                 "inspect_ui_tree returns no useful AutomationId (custom WPF Canvas, SkiaSharp, " +
                 "DirectX surfaces, or any control that does not expose itself to UI Automation). " +
                 "Returns an OperationResult JSON; on success, data is the grounded element: the bounding " +
                 "box center (x, y) plus width/height in screenshot pixels, a confidence score, the " +
                 "model's reasoning, and screenX/screenY (absolute desktop coordinates) so you can act on " +
                 "the point directly. Pass act=single|double|right to click the grounded point in the " +
                 "same call (via click_point); data is then { grounded, click }. By default the tool only " +
                 "locates. " +
                 "Default backend is the Anthropic Claude Sonnet 4.6 vision API; configure it with " +
                 "the ANTHROPIC_API_KEY environment variable. Set WINSIGHT_VISION_BACKEND=onnx for the " +
                 "fully local Qwen2.5-VL backend (WINSIGHT_VISION_ONNX_PATH), which runs on-host and " +
                 "never falls back to cloud, so use it for regulated or sensitive content.")]
    public static Task<string> FindElementVisuallyAsync(
        IElementGrounder grounder,
        WindowCapture capture,
        ActionInvoker invoker,
        SessionTarget target,
        [Description("Natural-language description of the element to locate, " +
                     "e.g. 'the increment button in the lower-right of the counter panel'.")]
        string query,
        [Description("Optional case-insensitive substring of the target window title. " +
                     "Use this to capture modal dialogs, popups, or secondary windows.")]
        string? windowTitle = null,
        [Description("Optional action to perform on the grounded point: single, double, or right. " +
                     "When set, the point is clicked via click_point and the click result is returned " +
                     "alongside the grounding. Omit to only locate.")]
        string? act = null,
        [Description("Target process id (from list_windows). Optional; falls back to the session default set by set_target.")]
        int? processId = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSafety.SafeInvokeAsync(async () =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }

            ClickStyle? requestedAction = null;
            double minimumConfidence = DefaultMinimumConfidence;
            if (!string.IsNullOrWhiteSpace(act))
            {
                if (!ClickStyleParser.TryParse(act, out ClickStyle style))
                {
                    return JsonSerializer.Serialize(
                        OperationResult.Failure(
                            $"Unknown act \"{act}\". Accepted values: {ClickStyleParser.AcceptedValues}."),
                        JsonOptions.Default);
                }
                if (!TryResolveMinimumConfidence(out minimumConfidence, out string confidenceError))
                {
                    return JsonSerializer.Serialize(OperationResult.Failure(confidenceError), JsonOptions.Default);
                }
                requestedAction = style;
            }

            var screenshotPath = ScreenshotOutput.BuildTimestampedPath(ScreenshotPrefix);
            ScreenshotResult screenshot = capture.Capture(pid, screenshotPath, windowTitle);
            GroundedElement grounded = await grounder.FindAsync(screenshotPath, query, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            WindowIdentity? capturedWindow = screenshot.Window;
            if (capturedWindow is null || !WindowGeometry.TryValidate(capturedWindow, out _))
            {
                return JsonSerializer.Serialize(
                    OperationResult.Failure("The captured window is unavailable or changed. Capture again before acting."),
                    JsonOptions.Default);
            }
            WindowBounds bounds = capturedWindow.Bounds;
            (int screenX, int screenY) =
                WindowPointMapper.ToScreen(bounds.Left, bounds.Top, grounded.X, grounded.Y);
            grounded = grounded with { ScreenX = screenX, ScreenY = screenY };

            if (requestedAction is null)
            {
                return JsonSerializer.Serialize(OperationResult.OkWith(grounded), JsonOptions.Default);
            }
            if (ValidateMinimumConfidence(grounded, minimumConfidence) is { } confidenceFailure)
            {
                return JsonSerializer.Serialize(confidenceFailure, JsonOptions.Default);
            }
            if (ToolAccessPolicy.GuardMutation(
                    "find_element_visually.act",
                    new { processId = pid, query, act, windowTitle, grounded })
                is { } gated)
            {
                return JsonSerializer.Serialize(
                    gated.Success
                        ? OperationResult.OkWith(new { grounded, click = gated.Data })
                        : OperationResult.FailureWith(gated.Error!, new { grounded }),
                    JsonOptions.Default);
            }

            OperationResult click = invoker.ClickPoint(
                pid, grounded.X, grounded.Y, requestedAction.Value, windowTitle, windowRelative: true,
                expectedWindow: capturedWindow);
            return JsonSerializer.Serialize(
                click.Success
                    ? OperationResult.OkWith(new { grounded, click })
                    : OperationResult.FailureWith(click.Error!, new { grounded, click }),
                JsonOptions.Default);
        });
    }

    internal static OperationResult? ValidateMinimumConfidence(
        GroundedElement grounded,
        double minimumConfidence)
    {
        if (!double.IsFinite(grounded.Confidence) ||
            grounded.Confidence < 0.0 ||
            grounded.Confidence > 1.0)
        {
            return OperationResult.FailureWith(
                $"Vision grounding confidence {grounded.Confidence.ToString(CultureInfo.InvariantCulture)} is outside the valid range 0..1; refusing to click.",
                new { grounded, minConfidence = minimumConfidence });
        }

        if (grounded.Confidence < minimumConfidence)
        {
            return OperationResult.FailureWith(
                "Vision grounding confidence " +
                $"{grounded.Confidence.ToString("0.###", CultureInfo.InvariantCulture)} is below " +
                $"{MinimumConfidenceVariable}=" +
                $"{minimumConfidence.ToString("0.###", CultureInfo.InvariantCulture)}; refusing to click.",
                new { grounded, minConfidence = minimumConfidence });
        }

        return null;
    }

    internal static bool TryResolveMinimumConfidence(
        out double minimumConfidence,
        out string error,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        string? raw = (getEnvironmentVariable ?? Environment.GetEnvironmentVariable)(MinimumConfidenceVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            minimumConfidence = DefaultMinimumConfidence;
            error = string.Empty;
            return true;
        }

        if (!double.TryParse(
                raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed) ||
            !double.IsFinite(parsed) ||
            parsed < 0.0 ||
            parsed > 1.0)
        {
            minimumConfidence = DefaultMinimumConfidence;
            error = $"{MinimumConfidenceVariable} must be a number between 0 and 1.";
            return false;
        }

        minimumConfidence = parsed;
        error = string.Empty;
        return true;
    }
}
