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
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Winsight.Core.Modules.Capture;

/// <summary>Warning codes and texts attached to captures, and the uniform-frame check.</summary>
[SupportedOSPlatform("windows")]
public static class CaptureWarnings
{
    public const string RegionUniformCode = "region_uniform";

    /// <summary>Distance, in pixels, between two sampled columns and rows.</summary>
    public const int UniformSampleStep = 4;

    /// <summary>Largest per-channel difference from the first sample still counted as the same color.</summary>
    public const int UniformChannelTolerance = 2;

    /// <summary>Joins several warnings into the single Warning string.</summary>
    public const string WarningSeparator = " | ";

    private const int BytesPerPixel = 4;

    public static readonly string RegionUniformWarning =
        $"{RegionUniformCode}: the captured area is a single color, so nothing is drawn in it. " +
        "Check the application itself (for example a stale or disconnected session, or a surface " +
        "that has not painted yet) before concluding nothing is displayed.";

    /// <summary>The uniform warning when the area of the bitmap is one color, otherwise null.</summary>
    public static string? EvaluateUniform(Bitmap bitmap, Rectangle area) =>
        IsUniform(bitmap, area) ? RegionUniformWarning : null;

    /// <summary>Joins the non-empty warnings, or returns null when there is none.</summary>
    public static string? Join(params string?[] warnings)
    {
        string[] present = warnings.Where(w => !string.IsNullOrEmpty(w)).Select(w => w!).ToArray();
        return present.Length == 0 ? null : string.Join(WarningSeparator, present);
    }

    /// <summary>
    /// True when every sampled pixel of <paramref name="area"/> is within
    /// <see cref="UniformChannelTolerance"/> per color channel of the first one.
    /// Samples a grid, so the cost stays low on large frames; alpha is ignored.
    /// </summary>
    public static bool IsUniform(Bitmap bitmap, Rectangle area)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        area.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        if (area.Width <= 0 || area.Height <= 0)
        {
            return false;
        }

        BitmapData data = bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] row = new byte[area.Width * BytesPerPixel];
            int firstBlue = 0, firstGreen = 0, firstRed = 0;
            bool haveFirst = false;
            for (int y = 0; y < area.Height; y += UniformSampleStep)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < area.Width; x += UniformSampleStep)
                {
                    int offset = x * BytesPerPixel;
                    int blue = row[offset];
                    int green = row[offset + 1];
                    int red = row[offset + 2];
                    if (!haveFirst)
                    {
                        (firstBlue, firstGreen, firstRed, haveFirst) = (blue, green, red, true);
                        continue;
                    }
                    if (Math.Abs(blue - firstBlue) > UniformChannelTolerance ||
                        Math.Abs(green - firstGreen) > UniformChannelTolerance ||
                        Math.Abs(red - firstRed) > UniformChannelTolerance)
                    {
                        return false;
                    }
                }
            }
            return haveFirst;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
