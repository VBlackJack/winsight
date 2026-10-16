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
using System.Drawing;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Clipboard;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Core.Modules.UserActivity;
using Winsight.Core.Modules.Ocr;
using Winsight.Core.Modules.VisualRegression;

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
    private readonly IForegroundReader _foreground;
    private readonly VisualWaiter _visual;
    private readonly VisualTextReader _text;

    public StepRunner(
        ActionInvoker invoker,
        ElementWaiter waiter,
        ElementStateReader stateReader,
        WindowCapture capture)
        : this(invoker, waiter, stateReader, capture, NativeForegroundReader.Instance)
    {
    }

    internal StepRunner(
        ActionInvoker invoker,
        ElementWaiter waiter,
        ElementStateReader stateReader,
        WindowCapture capture,
        IForegroundReader foreground)
    {
        _invoker = invoker;
        _waiter = waiter;
        _stateReader = stateReader;
        _capture = capture;
        _foreground = foreground;
        _visual = new VisualWaiter(capture, new VisualDiffer());
        _text = new VisualTextReader(capture);
    }

    /// <summary>
    /// Execute the steps in order. <paramref name="defaultProcessId"/> applies
    /// to steps that omit their own <see cref="BatchStep.ProcessId"/>. When
    /// <paramref name="stopOnError"/> is true, the run stops at the first failing
    /// step; otherwise it runs them all and reports every outcome. When
    /// <paramref name="maxDurationMs"/> is set, the run stops before a step once
    /// the elapsed time reaches the budget, marking the result timed out. When
    /// <paramref name="stopOnForegroundChange"/> is true, the run stops before a
    /// screen-changing step if the foreground moved to a window the batch did
    /// not cause (see <see cref="ForegroundGuard"/>), whatever
    /// <paramref name="stopOnError"/> says.
    /// </summary>
    public BatchResult Run(
        IReadOnlyList<BatchStep> steps,
        int? defaultProcessId,
        bool stopOnError,
        int? maxDurationMs = null,
        bool dryRun = false,
        bool stopOnForegroundChange = true)
    {
        ArgumentNullException.ThrowIfNull(steps);

        List<StepResult> results = new(steps.Count);
        int? failedAtIndex = null;
        bool timedOut = false;
        bool foregroundChanged = false;
        bool guardForeground = stopOnForegroundChange && !dryRun;
        HashSet<int> batchProcessIds = steps
            .Select(step => step.ProcessId ?? defaultProcessId)
            .OfType<int>()
            .ToHashSet();
        ForegroundSample baseline = guardForeground ? _foreground.Read() : default;
        Bitmap? reference = null;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {

        for (int i = 0; i < steps.Count; i++)
        {
            if (maxDurationMs is int budget && stopwatch.ElapsedMilliseconds >= budget)
            {
                timedOut = true;
                break;
            }

            BatchStep step = steps[i];
            bool screenStep = IsScreenAction(Normalize(step.Action));
            if (guardForeground && screenStep)
            {
                ForegroundSample current = _foreground.Read();
                if (!ForegroundGuard.Accepts(baseline, current, _foreground.Exists(baseline.Handle), batchProcessIds))
                {
                    results.Add(new StepResult(
                        i, step.Action ?? string.Empty, step.Label, false,
                        ForegroundGuard.Describe(baseline, current),
                        new { expectedForeground = baseline, actualForeground = current }));
                    failedAtIndex ??= i;
                    foregroundChanged = true;
                    break;
                }
            }

            // A visual-change wait must compare against the screen before the
            // action that precedes it, or a fast change happens before its reference.
            if (screenStep && !dryRun && i + 1 < steps.Count &&
                Normalize(steps[i + 1].Action) == WaitVisualChangeAction)
            {
                reference?.Dispose();
                reference = TryCaptureReference(steps[i + 1], defaultProcessId);
            }

            OperationResult outcome = ExecuteSafely(step, defaultProcessId, dryRun, reference);
            if (Normalize(step.Action) == WaitVisualChangeAction)
            {
                reference?.Dispose();
                reference = null;
            }
            results.Add(new StepResult(
                i, step.Action ?? string.Empty, step.Label, outcome.Success, outcome.Error, outcome.Data));
            if (guardForeground && screenStep)
            {
                baseline = _foreground.Read();
            }

            if (!outcome.Success)
            {
                failedAtIndex ??= i;
                if (stopOnError)
                {
                    break;
                }
            }
        }

        }
        finally
        {
            reference?.Dispose();
        }

        int completed = results.Count(r => r.Success);
        bool success = failedAtIndex is null && !timedOut;
        return new BatchResult(
            success, completed, steps.Count, failedAtIndex, results, Exceptions: null, TimedOut: timedOut,
            ForegroundChanged: foregroundChanged);
    }

    private Bitmap? TryCaptureReference(BatchStep wait, int? defaultProcessId)
    {
        if ((wait.ProcessId ?? defaultProcessId) is not int processId)
        {
            return null;
        }
        try
        {
            return _capture.CaptureFrame(processId, wait.WindowTitle, wait.Region).Detach();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // The wait captures its own reference when it runs.
            return null;
        }
    }

    private OperationResult ExecuteSafely(BatchStep step, int? defaultProcessId, bool dryRun, Bitmap? reference)
    {
        try
        {
            return Execute(step, defaultProcessId, dryRun, reference);
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            return OperationResult.Failure($"{step.Action} step threw: {ex.Message}");
        }
    }

    private OperationResult Execute(BatchStep step, int? defaultProcessId, bool dryRun, Bitmap? reference)
    {
        string action = Normalize(step.Action);
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

        if (ValidateStep(action, step) is { } validationError)
        {
            return validationError;
        }

        if (dryRun && IsMutatingAction(action))
        {
            return OperationResult.OkWith(new
            {
                dryRun = true,
                action,
                processId = pid,
                locator = Locator(step),
                step.Text,
                step.Chord,
                step.Mode,
                step.ItemName,
                step.X,
                step.Y,
                step.ClickStyle,
                step.CoordinateSpace,
                step.AllowOutOfWindow,
                step.SettleMs,
                step.FocusWindow,
                step.Delta,
                step.Horizontal,
                step.HoldMs,
                step.WindowTitle,
                step.OutputPath,
            });
        }

        if (IsScreenAction(action) && UserActivityPolicy.CheckYield() is { } userActive)
        {
            return OperationResult.Failure(userActive);
        }

        return action switch
        {
            "click" => Click(step, pid),
            "click_point" => ClickPoint(step, pid),
            "scroll_point" => ScrollPoint(step, pid),
            "hover_point" => HoverPoint(step, pid),
            "type" => Type(step, pid),
            "type_keys" => TypeKeys(step, pid),
            "key_chord" => KeyChord(step, pid),
            "select" => Select(step, pid),
            "hover" => Hover(step, pid),
            "wait" => Wait(step, pid),
            WaitVisualChangeAction => _visual.WaitForChange(
                pid, step.WindowTitle, step.Region, VisualOptions(step), reference),
            WaitVisualStableAction => _visual.WaitForStable(pid, step.WindowTitle, step.Region, VisualOptions(step)),
            "assert" when IsTextVisible(step) => AssertTextVisible(step, pid),
            "assert" => Assert(step, pid),
            "get_state" => GetState(step, pid),
            "screenshot" => Screenshot(step, pid),
            _ => OperationResult.Failure(
                $"Unknown action \"{step.Action}\". Accepted: {AcceptedActions}."),
        };
    }

    private static OperationResult? ValidateStep(string action, BatchStep step)
    {
        return action switch
        {
            "click" => ValidateClick(step),
            "click_point" => ValidateClickPoint(step),
            "scroll_point" => ValidatePoint(step, "scroll_point") ?? ValidateDelta(step),
            "hover_point" => ValidatePoint(step, "hover_point") ??
                (PointerDispatch.ValidateHold(step.HoldMs) is { } holdError ? OperationResult.Failure(holdError) : null),
            "type_keys" => ValidateTypeKeys(step),
            "type" => step.Text is null
                ? OperationResult.Failure("type step requires text.")
                : null,
            "key_chord" => ValidateKeyChord(step),
            "select" => string.IsNullOrEmpty(step.ItemName)
                ? OperationResult.Failure("select step requires itemName.")
                : null,
            "wait" => WaitConditionParser.TryParse(step.Condition, out _)
                ? null
                : OperationResult.Failure(
                    $"wait step has unknown condition \"{step.Condition}\". " +
                    $"Accepted values: {WaitConditionParser.AcceptedValues}."),
            WaitVisualChangeAction or WaitVisualStableAction => VisualOptions(step).Validate() is { } invalid
                ? OperationResult.Failure($"{action} step: {invalid}")
                : null,
            "assert" when IsTextVisible(step) => string.IsNullOrWhiteSpace(step.ExpectedValue)
                ? OperationResult.Failure("assert text_visible requires expectedValue, the text to find.")
                : null,
            "assert" => AssertionEvaluator.TryParseOperator(step.Operator, out _)
                ? null
                : OperationResult.Failure(
                    $"assert step has unknown operator \"{step.Operator}\". " +
                    $"Accepted: {AssertionEvaluator.AcceptedOperators}, {VisualTextMatcher.TextVisibleOperator}."),
            "screenshot" => string.IsNullOrEmpty(step.OutputPath)
                ? OperationResult.Failure(ScreenshotMissingPath)
                : null,
            "hover" or "get_state" => null,
            _ => OperationResult.Failure(
                $"Unknown action \"{step.Action}\". Accepted: {AcceptedActions}."),
        };
    }

    private static OperationResult? ValidateClick(BatchStep step)
    {
        return TryClickStyle(step.ClickStyle, out _, out OperationResult error)
            ? null
            : error;
    }

    private static OperationResult? ValidateClickPoint(BatchStep step)
    {
        if (step.X is not int || step.Y is not int)
        {
            return OperationResult.Failure("click_point step requires x and y.");
        }
        if (!TryClickStyle(step.ClickStyle, out _, out OperationResult clickError))
        {
            return clickError;
        }
        if (PointerDispatch.ValidateSettle(step.SettleMs) is { } settleError)
        {
            return OperationResult.Failure(settleError);
        }
        return TryCoordinateSpace(step.CoordinateSpace, out _, out OperationResult spaceError)
            ? null
            : spaceError;
    }

    private static OperationResult? ValidatePoint(BatchStep step, string action)
    {
        if (step.X is not int || step.Y is not int)
        {
            return OperationResult.Failure($"{action} step requires x and y.");
        }
        if (PointerDispatch.ValidateSettle(step.SettleMs) is { } settleError)
        {
            return OperationResult.Failure(settleError);
        }
        return TryCoordinateSpace(step.CoordinateSpace, out _, out OperationResult spaceError) ? null : spaceError;
    }

    private static OperationResult? ValidateDelta(BatchStep step)
    {
        if (step.Delta is not int delta)
        {
            return OperationResult.Failure("scroll_point step requires delta.");
        }
        return PointerDispatch.ValidateScroll(delta) is { } deltaError ? OperationResult.Failure(deltaError) : null;
    }

    private OperationResult ScrollPoint(BatchStep step, int processId)
    {
        TryCoordinateSpace(step.CoordinateSpace, out bool windowRelative, out _);
        return _invoker.ScrollPoint(
            processId, step.X!.Value, step.Y!.Value, step.Delta!.Value, step.Horizontal ?? false, step.WindowTitle,
            windowRelative, step.AllowOutOfWindow ?? false, step.FocusWindow ?? false, step.SettleMs);
    }

    private OperationResult HoverPoint(BatchStep step, int processId)
    {
        TryCoordinateSpace(step.CoordinateSpace, out bool windowRelative, out _);
        return _invoker.HoverPoint(
            processId, step.X!.Value, step.Y!.Value, step.HoldMs, step.WindowTitle,
            windowRelative, step.AllowOutOfWindow ?? false, step.FocusWindow ?? false, step.SettleMs);
    }

    private static OperationResult? ValidateTypeKeys(BatchStep step)
    {
        if (TextTyper.Validate(step.Text) is { } invalid)
        {
            return OperationResult.Failure($"type_keys step: {invalid}");
        }
        return TypeKeysMode.TryParse(step.Mode, out _, out string? modeError)
            ? null
            : OperationResult.Failure(modeError!);
    }

    private OperationResult TypeKeys(BatchStep step, int processId)
    {
        if (!TypeKeysMode.TryParse(step.Mode, out bool paste, out string? modeError))
        {
            return OperationResult.Failure(modeError!);
        }
        if (paste && ClipboardPolicy.Refusal("type_keys step mode=paste") is { } refusal)
        {
            return OperationResult.Failure(refusal);
        }
        return _invoker.TypeKeys(
            processId, step.Text!, step.WindowTitle, paste, restoreDelayMs: step.RestoreDelayMs);
    }

    private static OperationResult? ValidateKeyChord(BatchStep step)
    {
        return KeyboardChordParser.TryParse(step.Chord, out _, out string? parseError)
            ? null
            : OperationResult.Failure(parseError!);
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
        return _invoker.ClickPoint(
            processId,
            x,
            y,
            style,
            step.WindowTitle,
            windowRelative,
            step.AllowOutOfWindow ?? false,
            focusWindow: step.FocusWindow ?? false,
            settleMs: step.SettleMs);
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

    private static bool IsTextVisible(BatchStep step) =>
        string.Equals(step.Operator?.Trim(), VisualTextMatcher.TextVisibleOperator, StringComparison.OrdinalIgnoreCase);

    private static VisualWaitOptions VisualOptions(BatchStep step) =>
        new(
            step.TimeoutMs ?? VisualWaitOptions.DefaultTimeoutMs,
            step.PollIntervalMs ?? VisualWaitOptions.DefaultPollMs,
            step.StableComparisons ?? VisualWaitOptions.DefaultStableComparisons,
            step.Threshold,
            step.LocalThreshold);

    private OperationResult AssertTextVisible(BatchStep step, int processId)
    {
        OperationResult read = _text.Read(processId, step.WindowTitle, step.Region);
        if (!read.Success)
        {
            return read;
        }
        VisualTextResult text = (VisualTextResult)read.Data!;
        VisualTextLine? line = VisualTextMatcher.Find(text, step.ExpectedValue!);
        return line is not null
            ? OperationResult.OkWith(new { found = step.ExpectedValue, line })
            : OperationResult.FailureWith(
                $"Assertion failed: \"{step.ExpectedValue}\" is not visible; OCR read: \"{text.Text}\".",
                new { text.Text, text.Region });
    }

    private OperationResult Assert(BatchStep step, int processId)
    {
        if (!AssertionEvaluator.TryParseOperator(step.Operator, out AssertionOperator op))
        {
            return OperationResult.Failure(
                $"assert step has unknown operator \"{step.Operator}\". " +
                $"Accepted: {AssertionEvaluator.AcceptedOperators}.");
        }

        OperationResult read = _stateReader.Read(processId, Locator(step), step.WindowTitle);
        if (!read.Success)
        {
            // The element could not be located; the assertion cannot hold.
            return read;
        }

        ElementState state = (ElementState)read.Data!;
        AssertionResult outcome = AssertionEvaluator.Evaluate(op, step.ExpectedValue, state);
        return outcome.Passed
            ? OperationResult.OkWith(new { assertion = outcome.Detail })
            : OperationResult.Failure($"Assertion failed: {outcome.Detail}");
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

    private static bool IsMutatingAction(string action) =>
        IsScreenAction(action) || action is "screenshot";

    // Steps that send input to the desktop; a screenshot only writes a file.
    private static bool IsScreenAction(string action) =>
        action is "click" or "click_point" or "scroll_point" or "hover_point" or "type" or "type_keys" or
            "key_chord" or "select" or "hover";

    private const string WaitVisualChangeAction = "wait_visual_change";
    private const string WaitVisualStableAction = "wait_visual_stable";
    private const string AcceptedActions =
        "click, click_point, scroll_point, hover_point, type, type_keys, key_chord, select, hover, wait, " +
        "wait_visual_change, " +
        "wait_visual_stable, assert, get_state, screenshot";

    private static string Normalize(string? action) => (action ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsFatal(Exception ex) =>
        ex is OutOfMemoryException or StackOverflowException or ThreadAbortException;
}
