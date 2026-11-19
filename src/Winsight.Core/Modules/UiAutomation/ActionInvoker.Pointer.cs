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
using System.Globalization;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Winsight.Core.Models;
using Winsight.Core.Modules.Clipboard;
using FlaUIControlType = FlaUI.Core.Definitions.ControlType;

namespace Winsight.Core.Modules.UiAutomation;

public sealed partial class ActionInvoker
{
    /// <summary>
    /// Click at a coordinate without locating an element through UI Automation.
    /// This is the escape hatch for surfaces UIA cannot see (custom Canvas,
    /// SkiaSharp, DirectX), and the action partner of <c>find_element_visually</c>.
    /// When <paramref name="windowRelative"/> is true the point is interpreted in
    /// the target window's coordinate space (same origin as a full-window
    /// screenshot) and translated to screen space; otherwise it is treated as an
    /// absolute screen coordinate. The pointer moves first, settles for
    /// <paramref name="settleMs"/> (see <see cref="PointerDispatch"/>), and the
    /// press happens only if it is still there. With
    /// <paramref name="focusWindow"/> the target window is raised and verified
    /// in the foreground before the point is validated.
    /// </summary>
    public OperationResult ClickPoint(
        int processId,
        int x,
        int y,
        ClickStyle style = ClickStyle.Primary,
        string? windowTitle = null,
        bool windowRelative = true,
        bool allowOutOfWindow = false,
        WindowIdentity? expectedWindow = null,
        bool focusWindow = false,
        int? settleMs = null)
    {
        if (PointerDispatch.ValidateSettle(settleMs) is { } settleError)
        {
            return OperationResult.Failure(settleError);
        }
        if (CheckInputIntegrity(processId, out IntegrityComparison integrity) is { } blocked)
        {
            return blocked;
        }
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            return PointActionCore(
                processId, x, y, windowTitle, windowRelative, allowOutOfWindow, expectedWindow, focusWindow,
                (point, revalidate) =>
                {
                    OperationResult? refusal = PointerDispatch.Click(
                        point, style, out InputDispatchInfo? dispatch, settleMs, revalidate, integrity: integrity);
                    return (refusal, dispatch);
                });
        }
    }

    /// <summary>
    /// Turn the mouse wheel by <paramref name="notches"/> at a coordinate, with
    /// the same coordinate spaces, window checks, <paramref name="focusWindow"/>
    /// and pointer settle as <see cref="ClickPoint"/>. Positive notches scroll
    /// up, or right when <paramref name="horizontal"/>.
    /// </summary>
    public OperationResult ScrollPoint(
        int processId,
        int x,
        int y,
        int notches,
        bool horizontal = false,
        string? windowTitle = null,
        bool windowRelative = true,
        bool allowOutOfWindow = false,
        bool focusWindow = false,
        int? settleMs = null)
    {
        if ((PointerDispatch.ValidateScroll(notches) ?? PointerDispatch.ValidateSettle(settleMs)) is { } invalid)
        {
            return OperationResult.Failure(invalid);
        }
        if (CheckInputIntegrity(processId, out IntegrityComparison integrity) is { } blocked)
        {
            return blocked;
        }
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            return PointActionCore(
                processId, x, y, windowTitle, windowRelative, allowOutOfWindow, null, focusWindow,
                (point, revalidate) =>
                {
                    OperationResult? refusal = PointerDispatch.Scroll(
                        point, notches, horizontal, out InputDispatchInfo? dispatch, settleMs, revalidate, integrity: integrity);
                    return (refusal, dispatch);
                });
        }
    }

    /// <summary>
    /// Rest the pointer on a coordinate, with the same checks as
    /// <see cref="ClickPoint"/>, and keep it there for <paramref name="holdMs"/>
    /// so hover styles and tooltips of a drawn UI appear. Nothing is pressed.
    /// </summary>
    public OperationResult HoverPoint(
        int processId,
        int x,
        int y,
        int? holdMs = null,
        string? windowTitle = null,
        bool windowRelative = true,
        bool allowOutOfWindow = false,
        bool focusWindow = false,
        int? settleMs = null)
    {
        if ((PointerDispatch.ValidateHold(holdMs) ?? PointerDispatch.ValidateSettle(settleMs)) is { } invalid)
        {
            return OperationResult.Failure(invalid);
        }
        if (CheckInputIntegrity(processId, out IntegrityComparison integrity) is { } blocked)
        {
            return blocked;
        }
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            return PointActionCore(
                processId, x, y, windowTitle, windowRelative, allowOutOfWindow, null, focusWindow,
                (point, revalidate) =>
                {
                    OperationResult? refusal = PointerDispatch.Hover(
                        point, out InputDispatchInfo? dispatch, holdMs, settleMs, revalidate, integrity: integrity);
                    return (refusal, dispatch);
                });
        }
    }

    /// <summary>
    /// Read-only-safe diagnostic probe for a point inside an authorized target
    /// window: tries all four Windows pointer movement methods in order,
    /// measures GetCursorPos and GetPhysicalCursorPos immediately and after
    /// 50 ms / 150 ms, restores the original cursor position after each
    /// attempt, and reports ClipCursor, GetCursorInfo, mouse button states,
    /// mouse acceleration, foreground process, and input context.
    /// </summary>
    public OperationResult DiagnosePointer(
        int processId,
        int x,
        int y,
        string? windowTitle = null,
        bool windowRelative = true)
    {
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            if (!TryResolveClickWindow(
                    processId, x, y, windowTitle, windowRelative, allowOutOfWindow: false, expectedWindow: null,
                    out WindowIdentity window, out string? resolveError))
            {
                return OperationResult.Failure(resolveError!);
            }

            int screenX;
            int screenY;
            if (windowRelative)
            {
                WindowBounds bounds = window.Bounds;
                if (!WindowPointMapper.Contains(bounds.Width, bounds.Height, x, y))
                {
                    return OperationResult.Failure(
                        $"Window-relative point ({x}, {y}) is outside the target window bounds " +
                        $"{bounds.Width}x{bounds.Height}. Pass coordinates within the window, or use " +
                        "screen-space coordinates.");
                }
                (screenX, screenY) = WindowPointMapper.ToScreen(bounds.Left, bounds.Top, x, y);
            }
            else
            {
                screenX = x;
                screenY = y;
                if (!WindowGeometry.TryResolveBoundsContainingPoint(
                        processId,
                        windowTitle,
                        screenX,
                        screenY,
                        out _,
                        out string? boundsError))
                {
                    return OperationResult.Failure(boundsError!);
                }
            }

            PointerDiagnosticReport report = PointerDiagnostics.RunDiagnostic(new System.Drawing.Point(screenX, screenY));
            return OperationResult.OkWith(report);
        }
    }

    private static OperationResult PointActionCore(
        int processId, int x, int y, string? windowTitle,
        bool windowRelative, bool allowOutOfWindow, WindowIdentity? expectedWindow,
        bool focusWindow, Func<System.Drawing.Point, Func<string?>, (OperationResult? Failure, InputDispatchInfo? Dispatch)> dispatch)
    {
        if (expectedWindow is not null && expectedWindow.ProcessId != processId)
        {
            return OperationResult.Failure("The captured window belongs to a different process.");
        }
        if (!TryResolveClickWindow(processId, x, y, windowTitle, windowRelative, allowOutOfWindow, expectedWindow,
                out WindowIdentity window, out string? resolveError))
        {
            return OperationResult.Failure(resolveError!);
        }
        if (focusWindow)
        {
            WindowFocusResult focus = WindowFocus.FocusWindow(
                new IntPtr(window.Handle), windowTitle ?? string.Empty, processId);
            if (!focus.Success)
            {
                return OperationResult.Failure(focus.Error!);
            }
            // Raising a minimized or covered window can move it: resolve its frame again.
            if (expectedWindow is null)
            {
                if (!TryResolveClickWindow(processId, x, y, windowTitle, windowRelative, allowOutOfWindow, null,
                        out WindowIdentity raised, out resolveError))
                {
                    return OperationResult.Failure(resolveError!);
                }
                if (raised.Handle != window.Handle)
                {
                    return OperationResult.Failure(FocusedWindowChanged);
                }
                window = raised;
            }
        }
        int screenX;
        int screenY;
        if (windowRelative)
        {
            WindowBounds bounds = window.Bounds;
            if (!WindowPointMapper.Contains(bounds.Width, bounds.Height, x, y))
            {
                return OperationResult.Failure(
                    $"Window-relative point ({x}, {y}) is outside the target window bounds " +
                    $"{bounds.Width}x{bounds.Height}. Pass coordinates within the window, or use " +
                    "screen-space coordinates.");
            }
            (screenX, screenY) = WindowPointMapper.ToScreen(bounds.Left, bounds.Top, x, y);
        }
        else
        {
            screenX = x;
            screenY = y;
            if (!allowOutOfWindow)
            {
                if (!WindowGeometry.TryResolveBoundsContainingPoint(
                        processId,
                        windowTitle,
                        screenX,
                        screenY,
                        out _,
                        out string? error))
                {
                    return OperationResult.Failure(error!);
                }
            }
        }

        System.Drawing.Point point = new(screenX, screenY);
        bool allowOtherWindow = allowOutOfWindow && expectedWindow is null;
        // Check before the move, then again after the settle: hovering can open
        // a popup over the point.
        string? RecipientRefusal() =>
            WindowGeometry.TryValidatePointRecipient(window, screenX, screenY, allowOtherWindow, out string? error)
                ? null
                : error;
        if (RecipientRefusal() is { } recipientError)
        {
            return OperationResult.Failure(recipientError);
        }
        (OperationResult? failure, InputDispatchInfo? dispatchInfo) = dispatch(point, RecipientRefusal);
        return failure ?? OperationResult.OkWith(new ClickPointResult(screenX, screenY, dispatchInfo));
    }

    private static bool TryResolveClickWindow(
        int processId, int x, int y, string? windowTitle, bool windowRelative, bool allowOutOfWindow,
        WindowIdentity? expectedWindow, out WindowIdentity window, out string? error)
    {
        if (expectedWindow is not null)
        {
            window = expectedWindow;
            error = null;
            return true;
        }
        return WindowGeometry.TryResolveWindow(
            processId, windowTitle, out window, out error,
            screenPoint: !windowRelative && !allowOutOfWindow ? new System.Drawing.Point(x, y) : null);
    }

    /// <summary>The element's clickable point, or the center of its bounds when it publishes none.</summary>
    private static System.Drawing.Point ClickablePoint(AutomationElement element)
    {
        if (element.TryGetClickablePoint(out System.Drawing.Point clickable))
        {
            return clickable;
        }
        System.Drawing.Rectangle bounds = element.BoundingRectangle;
        return new System.Drawing.Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    }

    /// <summary>
    /// Press the left button on <paramref name="source"/>, hold it, move in
    /// paced steps to <paramref name="target"/> and release, so OLE drag and
    /// drop (WPF <c>DragDrop.DoDragDrop</c>) starts, tracks and drops. Each
    /// endpoint is an element (center plus offset) or a point; points are
    /// window-relative when <paramref name="windowRelative"/> is true. The
    /// target window is brought to the foreground first. Both
    /// points must reach a window of the target process unless
    /// <paramref name="allowOutOfWindow"/> releases the target check.
    /// </summary>
    public OperationResult Drag(
        int processId,
        DragEndpoint source,
        DragEndpoint target,
        DragOptions? options = null,
        string? windowTitle = null,
        bool windowRelative = true,
        bool allowOutOfWindow = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        DragOptions pacing = options ?? new DragOptions();
        string? invalid = source.Validate("source") ?? target.Validate("target") ?? pacing.Validate();
        if (invalid is not null)
        {
            return OperationResult.Failure(invalid);
        }

        if (CheckInputIntegrity(processId, out _) is { } blocked)
        {
            return blocked;
        }

        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            // A drag is physical input: raise the target window first, so a
            // window left on top of it does not swallow the press.
            WindowFocusResult focus = WindowFocus.FocusProcessWindow(processId, windowTitle);
            if (!focus.Success)
            {
                return OperationResult.Failure(focus.Error!);
            }

            using UIA3Automation automation = new();
            if (!TryResolveDragPoint(automation, processId, source, "source", windowTitle, windowRelative,
                    out System.Drawing.Point from, out OperationResult? failure) ||
                !TryResolveDragPoint(automation, processId, target, "target", windowTitle, windowRelative,
                    out System.Drawing.Point to, out failure))
            {
                return failure!;
            }
            if (!TryValidateDragRecipient(processId, from, "source", out string? recipientError) ||
                (!allowOutOfWindow && !TryValidateDragRecipient(processId, to, "target", out recipientError)))
            {
                return OperationResult.Failure(recipientError!);
            }

            if (PrepareInputContext(out InputDispatchInfo dispatch) is { } contextBlocked)
            {
                return contextBlocked;
            }
            int threshold = Math.Max(
                GetSystemMetrics(SystemMetricDragWidth),
                GetSystemMetrics(SystemMetricDragHeight));
            IReadOnlyList<System.Drawing.Point> moves = DragPath.Plan(from, to, pacing.Steps, threshold);

            Mouse.Position = from;
            Thread.Sleep(pacing.StepDelayMs);
            Mouse.Down(MouseButton.Left);
            try
            {
                Thread.Sleep(pacing.HoldMs);
                foreach (System.Drawing.Point move in moves)
                {
                    Mouse.Position = move;
                    Thread.Sleep(pacing.StepDelayMs);
                }
                Thread.Sleep(pacing.DropDelayMs);
            }
            finally
            {
                Mouse.Up(MouseButton.Left);
            }
            return OperationResult.OkWith(new DragResult(from.X, from.Y, to.X, to.Y, moves.Count, dispatch));
        }
    }

    private static bool TryResolveDragPoint(
        UIA3Automation automation,
        int processId,
        DragEndpoint endpoint,
        string role,
        string? windowTitle,
        bool windowRelative,
        out System.Drawing.Point point,
        out OperationResult? failure)
    {
        point = default;
        failure = null;
        if (!endpoint.IsPoint)
        {
            ElementSearchResult search = ElementFinder.FindSingle(automation, processId, endpoint.Locator!, windowTitle);
            if (!search.Found)
            {
                failure = OperationResult.FailureWith($"Drag {role}: {search.Error}", search.Candidates);
                return false;
            }
            AutomationElement element = search.Element!;
            ScrollIntoViewIfOffscreen(element);
            System.Drawing.Rectangle rect = element.BoundingRectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                failure = OperationResult.Failure(
                    $"Drag {role} element has invalid bounds {rect.Width}x{rect.Height}.");
                return false;
            }
            point = new System.Drawing.Point(
                rect.Left + rect.Width / 2 + endpoint.OffsetX,
                rect.Top + rect.Height / 2 + endpoint.OffsetY);
            return true;
        }

        if (!windowRelative)
        {
            point = new System.Drawing.Point(endpoint.X!.Value, endpoint.Y!.Value);
            return true;
        }
        if (!WindowGeometry.TryResolveWindow(processId, windowTitle, out WindowIdentity window, out string? error))
        {
            failure = OperationResult.Failure($"Drag {role}: {error}");
            return false;
        }
        WindowBounds bounds = window.Bounds;
        if (!WindowPointMapper.Contains(bounds.Width, bounds.Height, endpoint.X!.Value, endpoint.Y!.Value))
        {
            failure = OperationResult.Failure(
                $"Drag {role} point ({endpoint.X}, {endpoint.Y}) is outside the target window bounds " +
                $"{bounds.Width}x{bounds.Height}.");
            return false;
        }
        (int screenX, int screenY) = WindowPointMapper.ToScreen(
            bounds.Left, bounds.Top, endpoint.X.Value, endpoint.Y.Value);
        point = new System.Drawing.Point(screenX, screenY);
        return true;
    }

    private static bool TryValidateDragRecipient(
        int processId,
        System.Drawing.Point point,
        string role,
        out string? error)
    {
        if (!WindowGeometry.TryResolveWindow(processId, null, out WindowIdentity window, out error, point) ||
            !WindowGeometry.TryValidatePointRecipient(window, point.X, point.Y, allowOtherWindow: false, out error))
        {
            error = $"Drag {role} ({point.X}, {point.Y}): {error}";
            return false;
        }
        return true;
    }

}
