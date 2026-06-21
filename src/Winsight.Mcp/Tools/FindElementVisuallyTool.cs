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
    private const string ScreenshotPrefix = "vision-";

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
                 "never falls back to cloud — use it for regulated or sensitive content.")]
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
        [Description("Target process id (from list_windows). Optional — falls back to the session default set by set_target.")]
        int? processId = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSafety.SafeInvokeAsync(async () =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }

            var screenshotPath = ScreenshotOutput.BuildTimestampedPath(ScreenshotPrefix);
            capture.Capture(pid, screenshotPath, windowTitle);
            GroundedElement grounded = await grounder.FindAsync(screenshotPath, query, cancellationToken);

            // Enrich with absolute screen coordinates so the agent never has to
            // add the window origin itself. Best-effort: if the window origin
            // cannot be resolved, the window-relative point is still returned.
            if (WindowGeometry.TryResolveTopmostBounds(pid, windowTitle, out WindowBounds bounds, out _))
            {
                (int screenX, int screenY) =
                    WindowPointMapper.ToScreen(bounds.Left, bounds.Top, grounded.X, grounded.Y);
                grounded = grounded with { ScreenX = screenX, ScreenY = screenY };
            }

            if (string.IsNullOrWhiteSpace(act))
            {
                return JsonSerializer.Serialize(OperationResult.OkWith(grounded), JsonOptions.Default);
            }

            if (!ClickStyleParser.TryParse(act, out ClickStyle style))
            {
                return JsonSerializer.Serialize(
                    OperationResult.Failure(
                        $"Unknown act \"{act}\". Accepted values: {ClickStyleParser.AcceptedValues}."),
                    JsonOptions.Default);
            }

            OperationResult click = invoker.ClickPoint(
                pid, grounded.X, grounded.Y, style, windowTitle, windowRelative: true);
            return JsonSerializer.Serialize(
                OperationResult.OkWith(new { grounded, click }),
                JsonOptions.Default);
        });
    }
}
