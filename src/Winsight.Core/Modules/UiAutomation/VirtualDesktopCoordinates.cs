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

using System.Runtime.InteropServices;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>Bounding rectangle of the Windows virtual desktop across all monitors.</summary>
public readonly record struct VirtualDesktopBounds(int Left, int Top, int Width, int Height);

/// <summary>Normalized 0..65535 absolute mouse coordinates for SendInput with MOUSEEVENTF_VIRTUALDESK.</summary>
public readonly record struct NormalizedAbsolutePoint(int Dx, int Dy);

/// <summary>
/// Maps physical screen coordinates onto the 0..65535 absolute space expected
/// by SendInput when MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK is set.
/// </summary>
public static class VirtualDesktopCoordinates
{
    public const int AbsoluteMax = 65535;

    private const int SystemMetricXVirtualScreen = 76;
    private const int SystemMetricYVirtualScreen = 77;
    private const int SystemMetricCxVirtualScreen = 78;
    private const int SystemMetricCyVirtualScreen = 79;

    /// <summary>Query the current Windows virtual desktop origin and dimensions.</summary>
    public static VirtualDesktopBounds QueryCurrent() =>
        new(
            NativeMethods.GetSystemMetrics(SystemMetricXVirtualScreen),
            NativeMethods.GetSystemMetrics(SystemMetricYVirtualScreen),
            NativeMethods.GetSystemMetrics(SystemMetricCxVirtualScreen),
            NativeMethods.GetSystemMetrics(SystemMetricCyVirtualScreen));

    /// <summary>
    /// Normalize a screen pixel coordinate to [0, 65535] over <paramref name="desktop"/>.
    /// Coordinates outside the virtual desktop are clamped to the [0, 65535] range.
    /// </summary>
    public static NormalizedAbsolutePoint Normalize(int screenX, int screenY, VirtualDesktopBounds desktop)
    {
        if (desktop.Width <= 0 || desktop.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desktop),
                $"Virtual desktop dimensions must be positive (got {desktop.Width}x{desktop.Height}).");
        }

        int dx = NormalizeAxis(screenX, desktop.Left, desktop.Width);
        int dy = NormalizeAxis(screenY, desktop.Top, desktop.Height);
        return new NormalizedAbsolutePoint(dx, dy);
    }

    private static int NormalizeAxis(int coordinate, int origin, int span)
    {
        if (span <= 1)
        {
            return 0;
        }

        double scaled = ((double)(coordinate - origin) * AbsoluteMax) / (span - 1);
        int rounded = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, 0, AbsoluteMax);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);
    }
}
