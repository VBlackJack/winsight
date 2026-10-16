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

using System.Globalization;

namespace Winsight.Core.Models;

/// <summary>
/// A rectangle in window coordinates: the same origin as a full-window
/// screenshot and as window-relative <c>click_point</c> coordinates.
/// </summary>
public sealed record WindowRegion(int X, int Y, int Width, int Height)
{
    /// <summary>The whole window, or why the region does not fit inside it.</summary>
    public static string? Validate(WindowRegion? region, int windowWidth, int windowHeight) =>
        region is null ||
        (region.X >= 0 && region.Y >= 0 && region.Width > 0 && region.Height > 0 &&
         region.X + region.Width <= windowWidth && region.Y + region.Height <= windowHeight)
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"region ({region.X}, {region.Y}, {region.Width}x{region.Height}) must lie inside the " +
                $"{windowWidth}x{windowHeight} window, with a positive size.");
}
