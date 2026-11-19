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
public static class AuditUiAccessibilityTool
{
    [McpServerTool(Name = "audit_ui_accessibility", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Audit the target window's UI Automation tree for accessibility and QA issues: missing accessible Name, missing AutomationId, " +
                 "zero-size visible controls, keyboard-unreachable interactive controls, and overlapping interactive bounding boxes. " +
                 "Returns a score (0..100) and structured findings. Read-only.")]
    public static string AuditUiAccessibility(
        SceneStore store,
        SessionTarget target,
        [Description("Optional window title substring to scope the audit.")]
        string? windowTitle = null,
        [Description("Maximum UIA elements to inspect (1..1000, default 400).")]
        int maxResults = 400,
        [Description("Target process id. Optional; falls back to set_target.")]
        int? processId = null)
        => ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(
                    AgentFailure.Repairable("missing_target", targetError, "target_resolution", "set_target"),
                    JsonOptions.Default);
            }
            if (maxResults is < 1 or > 1000)
            {
                return JsonSerializer.Serialize(OperationResult.Failure("maxResults must be 1..1000."), JsonOptions.Default);
            }

            SceneCapture capture = store.Services.Driver.Capture(pid, windowTitle, maxResults);
            UiAccessibilityAuditReport report = UiAccessibilityAuditor.Audit(capture);
            return JsonSerializer.Serialize(OperationResult.OkWith(new
            {
                processId = pid,
                windowTitle,
                capture.Truncated,
                report,
            }), JsonOptions.Default);
        });
}
