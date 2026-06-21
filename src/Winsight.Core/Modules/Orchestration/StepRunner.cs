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

using System.Diagnostics;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.UiAutomation;

namespace Winsight.Core.Modules.Orchestration;

/// <summary>
/// Runs a sequence of <see cref="BatchStep"/>s in one call, dispatching each to the
/// same primitives the individual MCP tools use, so a whole scenario — drive,
/// synchronize, assert, capture — costs a single round trip instead of one per
/// action. Each step is isolated: an exception becomes that step's failure
/// rather than tearing down the batch.
/// </summary>
public sealed class StepRunner
{
    private const string ScreenshotMissingPath =
        "screenshot step requires outputPath (the run_steps tool fills a default when omitted).";

    private readonly ActionInvoker _invoker;
    private readonly ElementWaiter _waiter;
    private readonly ElementStateReader _stateReader;
    private readonly WindowCapture _capture;

    public StepRunner(
        ActionInvoker invoker,
        ElementWaiter waiter,
        ElementStateReader stateReader,
        WindowCapture capture)
    {
        _invoker = invoker;
        _waiter = waiter;
        _stateReader = stateReader;
        _capture = capture;
    }

    /// <summary>
    /// Execute the steps in order. <paramref name="defaultProcessId"/> applies
    /// to steps that omit their own <see cref="BatchStep.ProcessId"/>. When
    /// <paramref name="stopOnError"/> is true, the run stops at the first failing
    /// step; otherwise it runs them all and reports every outcome. When
    /// <paramref name="maxDurationMs"/> is set, the run stops before a step once
    /// the elapsed time reaches the budget, marking the result timed out.
    /// </summary>
    public BatchResult Run(
        IReadOnlyList<BatchStep> steps,
        int? defaultProcessId,
        bool stopOnError,
        int? maxDurationMs = null)
    {
        ArgumentNullException.ThrowIfNull(steps);

        List<StepResult> results = new(steps.Count);
        int? failedAtIndex = null;
        bool timedOut = false;
        Stopwatch stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < steps.Count; i++)
        {
            if (maxDurationMs is int budget && stopwatch.ElapsedMilliseconds >= budget)
            {
                timedOut = true;
                break;
            }

            BatchStep step = steps[i];
            OperationResult outcome = ExecuteSafely(step, defaultProcessId);
            results.Add(new StepResult(
                i, step.Action ?? string.Empty, step.Label, outcome.Success, outcome.Error, outcome.Data));

            if (!outcome.Success)
            {
                failedAtIndex ??= i;
                if (stopOnError)
                {
                    break;
                }
            }
        }

        int completed = results.Count(r => r.Success);
        bool success = failedAtIndex is null && !timedOut;
        return new BatchResult(
            success, completed, steps.Count, failedAtIndex, results, Exceptions: null, TimedOut: timedOut);
    }

    private OperationResult ExecuteSafely(BatchStep step, int? defaultProcessId)
    {
        try
        {
            return Execute(step, defaultProcessId);
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            return OperationResult.Failure($"{step.Action} step threw: {ex.Message}");
        }
    }

    private OperationResult Execute(BatchStep step, int? defaultProcessId)
    {
        string action = (step.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (action.Length == 0)
        {
            return OperationResult.Failure("BatchStep is missing 'action'.");
        }

        int? processId = step.ProcessId ?? defaultProcessId;
        if (processId is not int pid)
        {
            return OperationResult.Failure(
                $"'{action}' step has no processId and no batch-level processId default was provided.");
        }

        return action switch
        {
            "click" => Click(step, pid),
            "click_point" => ClickPoint(step, pid),
            "type" => Type(step, pid),
            "key_chord" => KeyChord(step, pid),
            "select" => Select(step, pid),
            "hover" => Hover(step, pid),
            "wait" => Wait(step, pid),
            "get_state" => GetState(step, pid),
            "screenshot" => Screenshot(step, pid),
            _ => OperationResult.Failure(
                $"Unknown action \"{step.Action}\". Accepted: click, click_point, type, key_chord, " +
                "select, hover, wait, get_state, screenshot."),
        };
    }

    private OperationResult Click(BatchStep step, int processId)
    {
        if (!TryClickStyle(step.ClickStyle, out ClickStyle style, out OperationResult? error))
        {
            return error;
        }
        return _invoker.Click(processId, Locator(step), style, step.WindowTitle);
    }

    private OperationResult ClickPoint(BatchStep step, int processId)
    {
        if (step.X is not int x || step.Y is not int y)
        {
            return OperationResult.Failure("click_point step requires x and y.");
        }
        if (!TryClickStyle(step.ClickStyle, out ClickStyle style, out OperationResult? error))
        {
            return error;
        }
        if (!TryCoordinateSpace(step.CoordinateSpace, out bool windowRelative, out OperationResult? spaceError))
        {
            return spaceError;
        }
        return _invoker.ClickPoint(processId, x, y, style, step.WindowTitle, windowRelative);
    }

    private OperationResult Type(BatchStep step, int processId)
    {
        if (step.Text is null)
        {
            return OperationResult.Failure("type step requires text.");
        }
        return _invoker.Type(processId, Locator(step), step.Text, step.WindowTitle, step.FocusWindow ?? false);
    }

    private OperationResult KeyChord(BatchStep step, int processId)
    {
        if (string.IsNullOrEmpty(step.Chord))
        {
            return OperationResult.Failure("key_chord step requires chord.");
        }
        return _invoker.SendKeyChord(processId, step.Chord, step.WindowTitle);
    }

    private OperationResult Select(BatchStep step, int processId)
    {
        if (string.IsNullOrEmpty(step.ItemName))
        {
            return OperationResult.Failure("select step requires itemName.");
        }
        return _invoker.SelectItem(processId, Locator(step), step.ItemName, step.WindowTitle);
    }

    private OperationResult Hover(BatchStep step, int processId)
    {
        return _invoker.Hover(
            processId, Locator(step), step.ExpectedTooltipText, step.WindowTitle, step.TimeoutMs);
    }

    private OperationResult Wait(BatchStep step, int processId)
    {
        if (!WaitConditionParser.TryParse(step.Condition, out WaitCondition condition))
        {
            return OperationResult.Failure(
                $"wait step has unknown condition \"{step.Condition}\". " +
                $"Accepted values: {WaitConditionParser.AcceptedValues}.");
        }
        return _waiter.Wait(
            processId,
            Locator(step),
            condition,
            step.TimeoutMs,
            step.PollIntervalMs,
            step.WindowTitle,
            step.ExpectedValue,
            step.StableForMs);
    }

    private OperationResult GetState(BatchStep step, int processId)
    {
        return _stateReader.Read(processId, Locator(step), step.WindowTitle);
    }

    private OperationResult Screenshot(BatchStep step, int processId)
    {
        if (string.IsNullOrEmpty(step.OutputPath))
        {
            return OperationResult.Failure(ScreenshotMissingPath);
        }
        ScreenshotResult result = _capture.Capture(processId, step.OutputPath, step.WindowTitle);
        return OperationResult.OkWith(result);
    }

    private static ElementLocator Locator(BatchStep step) => new(
        step.AutomationId,
        step.Name,
        step.ClassName,
        step.ControlType,
        step.NameContains,
        step.Index,
        step.DescendantText);

    private static bool TryClickStyle(string? raw, out ClickStyle style, out OperationResult error)
    {
        error = null!;
        if (string.IsNullOrEmpty(raw))
        {
            style = ClickStyle.Primary;
            return true;
        }
        if (ClickStyleParser.TryParse(raw, out style))
        {
            return true;
        }
        error = OperationResult.Failure(
            $"Unknown clickStyle \"{raw}\". Accepted values: {ClickStyleParser.AcceptedValues}.");
        return false;
    }

    private static bool TryCoordinateSpace(string? raw, out bool windowRelative, out OperationResult error)
    {
        error = null!;
        string space = (raw ?? "window").Trim();
        if (string.Equals(space, "window", StringComparison.OrdinalIgnoreCase))
        {
            windowRelative = true;
            return true;
        }
        if (string.Equals(space, "screen", StringComparison.OrdinalIgnoreCase))
        {
            windowRelative = false;
            return true;
        }
        windowRelative = true;
        error = OperationResult.Failure(
            $"Unknown coordinateSpace \"{raw}\". Accepted values: window, screen.");
        return false;
    }

    private static bool IsFatal(Exception ex) =>
        ex is OutOfMemoryException or StackOverflowException or ThreadAbortException;
}
