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

/// <summary>The physical pointer operations a click is made of; replaced by a recorder in tests.</summary>
internal interface IPointerDriver
{
    Point Position { get; }

    void MoveTo(Point point);

    void Wait(int milliseconds);

    /// <summary>Press and release at the current position, without moving.</summary>
    void Click(MouseButton button);

    /// <summary>Two press and release pairs at the current position, without moving.</summary>
    void DoubleClick(MouseButton button);

    /// <summary>Turn the wheel by whole notches at the current position; positive is up, or right when horizontal.</summary>
    void Scroll(int notches, bool horizontal);
}

internal sealed class FlaUiPointerDriver : IPointerDriver
{
    public static readonly FlaUiPointerDriver Instance = new();

    public Point Position => Mouse.Position;

    public void MoveTo(Point point) => Mouse.Position = point;

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
/// pointer position. The press is refused when the pointer is no longer where
/// it was sent, because something else moved it during the settle.
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
        IPointerDriver? driver = null)
    {
        IPointerDriver pointer = driver ?? FlaUiPointerDriver.Instance;
        if (Settle(pointer, point, settleMs, revalidate) is { } refusal)
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
        IPointerDriver? driver = null)
    {
        IPointerDriver pointer = driver ?? FlaUiPointerDriver.Instance;
        if (Settle(pointer, point, settleMs, revalidate) is { } refusal)
        {
            return refusal;
        }
        if (holdMs is > 0)
        {
            pointer.Wait(holdMs.Value);
            return CheckPosition(pointer, point, holdMs.Value);
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
        IPointerDriver? driver = null)
    {
        IPointerDriver pointer = driver ?? FlaUiPointerDriver.Instance;
        if (Settle(pointer, point, settleMs, revalidate) is { } refusal)
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

    private static OperationResult? Settle(IPointerDriver pointer, Point point, int? settleMs, Func<string?>? revalidate)
    {
        int settle = settleMs ?? DefaultSettleMs;
        pointer.MoveTo(point);
        pointer.Wait(settle);
        if (CheckPosition(pointer, point, settle) is { } moved)
        {
            return moved;
        }
        return revalidate?.Invoke() is { } refusal ? OperationResult.Failure(refusal) : null;
    }

    private static OperationResult? CheckPosition(IPointerDriver pointer, Point point, int waitedMs)
    {
        Point actual = pointer.Position;
        if (Math.Abs(actual.X - point.X) <= CursorTolerancePx && Math.Abs(actual.Y - point.Y) <= CursorTolerancePx)
        {
            return null;
        }
        return OperationResult.FailureWith(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{CursorMovedCode}: the pointer is at ({actual.X}, {actual.Y}) instead of ({point.X}, {point.Y}) " +
                $"after {waitedMs} ms; other input moved it. Check get_user_activity before retrying."),
            new { expectedX = point.X, expectedY = point.Y, actualX = actual.X, actualY = actual.Y });
    }
}
