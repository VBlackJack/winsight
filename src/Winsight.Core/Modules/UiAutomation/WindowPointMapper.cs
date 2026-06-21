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

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Pure coordinate math for translating window-relative points into absolute
/// screen coordinates. A window-relative point uses the same origin as a
/// full-window screenshot (the window's top-left corner), so a point produced
/// by <c>find_element_visually</c> maps to the screen by adding the window
/// origin reported by <see cref="WindowGeometry"/>.
/// </summary>
public static class WindowPointMapper
{
    /// <summary>
    /// Translate a window-relative point into absolute screen coordinates.
    /// </summary>
    public static (int ScreenX, int ScreenY) ToScreen(int windowLeft, int windowTop, int x, int y)
        => (windowLeft + x, windowTop + y);

    /// <summary>
    /// Whether a window-relative point falls within a window of the given size.
    /// The right and bottom edges are exclusive, matching pixel addressing.
    /// </summary>
    public static bool Contains(int width, int height, int x, int y)
        => x >= 0 && y >= 0 && x < width && y < height;
}
