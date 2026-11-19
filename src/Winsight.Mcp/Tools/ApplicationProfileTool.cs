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
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Winsight.Core.Models;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class ApplicationProfileTool
{
    [McpServerTool(Name = "save_app_profile", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Explicitly save a reusable recipe for the target executable version and SHA256. 1..20 semantic steps; textParameter names runtime inputs, never store passwords or private text in recipes. Saving does not certify the recipe. dryRun previews without writing. Replaces a profile of the same name.")]
    public static string Save(ApplicationProfiles profiles, SessionTarget target, string name, ProfileStep[] steps,
        int? processId = null, bool dryRun = false) => ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string error)) return VerifiedSceneTool.Serialize(OperationResult.Failure(error));
            if (steps is null || steps.Length is < 1 or > 20) return VerifiedSceneTool.Serialize(OperationResult.Failure("Expected 1..20 steps."));
            foreach (ProfileStep step in steps)
            {
                string? invalid = ValidateStep(step);
                if (invalid is not null) return VerifiedSceneTool.Serialize(OperationResult.Failure(invalid));
            }
            (string path, string version, string hash) = ApplicationProfiles.Fingerprint(pid);
            ApplicationProfile profile = new(name, path, version, hash, steps);
            if (!dryRun && !ToolAccessPolicy.IsDryRunEnabled) profiles.Save(profile);
            return VerifiedSceneTool.Serialize(OperationResult.OkWith(new { dryRun = dryRun || ToolAccessPolicy.IsDryRunEnabled, profile, validated = false }));
        });

    [McpServerTool(Name = "run_app_profile", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Run a saved recipe with fresh observations and act_verified at every step. Refuses a changed executable/hash/version, missing parameters or ambiguous target. Stops at the first unverified result and never retries. Default dryRun=true returns a plan; use false only for an authorized workflow. Overall verified requires every postcondition; verification applies to this run only.")]
    public static Task<string> Run(ApplicationProfiles profiles, SceneStore store, SessionTarget target,
        string name, Dictionary<string, string>? parameters = null, int? processId = null, bool dryRun = true,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default) => ToolSafety.SafeInvokeAsync(async () =>
        {
            if (!target.TryResolve(processId, out int pid, out string error)) return VerifiedSceneTool.Serialize(OperationResult.Failure(error));
            ApplicationProfile profile = profiles.Load(name);
            using Process process = Process.GetProcessById(pid);
            long processStart = process.StartTime.ToUniversalTime().Ticks;
            (string path, string version, string hash) = ApplicationProfiles.Fingerprint(pid);
            if (!string.Equals(path, profile.Executable, StringComparison.OrdinalIgnoreCase) || hash != profile.Sha256 || version != profile.Version)
                return VerifiedSceneTool.Serialize(OperationResult.Failure("Profile invalidated: executable path, bytes or version changed. Review and explicitly save a new profile."));
            foreach (ProfileStep step in profile.Steps)
            {
                string? invalid = ValidateStep(step);
                if (invalid is not null) return VerifiedSceneTool.Serialize(OperationResult.Failure(invalid));
                if (step.TextParameter is not null && (parameters is null || !parameters.TryGetValue(step.TextParameter, out string? text) || text.Length > 16384))
                    return VerifiedSceneTool.Serialize(OperationResult.Failure("Missing or oversized runtime parameter: " + step.TextParameter));
            }
            if (dryRun || ToolAccessPolicy.IsDryRunEnabled) return VerifiedSceneTool.Serialize(OperationResult.OkWith(new { dryRun = true, profile, verified = false }));
            List<JsonElement> results = new();
            for (int stepIndex = 0; stepIndex < profile.Steps.Count; stepIndex++)
            {
                ProfileStep step = profile.Steps[stepIndex];
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ProgressNotificationValue
                {
                    Progress = stepIndex,
                    Total = profile.Steps.Count,
                    Message = $"Profile '{name}' step {stepIndex + 1}/{profile.Steps.Count}: {step.Action}",
                });
                if (process.HasExited) return VerifiedSceneTool.Serialize(new { status = "blocked", verified = false, message = "Profile process exited.", results });
                SceneStore.Entry scene = VerifiedSceneTool.Capture(store, pid, step.WindowTitle);
                if (scene.ProcessStart != processStart) return VerifiedSceneTool.Serialize(new { status = "blocked", verified = false, message = "Profile process was replaced.", results });
                SceneElement[] matches = scene.Capture.Elements.Where(e => VerifiedSceneTool.Matches(e, step.Target)).ToArray();
                if (scene.Capture.Truncated || matches.Length != 1) return VerifiedSceneTool.Serialize(new { status = "blocked", verified = false, message = "Target is ambiguous, missing or scene truncated.", completed = results.Count, results });
                string? text = step.TextParameter is null ? null : parameters![step.TextParameter];
                SceneAction request = new(scene.Id, matches[0].Ref, step.Action, step.Expect, Text: text, ItemName: step.ItemName);
                string output = await VerifiedSceneTool.Execute(store, request, cancellationToken).ConfigureAwait(false);
                using JsonDocument document = JsonDocument.Parse(output);
                results.Add(document.RootElement.Clone());
                if (!document.RootElement.GetProperty("verified").GetBoolean()) return VerifiedSceneTool.Serialize(new { status = "stopped", verified = false, completed = results.Count - 1, results });
            }
            progress?.Report(new ProgressNotificationValue
            {
                Progress = profile.Steps.Count,
                Total = profile.Steps.Count,
                Message = $"Profile '{name}' completed ({profile.Steps.Count}/{profile.Steps.Count}).",
            });
            return VerifiedSceneTool.Serialize(new { status = "verified", verified = true, completed = results.Count, results });
        });

    [McpServerTool(Name = "list_app_profiles", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List saved version-pinned application profiles (name, executable, version, SHA256, step count) so an agent can discover and reuse verified workflows across sessions.")]
    public static string List(ApplicationProfiles profiles) => ToolSafety.SafeInvoke(() =>
        VerifiedSceneTool.Serialize(OperationResult.OkWith(profiles.List())));

    [McpServerTool(Name = "delete_app_profile", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Delete a saved application profile by name. Returns deleted=true when a file was removed or deleted=false if it did not exist.")]
    public static string Delete(ApplicationProfiles profiles, string name) => ToolSafety.SafeInvoke(() =>
    {
        if (ToolAccessPolicy.GuardMutation("delete_app_profile", new { name }) is { } gated)
        {
            return VerifiedSceneTool.Serialize(gated);
        }

        bool deleted = profiles.Delete(name);
        return VerifiedSceneTool.Serialize(OperationResult.OkWith(new { name, deleted }));
    });

    private static string? ValidateStep(ProfileStep step)
    {
        if (step is null || step.Target is null || step.Expect is null) return "Step target and expectation are required.";
        if (step.Expect.Kind is "any_change" or "visual_change") return "Saved profiles require a strict deterministic expectation (any_change and visual_change are exploratory only).";
        if (step.Target.Index is not null || step.Target.ClassName is not null || step.Target.NameContains is not null || step.Target.DescendantText is not null ||
            (string.IsNullOrEmpty(step.Target.AutomationId) && string.IsNullOrEmpty(step.Target.Name))) return "Profile target requires an exact AutomationId or Name; only ControlType may supplement it.";
        if (step.Action is not ("activate" or "set_value" or "select" or "select_item" or "expand" or "collapse" or "scroll_into_view" or "focus" or "right_click" or "double_click")) return "Unsupported profile action.";
        if (step.Action == "set_value" && string.IsNullOrWhiteSpace(step.TextParameter)) return "set_value requires a runtime textParameter.";
        return VerifiedSceneTool.Validate(new SceneAction("preview", "preview", step.Action, step.Expect, Text: "preview", ItemName: step.ItemName));
    }
}
