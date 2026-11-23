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
using ModelContextProtocol.Server;

namespace Winsight.Mcp.Prompts;

/// <summary>
/// Reusable MCP prompt playbooks for autonomous desktop exploration,
/// verified workflows, and UI diagnostics.
/// </summary>
[McpServerPromptType]
public static class WinsightPrompts
{
    [McpServerPrompt(Name = "explore_app", Title = "Explore Windows Application")]
    [Description("Autonomous playbook to attach/launch a Windows app, inspect its UI with Set-of-Mark vision, and explore safely.")]
    public static string ExploreApp(
        [Description("Executable path, window title, or process name to explore.")]
        string target)
    {
        return $"""
            Explore the Windows application "{target}" autonomously using Winsight:
            1. If "{target}" is an executable path, call launch_app (setAsTarget=true by default). If "{target}" is a window title, call list_windows with includeTitles=true and set_target on the matching processId. Otherwise call list_windows, pick the window by processName, className, or bounds (titles of other apps are withheld by default), and set_target on its processId.
            2. Call observe_scene to inspect windows, modals, and actionable eN elements.
            3. Call capture_scene(observation) to view the inline Set-of-Mark annotated screenshot before interacting with unfamiliar layout.
            4. Drive controls with act_verified (use observeAfter=true and exploratory predicates like any_change or name_contains when exploring).
            5. On user_active or foreground_changed, call wait_for_user_idle and observe_scene again. Never blindly retry dispatched_unconfirmed.
            """;
    }

    [McpServerPrompt(Name = "verify_flow", Title = "Verify & Record Application Flow")]
    [Description("Strict verification workflow that proves each step with explicit postconditions and optionally saves a version-pinned app profile.")]
    public static string VerifyFlow(
        [Description("End-to-end user goal or acceptance criterion to verify.")]
        string goal)
    {
        return $"""
            Verify the following flow using Winsight strict verified control: "{goal}"
            1. Check list_app_profiles first to see if a verified profile already exists for this app.
            2. Call observe_scene on the target process.
            3. Execute each step with act_verified using a discriminating strict postcondition (value, name, selected, toggle, window_open, window_closed, or managed) and observeAfter=true.
            4. Stop immediately on any unverified or dispatched_unconfirmed status and re-observe before deciding.
            5. Once every step is verified, offer to save the workflow with save_app_profile (using textParameter placeholders for runtime inputs).
            """;
    }

    [McpServerPrompt(Name = "diagnose_ui_bug", Title = "Diagnose Desktop UI Bug")]
    [Description("Diagnostic playbook combining causal CLR exception capture, screenshots, and managed heap state inspection.")]
    public static string DiagnoseUiBug(
        [Description("Observed bug symptom or failing user action.")]
        string symptom)
    {
        return $"""
            Diagnose the desktop UI issue: "{symptom}"
            1. Call observe_scene and capture_scene to record the initial visual and UIA state.
            2. Reproduce the failing sequence with run_steps(captureExceptions=true, traceRun=true) so all thrown CLR exceptions and the replay trace are captured causally.
            3. Inspect internal ViewModel/service state with read_managed_state and performance metrics with observe_counters if relevant.
            4. Report: reproduction steps, captured exceptions, UI diff/screenshot evidence, and root cause hypothesis.
            """;
    }
}
