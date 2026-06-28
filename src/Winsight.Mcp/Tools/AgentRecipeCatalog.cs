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

namespace Winsight.Mcp.Tools;

internal sealed record AgentRecipe(
    string Id,
    string Goal,
    string WhenToUse,
    IReadOnlyList<string> Steps,
    string SuccessSignal,
    string Fallback);

internal static class AgentRecipeCatalog
{
    public static readonly IReadOnlyList<AgentRecipe> All =
    [
        new(
            "form_fill",
            "Fill a field and prove the value landed.",
            "Use for text boxes, search fields, and settings forms.",
            [
                "observe",
                "plan_steps with text",
                "run_steps dryRun=true",
                "type_and_assert or run_steps dryRun=false",
            ],
            "type_and_assert succeeds, or assert/get_state confirms the expected value.",
            "repair_locator with the failed field label, then retry."),

        new(
            "dialog_flow",
            "Handle modal dialogs and transient windows.",
            "Use when an action opens a dialog, popup, menu, or message box.",
            [
                "act_and_observe dryRun=false",
                "read change.windowsOpened",
                "observe with windowTitle",
                "click_and_wait on the dialog action",
            ],
            "diff reports the expected window opened/closed and follow-up assertion passes.",
            "list_windows for the process, then scope by windowTitle."),

        new(
            "visual_click",
            "Act on UI that UI Automation cannot see.",
            "Use for canvas, DirectX, Skia, WebView2 surfaces, or custom-drawn controls.",
            [
                "capture_screenshot or observe includeScreenshot=true",
                "find_element_visually",
                "click_point coordinateSpace=window dryRun=true through run_steps",
                "act_and_observe or click_point dryRun=false",
            ],
            "act_and_observe reports a structural or visual change, or assertion succeeds.",
            "Retry with windowTitle and a more specific visual query."),

        new(
            "assert_counter",
            "Prove a scenario changed app state.",
            "Use when UI text, managed fields, counters, or events can serve as an oracle.",
            [
                "run_steps with actions",
                "assert_element or get_element_state",
                "read_managed_state if UI is not enough",
                "observe_counters or capture_events for runtime behavior",
            ],
            "A first-class assert, managed-state read, counter, or event oracle passes.",
            "Use capture_events around the batch to find causal exceptions."),

        new(
            "webview_cdp",
            "Inspect or drive embedded WebView2 content.",
            "Use when WebView2 is configured with a remote debugging port.",
            [
                "detect_webview2_endpoints",
                "list_cdp_targets with the same host process",
                "evaluate_javascript for DOM state",
                "type_into_webview for key-driven input",
            ],
            "CDP evaluation returns the expected DOM state or typing dispatch count.",
            "If no endpoint is authorized, fall back to UIA or visual_click."),

        new(
            "locator_repair",
            "Recover from a missing or ambiguous locator.",
            "Use whenever a locator fails, is ambiguous, or a UI changed.",
            [
                "repair_locator with the failed label/id",
                "use currentUi[0].locatorArgs when confidence is high",
                "run_steps dryRun=true",
                "retry the original action",
            ],
            "The dry-run validates, then the retried action succeeds.",
            "observe with includeScreenshot=true and inspect the affordance list."),
    ];
}
