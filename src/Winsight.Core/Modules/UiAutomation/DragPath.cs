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

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Plans the pointer moves of a drag that OLE drag and drop accepts. The
/// first move leaves the system drag rectangle so the source starts
/// DoDragDrop; the intermediate moves let every drop target along the way see
/// DragEnter/DragOver; the last two moves step one pixel past the target and
/// back, so the drop target receives a DragOver at the exact drop point.
/// </summary>
internal static class DragPath
{
    private const int ThresholdMarginPx = 2;
    private const int SettleOvershootPx = 1;

    /// <summary>
    /// Moves after the button press, in order, ending on <paramref name="to"/>.
    /// </summary>
    /// <param name="from">Screen point where the button is pressed.</param>
    /// <param name="to">Screen point where the button is released.</param>
    /// <param name="steps">Intermediate moves between the threshold exit and the target.</param>
    /// <param name="dragThresholdPx">System drag distance (SM_CXDRAG/SM_CYDRAG).</param>
    internal static IReadOnlyList<Point> Plan(Point from, Point to, int steps, int dragThresholdPx)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(dragThresholdPx);

        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        int exitDistance = dragThresholdPx + ThresholdMarginPx;
        Point exit = length < 1
            ? new Point(from.X + exitDistance, from.Y)
            : new Point(
                from.X + (int)Math.Round(dx / length * exitDistance),
                from.Y + (int)Math.Round(dy / length * exitDistance));

        List<Point> moves = new(steps + 3) { exit };
        for (int i = 1; i <= steps; i++)
        {
            moves.Add(new Point(
                exit.X + (int)Math.Round((to.X - exit.X) * (double)i / steps),
                exit.Y + (int)Math.Round((to.Y - exit.Y) * (double)i / steps)));
        }
        moves.Add(new Point(to.X + SettleOvershootPx, to.Y));
        moves.Add(to);
        return moves;
    }
}
