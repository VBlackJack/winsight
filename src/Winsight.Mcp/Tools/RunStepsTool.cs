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
using Winsight.Core.Modules.Diagnostics;
using Winsight.Core.Modules.Orchestration;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class RunStepsTool
{
    private const string ScreenshotAction = "screenshot";
    private const string ScreenshotPrefix = "screenshot-";

    [McpServerTool(Name = "run_steps")]
    [Description("Run a sequence of UI steps in a single call, instead of one round trip per action. " +
                 "Each step has an 'action' plus that action's fields; the shared locator fields " +
                 "(automationId, name, className, controlType, nameContains, index, descendantText, " +
                 "windowTitle) work like click_element. Actions: " +
                 "'click' (clickStyle), " +
                 "'click_point' (x, y, clickStyle, coordinateSpace), " +
                 "'type' (text, focusWindow), " +
                 "'key_chord' (chord), " +
                 "'select' (itemName), " +
                 "'hover' (expectedTooltipText, timeoutMs), " +
                 "'wait' (condition, expectedValue, timeoutMs, pollIntervalMs, stableForMs) — the sync " +
                 "and assertion primitive, " +
                 "'get_state' (reads an element's state as an oracle), " +
                 "'screenshot' (outputPath optional; a timestamped default is used when omitted). " +
                 "Set processId per step, or rely on the batch-level processId default. With " +
                 "stopOnError=true (default) the run stops at the first failing step. Returns a journal: " +
                 "{ success, completed, total, failedAtIndex, steps:[{ index, action, label, success, " +
                 "error, data }] }. Use 'label' on a step to tag it in the journal. Set " +
                 "captureExceptions=true to attach, in 'exceptions', the exceptions the target process " +
                 "threw during the batch (a causal oracle; requires the batch-level processId). Does not " +
                 "launch or close apps — use launch_app/close_app around the batch.")]
    public static string RunSteps(
        StepRunner runner,
        DiagnosticsObserver diagnostics,
        SessionTarget target,
        [Description("Ordered steps to execute. Each is an object with 'action' and that action's fields.")]
        IReadOnlyList<BatchStep> steps,
        [Description("Default process id for steps that omit their own processId. Falls back to the session default set by set_target.")]
        int? processId = null,
        [Description("Stop at the first failing step (default true). When false, every step runs and all outcomes are reported.")]
        bool stopOnError = true,
        [Description("Capture the exceptions the target process throws during the batch (caught ones " +
                     "included) and attach them in 'exceptions'. Requires the batch-level processId.")]
        bool captureExceptions = false,
        [Description("Optional total budget in milliseconds for the whole batch. When the elapsed time " +
                     "reaches it before a step, the run stops and the result is flagged timedOut.")]
        int? maxDurationMs = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (steps is null || steps.Count == 0)
            {
                return JsonSerializer.Serialize(
                    OperationResult.Failure("steps is required and must contain at least one step."),
                    JsonOptions.Default);
            }
            // The batch process id falls back to the session default; per-step
            // processId still overrides it inside the runner.
            int? effectiveProcessId = processId ?? target.ProcessId;
            if (captureExceptions && effectiveProcessId is null)
            {
                return JsonSerializer.Serialize(
                    OperationResult.Failure(
                        "captureExceptions requires a process id (pass processId or call set_target); " +
                        "the exception session targets one process."),
                    JsonOptions.Default);
            }

            // Fill a default capture path for screenshot steps that omit one, so
            // the engine layer stays free of the output-directory convention.
            List<BatchStep> prepared = steps
                .Select(step =>
                    string.Equals(step.Action?.Trim(), ScreenshotAction, StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrEmpty(step.OutputPath)
                        ? step with { OutputPath = ScreenshotOutput.BuildTimestampedPath(ScreenshotPrefix) }
                        : step)
                .ToList();

            BatchResult result;
            if (captureExceptions)
            {
                BatchResult? batch = null;
                IReadOnlyList<DiagnosticEvent> exceptions = diagnostics.CaptureExceptionsDuring(
                    effectiveProcessId!.Value,
                    () => batch = runner.Run(prepared, effectiveProcessId, stopOnError, maxDurationMs));
                result = batch! with { Exceptions = exceptions };
            }
            else
            {
                result = runner.Run(prepared, effectiveProcessId, stopOnError, maxDurationMs);
            }

            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }
}
