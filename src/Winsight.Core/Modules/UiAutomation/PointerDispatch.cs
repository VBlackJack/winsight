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

using System.Drawing;
using System.Globalization;
using FlaUI.Core.Input;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>Diagnostics returned by a single synthetic pointer move.</summary>
internal readonly record struct PointerMoveResult(
    uint SendInputCount,
    int LastWin32Error,
    int NormalizedX,
    int NormalizedY,
    VirtualDesktopBounds VirtualDesktop,
    string Method = PointerMoveMethods.SendInputVirtualDesk);

/// <summary>The physical pointer operations a click is made of; replaced by a recorder in tests.</summary>
internal interface IPointerDriver
{
    Point Position { get; }

    PointerMoveResult MoveTo(Point point);

    PointerMoveResult MoveToWithMethod(Point point, string method) => MoveTo(point);

    void Wait(int milliseconds);

    /// <summary>Press and release at the current position, without moving.</summary>
    void Click(MouseButton button);

    /// <summary>Two press and release pairs at the current position, without moving.</summary>
    void DoubleClick(MouseButton button);

    /// <summary>Turn the wheel by whole notches at the current position; positive is up, or right when horizontal.</summary>
    void Scroll(int notches, bool horizontal);
}

internal sealed class NativePointerDriver : IPointerDriver
{
    public static readonly NativePointerDriver Instance = new();

    public Point Position =>
        PointerDiagnostics.ReadCursorPos() is { } pt ? new Point(pt.X, pt.Y) : Mouse.Position;

    public PointerMoveResult MoveTo(Point point) =>
        PointerDiagnostics.MoveSendInputVirtualDesk(point);

    public PointerMoveResult MoveToWithMethod(Point point, string method) =>
        PointerDiagnostics.MoveWithMethod(point, Position, method);

    public void Wait(int milliseconds) => Thread.Sleep(milliseconds);

    public void Click(MouseButton button) => Mouse.Click(button);

    public void DoubleClick(MouseButton button) => Mouse.DoubleClick(button);

    public void Scroll(int notches, bool horizontal)
    {
        if (horizontal)
        {
            Mouse.HorizontalScroll(notches);
        }
        else
        {
            Mouse.Scroll(notches);
        }
    }
}

/// <summary>
/// Clicks in three beats: move the pointer, let the target see the move, then
/// press at the current position. Immediate-mode UIs (iced on winit) resolve
/// hover from the last WM_MOUSEMOVE once per frame and act on that hover when
/// the button goes down; a move and a press sent together act on the previous
/// pointer position. The press is refused when the pointer did not reach the
/// target: <c>move_not_applied</c> when it never left its starting position, or
/// <c>cursor_moved</c> when something else moved it to another position.
/// </summary>
public static class PointerDispatch
{
    /// <summary>Default wait between the move and the press, long enough for a few frames.</summary>
    public const int DefaultSettleMs = 150;

    /// <summary>Upper bound for a caller-supplied settle.</summary>
    public const int MaxSettleMs = 2000;

    /// <summary>Rounding slack between the pointer position set and the one read back.</summary>
    internal const int CursorTolerancePx = 1;

    internal const string CursorMovedCode = "cursor_moved";

    internal const string MoveNotAppliedCode = "move_not_applied";

    /// <summary>Largest wheel turn one call sends, in notches.</summary>
    public const int MaxScrollNotches = 50;

    /// <summary>Longest time a hover keeps the pointer still after the settle.</summary>
    public const int MaxHoverHoldMs = 10_000;

    /// <summary>Validate a caller-supplied settle; null means the default.</summary>
    public static string? ValidateSettle(int? settleMs) =>
        settleMs is < 0 or > MaxSettleMs
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"settleMs must be between 0 and {MaxSettleMs} (got {settleMs}).")
            : null;

    /// <summary>Validate a wheel turn: a non-zero number of notches within the bound.</summary>
    public static string? ValidateScroll(int notches) =>
        notches == 0 || Math.Abs(notches) > MaxScrollNotches
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"delta must be a non-zero number of wheel notches between -{MaxScrollNotches} and {MaxScrollNotches} (got {notches}).")
            : null;

    /// <summary>Validate a hover hold; null means no hold beyond the settle.</summary>
    public static string? ValidateHold(int? holdMs) =>
        holdMs is < 0 or > MaxHoverHoldMs
            ? string.Create(
                CultureInfo.InvariantCulture, $"holdMs must be between 0 and {MaxHoverHoldMs} (got {holdMs}).")
            : null;

    /// <summary>
    /// Move to <paramref name="point"/>, settle, check the pointer is still
    /// there, then turn the wheel. Windows sends the wheel to the window under
    /// the pointer, so the pointer must be on the target first.
    /// </summary>
    internal static OperationResult? Scroll(
        Point point,
        int notches,
        bool horizontal,
        int? settleMs = null,
        Func<string?>? revalidate = null,
        IPointerDriver? driver = null,
        IntegrityComparison? integrity = null) =>
        Scroll(point, notches, horizontal, out _, settleMs, revalidate, driver, integrity);

    internal static OperationResult? Scroll(
        Point point,
        int notches,
        bool horizontal,
        out InputDispatchInfo? dispatch,
        int? settleMs = null,
        Func<string?>? revalidate = null,
        IPointerDriver? driver = null,
        IntegrityComparison? integrity = null,
        uint? expectedLeaseThreadId = null)
    {
        IPointerDriver pointer = driver ?? NativePointerDriver.Instance;
        if (Settle(pointer, point, settleMs, revalidate, integrity, expectedLeaseThreadId, out dispatch) is { } refusal)
        {
            return refusal;
        }
        pointer.Scroll(notches, horizontal);
        return null;
    }

    /// <summary>
    /// Move to <paramref name="point"/>, settle, and keep the pointer there for
    /// <paramref name="holdMs"/> so hover effects and tooltips can appear. The
    /// pointer is checked again after the hold.
    /// </summary>
    internal static OperationResult? Hover(
        Point point,
        int? holdMs = null,
        int? settleMs = null,
        Func<string?>? revalidate = null,
        IPointerDriver? driver = null,
        IntegrityComparison? integrity = null) =>
        Hover(point, out _, holdMs, settleMs, revalidate, driver, integrity);

    internal static OperationResult? Hover(
        Point point,
        out InputDispatchInfo? dispatch,
        int? holdMs = null,
        int? settleMs = null,
        Func<string?>? revalidate = null,
        IPointerDriver? driver = null,
        IntegrityComparison? integrity = null,
        uint? expectedLeaseThreadId = null)
    {
        IPointerDriver pointer = driver ?? NativePointerDriver.Instance;
        if (Settle(pointer, point, settleMs, revalidate, integrity, expectedLeaseThreadId, out dispatch) is { } refusal)
        {
            return refusal;
        }
        if (holdMs is > 0)
        {
            pointer.Wait(holdMs.Value);
            return CheckPosition(pointer, point, holdMs.Value, startPosition: null, moveResult: null, integrity: integrity);
        }
        return null;
    }

    /// <summary>
    /// Move to <paramref name="point"/>, wait <paramref name="settleMs"/>, check
    /// the pointer is still there, run <paramref name="revalidate"/> (a hover
    /// may have opened a popup over the point), then press. Returns null on
    /// success, or the failure that stopped the press.
    /// </summary>
    internal static OperationResult? Click(
        Point point,
        ClickStyle style,
        int? settleMs = null,
        Func<string?>? revalidate = null,
        IPointerDriver? driver = null,
        IntegrityComparison? integrity = null) =>
        Click(point, style, out _, settleMs, revalidate, driver, integrity);

    internal static OperationResult? Click(
        Point point,
        ClickStyle style,
        out InputDispatchInfo? dispatch,
        int? settleMs = null,
        Func<string?>? revalidate = null,
        IPointerDriver? driver = null,
        IntegrityComparison? integrity = null,
        uint? expectedLeaseThreadId = null)
    {
        IPointerDriver pointer = driver ?? NativePointerDriver.Instance;
        if (Settle(pointer, point, settleMs, revalidate, integrity, expectedLeaseThreadId, out dispatch) is { } refusal)
        {
            return refusal;
        }

        switch (style)
        {
            case ClickStyle.DoubleClick:
                pointer.DoubleClick(MouseButton.Left);
                break;
            case ClickStyle.RightClick:
                pointer.Click(MouseButton.Right);
                break;
            default:
                pointer.Click(MouseButton.Left);
                break;
        }
        return null;
    }

    /// <summary>Short poll window after an asynchronous SendInput move before triggering fallback.</summary>
    private const int AsyncMovePollStepMs = 5;
    private const int AsyncMovePollAttempts = 5;

    private readonly record struct MoveAttemptTrace(
        string Method,
        uint ApiResult,
        int LastWin32Error,
        int AfterX,
        int AfterY);

    private static OperationResult? Settle(
        IPointerDriver pointer,
        Point point,
        int? settleMs,
        Func<string?>? revalidate,
        IntegrityComparison? integrity,
        uint? expectedLeaseThreadId,
        out InputDispatchInfo? dispatch)
    {
        InputContextSnapshot inputContext = InputContext.Capture();
        uint? leaseThreadId = expectedLeaseThreadId ?? DesktopInputLease.CurrentThreadLeaseId;
        if (leaseThreadId is uint activeLeaseTid && inputContext.ThreadId != 0 && activeLeaseTid != inputContext.ThreadId)
        {
            dispatch = InputContext.ToDispatchInfo(inputContext, null);
            return OperationResult.FailureWith(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"lease_thread_mismatch: DesktopInputLease is held by thread {activeLeaseTid} but pointer dispatch ran on thread {inputContext.ThreadId}."),
                new
                {
                    code = "lease_thread_mismatch",
                    leaseThreadId = activeLeaseTid,
                    dispatchThreadId = inputContext.ThreadId,
                    inputContext,
                });
        }
        if (InputContext.EvaluateBlock(inputContext) is { } blocked)
        {
            dispatch = InputContext.ToDispatchInfo(inputContext, null);
            return OperationResult.FailureWith(
                blocked.Message,
                new
                {
                    code = blocked.Code,
                    restrictions = blocked.Restrictions,
                    inputContext = blocked.Context,
                });
        }

        DesktopSwitchAttempt? desktopSwitch = null;
        if (!inputContext.DesktopMatchesInput)
        {
            desktopSwitch = InputContext.TryAttachToInputDesktop(inputContext);
            if (desktopSwitch.Succeeded)
            {
                inputContext = InputContext.Capture();
            }
        }
        int settle = settleMs ?? DefaultSettleMs;
        Point startPosition = pointer.Position;
        List<string> attemptedMethods = new() { PointerMoveMethods.SendInputVirtualDesk };
        List<MoveAttemptTrace> attemptTraces = new();
        PointerMoveResult moveResult = pointer.MoveTo(point);
        string usedMethod = string.IsNullOrEmpty(moveResult.Method)
            ? PointerMoveMethods.SendInputVirtualDesk
            : moveResult.Method;

        Point afterPrimary = WaitForCursorDeparture(pointer, startPosition, point);
        attemptTraces.Add(new MoveAttemptTrace(
            usedMethod,
            moveResult.SendInputCount,
            moveResult.LastWin32Error,
            afterPrimary.X,
            afterPrimary.Y));

        if (!IsNear(point, startPosition) && IsNear(afterPrimary, startPosition))
        {
            foreach (string fallbackMethod in PointerMoveMethods.FallbackOrder)
            {
                attemptedMethods.Add(fallbackMethod);
                PointerMoveResult fallbackResult = pointer.MoveToWithMethod(point, fallbackMethod);
                Point afterFallback = fallbackMethod == PointerMoveMethods.SetCursorPos
                    ? pointer.Position
                    : WaitForCursorDeparture(pointer, startPosition, point);
                attemptTraces.Add(new MoveAttemptTrace(
                    fallbackMethod,
                    fallbackResult.SendInputCount,
                    fallbackResult.LastWin32Error,
                    afterFallback.X,
                    afterFallback.Y));
                if (!IsNear(afterFallback, startPosition))
                {
                    moveResult = fallbackResult;
                    usedMethod = fallbackMethod;
                    break;
                }
            }
        }

        dispatch = InputContext.ToDispatchInfo(inputContext, desktopSwitch, usedMethod);
        pointer.Wait(settle);
        if (CheckPosition(pointer, point, settle, startPosition, moveResult, integrity, inputContext, desktopSwitch, attemptedMethods, attemptTraces, leaseThreadId) is { } moved)
        {
            return moved;
        }
        return revalidate?.Invoke() is { } refusal ? OperationResult.Failure(refusal) : null;
    }

    /// <summary>
    /// SendInput is processed asynchronously by Windows's Raw Input Thread;
    /// poll briefly before concluding the move was not applied and triggering
    /// fallback methods. When the cursor moves immediately, zero waits occur.
    /// </summary>
    private static Point WaitForCursorDeparture(IPointerDriver pointer, Point startPosition, Point target)
    {
        Point current = pointer.Position;
        if (IsNear(target, startPosition) || !IsNear(current, startPosition))
        {
            return current;
        }
        for (int i = 0; i < AsyncMovePollAttempts; i++)
        {
            pointer.Wait(AsyncMovePollStepMs);
            current = pointer.Position;
            if (!IsNear(current, startPosition))
            {
                return current;
            }
        }
        return current;
    }

    private static OperationResult? CheckPosition(
        IPointerDriver pointer,
        Point target,
        int waitedMs,
        Point? startPosition,
        PointerMoveResult? moveResult,
        IntegrityComparison? integrity,
        InputContextSnapshot? inputContext = null,
        DesktopSwitchAttempt? desktopSwitch = null,
        IReadOnlyList<string>? attemptedMethods = null,
        IReadOnlyList<MoveAttemptTrace>? attemptTraces = null,
        uint? expectedLeaseThreadId = null)
    {
        Point actual = pointer.Position;
        if (IsNear(actual, target))
        {
            return null;
        }

        if (startPosition is Point start && IsNear(actual, start))
        {
            InputContextSnapshot effectiveContext = inputContext ?? InputContext.Capture();
            IntegrityComparison? effectiveIntegrity = integrity;
            string integrityDetail = effectiveIntegrity is not null
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"currentIntegrity={ProcessIntegrity.FormatIntegrity(effectiveIntegrity.Current)}, targetIntegrity={ProcessIntegrity.FormatIntegrity(effectiveIntegrity.Target)}, uipiBlocked={effectiveIntegrity.IsUipiBlocked}")
                : "integrity=unknown";
            string sendInputDetail = moveResult is PointerMoveResult move
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"method={move.Method}, SendInput={move.SendInputCount}, win32Error={move.LastWin32Error}, normalized=({move.NormalizedX}, {move.NormalizedY}), virtualDesktop=({move.VirtualDesktop.Left},{move.VirtualDesktop.Top} {move.VirtualDesktop.Width}x{move.VirtualDesktop.Height})")
                : "SendInput=unknown";
            string methodsDetail = attemptTraces is { Count: > 0 }
                ? $"attempts=[{string.Join("; ", attemptTraces.Select(t => $"{t.Method}:res={t.ApiResult},err={t.LastWin32Error},pos=({t.AfterX},{t.AfterY})"))}]"
                : attemptedMethods is { Count: > 0 }
                    ? $"attemptedMethods=[{string.Join(",", attemptedMethods)}]"
                    : "attemptedMethods=[]";
            uint? leaseThreadId = expectedLeaseThreadId ?? DesktopInputLease.CurrentThreadLeaseId;
            ForegroundProcessInfo? foreground = PointerDiagnostics.ReadForegroundProcess();
            PointerClipRect? clipCursor = PointerDiagnostics.ReadClipCursor();
            CursorStateInfo? cursorInfo = PointerDiagnostics.ReadCursorInfo();
            string threadDetail = string.Create(
                CultureInfo.InvariantCulture,
                $"leaseThreadId={leaseThreadId?.ToString(CultureInfo.InvariantCulture) ?? "none"}, dispatchThreadId={effectiveContext.ThreadId}, fg={foreground?.ProcessName ?? "?"}({foreground?.ProcessId ?? 0})");
            string contextDetail = InputContext.FormatSummary(effectiveContext, desktopSwitch);

            string message = string.Create(
                CultureInfo.InvariantCulture,
                $"{MoveNotAppliedCode}: the pointer remained at its starting position ({actual.X}, {actual.Y}) instead of moving to ({target.X}, {target.Y}) after {waitedMs} ms ({sendInputDetail}; {methodsDetail}; {threadDetail}; {integrityDetail}; {contextDetail}). The synthetic move was not applied or was dropped by the OS.");

            object data = new
            {
                code = MoveNotAppliedCode,
                startX = start.X,
                startY = start.Y,
                expectedX = target.X,
                expectedY = target.Y,
                actualX = actual.X,
                actualY = actual.Y,
                moveMethod = moveResult?.Method,
                attemptedMethods,
                attemptTraces,
                leaseThreadId,
                dispatchThreadId = effectiveContext.ThreadId,
                foregroundWindow = foreground,
                clipCursor,
                cursorInfo,
                sendInputCount = moveResult?.SendInputCount,
                lastWin32Error = moveResult?.LastWin32Error,
                normalizedX = moveResult?.NormalizedX,
                normalizedY = moveResult?.NormalizedY,
                virtualDesktop = moveResult is PointerMoveResult m
                    ? new
                    {
                        left = m.VirtualDesktop.Left,
                        top = m.VirtualDesktop.Top,
                        width = m.VirtualDesktop.Width,
                        height = m.VirtualDesktop.Height,
                    }
                    : null,
                dpiAwareness = ProcessDpiAwareness.DescribeCurrent(),
                integrity = effectiveIntegrity is not null
                    ? new
                    {
                        current = effectiveIntegrity.Current.Name,
                        currentRid = effectiveIntegrity.Current.Rid,
                        target = effectiveIntegrity.Target.Name,
                        targetRid = effectiveIntegrity.Target.Rid,
                        uipiBlocked = effectiveIntegrity.IsUipiBlocked,
                    }
                    : null,
                inputContext = effectiveContext,
                desktopSwitch,
            };
            return OperationResult.FailureWith(message, data);
        }

        string movedMessage = string.Create(
            CultureInfo.InvariantCulture,
            $"{CursorMovedCode}: the pointer is at ({actual.X}, {actual.Y}) instead of ({target.X}, {target.Y}) after {waitedMs} ms; other input moved it. Check get_user_activity before retrying.");

        return OperationResult.FailureWith(
            movedMessage,
            new
            {
                code = CursorMovedCode,
                startX = startPosition?.X,
                startY = startPosition?.Y,
                expectedX = target.X,
                expectedY = target.Y,
                actualX = actual.X,
                actualY = actual.Y,
            });
    }

    private static bool IsNear(Point a, Point b) =>
        Math.Abs(a.X - b.X) <= CursorTolerancePx && Math.Abs(a.Y - b.Y) <= CursorTolerancePx;
}
