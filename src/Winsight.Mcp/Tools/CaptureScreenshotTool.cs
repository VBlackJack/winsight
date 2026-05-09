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
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class CaptureScreenshotTool
{
    private const string ScreenshotSubdirectory = "winsight";
    private const string ScreenshotPrefix = "screenshot-";
    private const string ScreenshotExtension = ".png";
    private const string TimestampFormat = "yyyyMMdd-HHmmss-fff";

    [McpServerTool(Name = "capture_screenshot")]
    [Description("Capture a PNG screenshot of the target process's main window using PrintWindow with " +
                 "PW_RENDERFULLCONTENT (so DirectX-composed WPF and accelerated surfaces render correctly). " +
                 "If outputPath is omitted, the file is written under %TEMP%\\winsight with a timestamped name. " +
                 "Returns an OperationResult JSON with success/error; on success, data carries " +
                 "{ path, width, height, capturedAtUtc }.")]
    public static string CaptureScreenshot(
        WindowCapture capture,
        [Description("Process id of the target application (from list_windows).")]
        int processId,
        [Description("Optional absolute output path. Defaults to %TEMP%\\winsight\\screenshot-{timestamp}.png.")]
        string? outputPath = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            var resolvedPath = string.IsNullOrWhiteSpace(outputPath)
                ? BuildDefaultPath()
                : outputPath;
            var screenshot = capture.Capture(processId, resolvedPath);
            return JsonSerializer.Serialize(
                OperationResult.OkWith(screenshot),
                JsonOptions.Default);
        });
    }

    private static string BuildDefaultPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), ScreenshotSubdirectory);
        var timestamp = DateTime.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        return Path.Combine(directory, ScreenshotPrefix + timestamp + ScreenshotExtension);
    }
}
