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
                 "Returns the bounding box center (x, y) plus width/height in pixels of the screenshot, " +
                 "a confidence score, and the model's reasoning. Coordinates are in screenshot space " +
                 "(window-relative); add the window's origin to get screen-space if needed. " +
                 "Default backend is the Anthropic Claude Sonnet 4.6 vision API; configure it with " +
                 "the ANTHROPIC_API_KEY environment variable. Set WINSIGHT_VISION_BACKEND=onnx for the " +
                 "fully local Qwen2.5-VL backend (WINSIGHT_VISION_ONNX_PATH), which runs on-host and " +
                 "never falls back to cloud — use it for regulated or sensitive content.")]
    public static Task<string> FindElementVisuallyAsync(
        IElementGrounder grounder,
        WindowCapture capture,
        [Description("Process id of the target application (from list_windows).")]
        int processId,
        [Description("Natural-language description of the element to locate, " +
                     "e.g. 'the increment button in the lower-right of the counter panel'.")]
        string query,
        [Description("Optional case-insensitive substring of the target window title. " +
                     "Use this to capture modal dialogs, popups, or secondary windows.")]
        string? windowTitle = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSafety.SafeInvokeAsync(async () =>
        {
            var screenshotPath = ScreenshotOutput.BuildTimestampedPath(ScreenshotPrefix);
            capture.Capture(processId, screenshotPath, windowTitle);
            GroundedElement grounded = await grounder.FindAsync(screenshotPath, query, cancellationToken);
            return JsonSerializer.Serialize(grounded, JsonOptions.Default);
        });
    }
}
