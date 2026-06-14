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

namespace Winsight.Core.Modules.VisualRegression;

public sealed record VisualDiffOptions(
    double SsimThreshold,
    double LocalSsimThreshold,
    double PixelDiffThreshold,
    double ColorThreshold,
    IReadOnlyList<Rectangle> IgnoreRegions,
    string? DiffMapPath = null)
{
    public const string SsimThresholdVariable = "WINSIGHT_VISUAL_SSIM_THRESHOLD";
    public const string LocalSsimThresholdVariable = "WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD";
    public const string PixelDiffThresholdVariable = "WINSIGHT_VISUAL_PIXEL_THRESHOLD";
    public const string ColorThresholdVariable = "WINSIGHT_VISUAL_COLOR_THRESHOLD";

    public const double DefaultSsimThreshold = 0.98;
    public const double DefaultLocalSsimThreshold = 0.85;
    public const double DefaultPixelDiffThreshold = 0.005;
    // pixelmatch matching threshold (0..1); larger = more tolerant of small color diffs.
    public const double DefaultColorThreshold = 0.1;

    public static VisualDiffOptions FromEnvironment(
        IReadOnlyList<Rectangle>? ignoreRegions = null,
        string? diffMapPath = null,
        double? ssimThreshold = null,
        double? localSsimThreshold = null,
        double? pixelDiffThreshold = null,
        double? colorThreshold = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        Func<string, string?> read = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        return new VisualDiffOptions(
            ssimThreshold ?? ReadDouble(read, SsimThresholdVariable, DefaultSsimThreshold),
            localSsimThreshold ?? ReadDouble(read, LocalSsimThresholdVariable, DefaultLocalSsimThreshold),
            pixelDiffThreshold ?? ReadDouble(read, PixelDiffThresholdVariable, DefaultPixelDiffThreshold),
            colorThreshold ?? ReadDouble(read, ColorThresholdVariable, DefaultColorThreshold),
            ignoreRegions ?? Array.Empty<Rectangle>(),
            diffMapPath);
    }

    public void Validate()
    {
        EnsureUnit(SsimThreshold, nameof(SsimThreshold));
        EnsureUnit(LocalSsimThreshold, nameof(LocalSsimThreshold));
        EnsureUnit(PixelDiffThreshold, nameof(PixelDiffThreshold));
        EnsureUnit(ColorThreshold, nameof(ColorThreshold));
    }

    private static void EnsureUnit(double value, string name)
    {
        if (value is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be between 0 and 1.");
        }
    }

    private static double ReadDouble(
        Func<string, string?> getEnvironmentVariable,
        string variable,
        double defaultValue)
    {
        string? raw = getEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new FormatException(
                $"{variable} must be a floating-point number using invariant culture.");
        }

        return value;
    }
}
