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

using Winsight.Core.Models;

namespace Winsight.Core.Modules.Capture;

/// <summary>
/// The sub-rectangle of a window that a screen-DC copy transfers: where it
/// starts on the desktop, where it lands in the window-sized bitmap, and its size.
/// </summary>
public readonly record struct ScreenBlitRect(
    int SourceLeft,
    int SourceTop,
    int DestX,
    int DestY,
    int Width,
    int Height);

/// <summary>
/// Pure geometry for <see cref="CaptureSource.Screen"/>. The window rectangle
/// includes the invisible resize border of Windows 10/11, which shows pixels of
/// whatever lies behind it; only the visible frame (DWM extended frame bounds)
/// is copied, to the matching offset of a bitmap that keeps the window size, so
/// an image pixel stays equal to a window-relative coordinate.
/// </summary>
public static class ScreenBlitGeometry
{
    /// <summary>
    /// The rectangle to copy: the DWM frame when it is non-empty and fully inside
    /// the window rectangle, otherwise the whole window rectangle (DWM failure, or
    /// a DPI-unaware process whose two rectangles are in different units).
    /// </summary>
    public static ScreenBlitRect ComputeBlitRect(WindowBounds window, WindowBounds? dwmFrame)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (dwmFrame is { Width: > 0, Height: > 0 } frame && IsContained(frame, window))
        {
            return new ScreenBlitRect(
                frame.Left,
                frame.Top,
                frame.Left - window.Left,
                frame.Top - window.Top,
                frame.Width,
                frame.Height);
        }

        return new ScreenBlitRect(window.Left, window.Top, 0, 0, window.Width, window.Height);
    }

    private static bool IsContained(WindowBounds inner, WindowBounds outer) =>
        inner.Left >= outer.Left &&
        inner.Top >= outer.Top &&
        (long)inner.Left + inner.Width <= (long)outer.Left + outer.Width &&
        (long)inner.Top + inner.Height <= (long)outer.Top + outer.Height;
}
