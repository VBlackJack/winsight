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
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class PerceiveChangesTool
{
    private const string VerdictBaselineCaptured = "baseline_captured";

    [McpServerTool(Name = "snapshot_ui")]
    [Description("Capture a structural snapshot of the target process (its windows plus its actionable " +
                 "elements) and store it as the baseline for diff_ui. Call this right before an action " +
                 "you want to observe the effect of; then act; then call diff_ui to see what changed. " +
                 "Returns the snapshot size (window and element counts).")]
    public static string SnapshotUi(
        UiSnapshotter snapshotter,
        UiSnapshotStore store,
        SessionTarget target,
        [Description("Scope element capture to the first window whose title contains this text.")]
        string? windowTitle = null,
        [Description("Target process id. Optional; falls back to the session default set by set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }

            UiSnapshot snapshot = snapshotter.Capture(pid, windowTitle);
            store.Set(pid, snapshot);
            return JsonSerializer.Serialize(
                OperationResult.OkWith(new
                {
                    processId = pid,
                    windowCount = snapshot.Windows.Count,
                    interactableCount = snapshot.Interactables.Count,
                }),
                JsonOptions.Default);
        });
    }

    [McpServerTool(Name = "diff_ui")]
    [Description("Report what changed in the target process's UI since the last snapshot_ui or diff_ui: " +
                 "windowsOpened/windowsClosed, elementsAdded/elementsRemoved, and elementsChanged (per-field " +
                 "before/after for value, toggle, selection, enabled, visible). This is the feedback sense: " +
                 "use it after an action to confirm its effect without re-inspecting everything. The current " +
                 "state becomes the new baseline, so successive calls chain. With no prior snapshot, this " +
                 "captures a baseline and returns verdict=baseline_captured.")]
    public static string DiffUi(
        UiSnapshotter snapshotter,
        UiSnapshotStore store,
        SessionTarget target,
        [Description("Scope element capture to the first window whose title contains this text.")]
        string? windowTitle = null,
        [Description("Target process id. Optional; falls back to the session default set by set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }

            UiSnapshot current = snapshotter.Capture(pid, windowTitle);
            if (!store.TryGet(pid, out UiSnapshot baseline))
            {
                store.Set(pid, current);
                return JsonSerializer.Serialize(
                    OperationResult.OkWith(new
                    {
                        verdict = VerdictBaselineCaptured,
                        windowCount = current.Windows.Count,
                        interactableCount = current.Interactables.Count,
                    }),
                    JsonOptions.Default);
            }

            UiChange change = UiDiffer.Diff(baseline, current);
            store.Set(pid, current);
            return JsonSerializer.Serialize(OperationResult.OkWith(change), JsonOptions.Default);
        });
    }
}
