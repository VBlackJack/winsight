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
using System.Security.Cryptography;
using ModelContextProtocol.Server;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Core.Modules.Vision;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class SceneVisionTool
{
    [McpServerTool(Name = "locate_scene_target", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Vision fallback for a window in an existing scene. Uses the configured grounder (cloud or local ONNX), returning a NEW observation with v1 target and provenance/confidence. Query can describe visible text. This is not a dedicated OCR engine. Requires confidence >=0.8. act_verified rechecks the full screenshot bytes and window identity before clicking; visual change requires locating again. Use a native UIA or managed postcondition for verification.")]
    public static Task<string> Locate(SceneStore store, IElementGrounder grounder, string observation,
        long windowHandle, string query, CancellationToken cancellationToken = default)
        => ToolSafety.SafeInvokeAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length > 2048) return VerifiedSceneTool.Serialize(OperationResult.Failure("Query must be 1..2048 characters."));
            SceneStore.Entry entry = store.Get(observation) ?? throw new InvalidOperationException("Observation expired or consumed.");
            if (!ToolAccessPolicy.TryAuthorizeProcess(entry.ProcessId, out string error)) return VerifiedSceneTool.Serialize(OperationResult.Failure(error));
            WindowDescriptor descriptor = entry.Capture.Windows.Single(w => w.WindowHandle == windowHandle);
            WindowIdentity expected = new(entry.ProcessId, windowHandle, new WindowBounds(descriptor.X, descriptor.Y, descriptor.Width, descriptor.Height));
            ScreenshotResult screenshot = store.Services.Capture.Capture(entry.ProcessId, ScreenshotOutput.BuildTimestampedPath("scene-vision-"), descriptor.Title);
            if (screenshot.Window != expected) return VerifiedSceneTool.Serialize(OperationResult.Failure("Window changed; observe again."));
            string hash = Hash(screenshot.Path);
            GroundedElement grounded = await grounder.FindAsync(screenshot.Path, query, cancellationToken).ConfigureAwait(false);
            if (store.Get(observation) is null || !WindowGeometry.TryValidate(expected, out _)) return VerifiedSceneTool.Serialize(OperationResult.Failure("Observation or window changed during grounding."));
            if (!double.IsFinite(grounded.Confidence) || grounded.Confidence < 0.8 || grounded.Confidence > 1 ||
                grounded.X < 0 || grounded.Y < 0 || grounded.X >= screenshot.Width || grounded.Y >= screenshot.Height)
                return VerifiedSceneTool.Serialize(OperationResult.Failure("Vision confidence or coordinates did not meet the action threshold."));
            SceneElement node = new("v1", "vision", "visual_target", query, null, true, true, false, null,
                new WindowBounds(descriptor.X + grounded.X, descriptor.Y + grounded.Y, 1, 1),
                ["activate", "right_click", "double_click"], expected, new ElementLocator(), hash,
                Confidence: grounded.Confidence, StateHash: hash);
            SceneCapture capture = entry.Capture with { Elements = entry.Capture.Elements.Where(e => e.Source == "uia").Concat(new[] { node }).ToArray(), Source = "uia+vision" };
            return VerifiedSceneTool.Serialize(OperationResult.OkWith(store.Add(entry.ProcessId, entry.ProcessStart, descriptor.Title, capture)));
        });

    internal static OperationResult Dispatch(SceneServices services, SceneStore.Entry entry, SceneElement node, SceneAction request, Action beforeDispatch)
    {
        using DesktopInputLease lease = DesktopInputLease.Acquire();
        if (!WindowGeometry.TryValidate(node.Window, out string? error)) return OperationResult.Failure(error!);
        ScreenshotResult current = services.Capture.Capture(entry.ProcessId, ScreenshotOutput.BuildTimestampedPath("scene-recheck-"), entry.Title);
        if (current.Window != node.Window || Hash(current.Path) != node.StateHash) return OperationResult.Failure("stale: visual content changed; locate again.");
        beforeDispatch();
        ClickStyle style = request.Action switch { "right_click" => ClickStyle.RightClick, "double_click" => ClickStyle.DoubleClick, _ => ClickStyle.Primary };
        return services.Invoker.ClickPoint(entry.ProcessId, node.Bounds.Left, node.Bounds.Top, style,
            windowRelative: false, expectedWindow: node.Window);
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
