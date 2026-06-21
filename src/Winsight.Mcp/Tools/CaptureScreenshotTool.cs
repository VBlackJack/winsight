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
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class CaptureScreenshotTool
{
    private const string ScreenshotPrefix = "screenshot-";

    [McpServerTool(Name = "capture_screenshot")]
    [Description("Capture a PNG screenshot of the target process's topmost visible window, or the first " +
                 "visible window whose title contains windowTitle, using PrintWindow with " +
                 "PW_RENDERFULLCONTENT (so DirectX-composed WPF and accelerated surfaces render correctly). " +
                 "Use windowTitle to target modal dialogs, popups, and secondary windows. " +
                 "If outputPath is omitted, the file is written under WINSIGHT_OUTPUT_DIR " +
                 "(default %TEMP%\\winsight) with a timestamped name. " +
                 "Returns an OperationResult JSON with success/error; on success, data carries " +
                 "{ path, width, height, capturedAtUtc }.")]
    public static string CaptureScreenshot(
        WindowCapture capture,
        [Description("Process id of the target application (from list_windows).")]
        int processId,
        [Description("Optional absolute output path. Defaults to a timestamped file under " +
                     "WINSIGHT_OUTPUT_DIR (default %TEMP%\\winsight).")]
        string? outputPath = null,
        [Description("Optional case-insensitive substring of the target window title. " +
                     "Use this to capture modal dialogs, popups, or secondary windows.")]
        string? windowTitle = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            var resolvedPath = string.IsNullOrWhiteSpace(outputPath)
                ? ScreenshotOutput.BuildTimestampedPath(ScreenshotPrefix)
                : outputPath;
            ScreenshotResult screenshot = capture.Capture(processId, resolvedPath, windowTitle);
            return JsonSerializer.Serialize(
                OperationResult.OkWith(screenshot),
                JsonOptions.Default);
        });
    }
}
