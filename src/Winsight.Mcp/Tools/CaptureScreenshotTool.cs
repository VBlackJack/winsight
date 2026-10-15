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
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class CaptureScreenshotTool
{
    private const string ScreenshotPrefix = "screenshot-";
    private const string MediaTypePng = "image/png";

    [McpServerTool(Name = "capture_screenshot")]
    [Description("Capture a PNG screenshot of the target process's topmost visible window that is not a " +
                 "transient overlay (an open tooltip or menu is skipped), its main window when mainWindow=true, " +
                 "or the first " +
                 "visible window whose title contains windowTitle, using PrintWindow with " +
                 "PW_RENDERFULLCONTENT (so DirectX-composed WPF and accelerated surfaces render correctly). " +
                 "Use windowTitle to target modal dialogs, popups, and secondary windows. " +
                 "If outputPath is omitted, the file is written under WINSIGHT_OUTPUT_DIR " +
                 "(default %TEMP%\\winsight) with a timestamped name. " +
                 "Returns a text block with the OperationResult JSON (success/error; on success, data " +
                 "carries { path, width, height, capturedAtUtc }) and, unless includeImage=false, the " +
                 "captured PNG as an inline image so you can see the UI directly without reading the file.")]
    public static CallToolResult CaptureScreenshot(
        WindowCapture capture,
        SessionTarget target,
        [Description("Optional absolute output path. Defaults to a timestamped file under " +
                     "WINSIGHT_OUTPUT_DIR (default %TEMP%\\winsight).")]
        string? outputPath = null,
        [Description("Optional case-insensitive substring of the target window title. " +
                     "Use this to capture modal dialogs, popups, or secondary windows.")]
        string? windowTitle = null,
        [Description("Attach the captured PNG as an inline image (default true). Set false to return " +
                     "only the JSON path when the bytes are not needed.")]
        bool includeImage = true,
        [Description("Capture the process main window even when a dialog or overlay is on top of it. " +
                     "Ignored when windowTitle is set.")]
        bool mainWindow = false,
        [Description("Target process id (from list_windows). Optional — falls back to the session default set by set_target.")]
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
            var resolvedPath = string.IsNullOrWhiteSpace(outputPath)
                ? ScreenshotOutput.BuildTimestampedPath(ScreenshotPrefix)
                : outputPath;
            ScreenshotResult screenshot = capture.Capture(pid, resolvedPath, windowTitle, mainWindow);
            string json = JsonSerializer.Serialize(OperationResult.OkWith(screenshot), JsonOptions.Default);

            CallToolResult result = new()
            {
                Content = { new TextContentBlock { Text = json } },
            };
            if (includeImage)
            {
                byte[] bytes = File.ReadAllBytes(screenshot.Path);
                result.Content.Add(ImageContentBlock.FromBytes(bytes, MediaTypePng));
            }
            return result;
        });
    }
}
