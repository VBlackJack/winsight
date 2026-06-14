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
using System.Runtime.Versioning;
using FluentAssertions;
using Winsight.Core.Modules.VisualRegression;
using Xunit;

namespace Winsight.Core.Tests;

[SupportedOSPlatform("windows")]
public class VisualDifferTests
{
    // ctor order: ssim, localSsim, pixel, color, ignoreRegions, diffMapPath?
    private static VisualDiffOptions Options(
        double ssim, double localSsim, double pixel, double color,
        IReadOnlyList<Rectangle>? ignore = null, string? diffMap = null) =>
        new(ssim, localSsim, pixel, color, ignore ?? Array.Empty<Rectangle>(), diffMap);

    [Fact]
    public void Identical_images_pass_with_perfect_scores()
    {
        using TempImages temp = new();
        string baseline = temp.WriteSolid("baseline.png", 16, 16, Color.FromArgb(12, 34, 56));
        string candidate = temp.WriteSolid("candidate.png", 16, 16, Color.FromArgb(12, 34, 56));

        VisualDiffResult result = new VisualDiffer().Compare(
            baseline, candidate, Options(1.0, 1.0, 0.0, 0.1));

        result.Passed.Should().BeTrue();
        result.Verdict.Should().Be(VisualDiffVerdicts.Pass);
        result.Ssim.Should().BeApproximately(1.0, 1e-12);
        result.LocalSsimMin.Should().BeApproximately(1.0, 1e-12);
        result.PixelDiffRatio.Should().Be(0.0);
    }

    [Fact]
    public void Pixel_diff_ratio_counts_changed_unmasked_pixels()
    {
        using TempImages temp = new();
        string baseline = temp.WriteSolid("baseline.png", 10, 10, Color.Black);
        string candidate = temp.WriteSolid(
            "candidate.png", 10, 10, Color.Black,
            bitmap => bitmap.SetPixel(0, 0, Color.White));

        VisualDiffResult result = new VisualDiffer().Compare(
            baseline, candidate, Options(0.0, 0.0, 0.005, 0.0));

        result.Passed.Should().BeFalse();
        result.PixelDiffRatio.Should().BeApproximately(0.01, 1e-12);
        result.Reasoning.Should().Contain("pixel diff ratio");
    }

    [Fact]
    public void Ignore_regions_exclude_differences_from_both_metrics()
    {
        using TempImages temp = new();
        string baseline = temp.WriteSolid("baseline.png", 10, 10, Color.Black);
        string candidate = temp.WriteSolid(
            "candidate.png", 10, 10, Color.Black,
            bitmap => bitmap.SetPixel(0, 0, Color.White));

        VisualDiffResult result = new VisualDiffer().Compare(
            baseline, candidate, Options(1.0, 1.0, 0.0, 0.1, new[] { new Rectangle(0, 0, 1, 1) }));

        result.Passed.Should().BeTrue();
        result.Ssim.Should().BeApproximately(1.0, 1e-12);
        result.PixelDiffRatio.Should().Be(0.0);
    }

    [Fact]
    public void Color_threshold_ignores_small_channel_deltas_for_pixel_ratio()
    {
        using TempImages temp = new();
        string baseline = temp.WriteSolid("baseline.png", 10, 10, Color.Black);
        string candidate = temp.WriteSolid(
            "candidate.png", 10, 10, Color.Black,
            bitmap => bitmap.SetPixel(0, 0, Color.FromArgb(5, 5, 5)));

        VisualDiffResult result = new VisualDiffer().Compare(
            baseline, candidate, Options(0.0, 0.0, 0.0, 0.1));

        result.Passed.Should().BeTrue();
        result.PixelDiffRatio.Should().Be(0.0);
    }

    [Fact]
    public void Local_ssim_min_catches_a_small_localized_change_global_mean_misses()
    {
        using TempImages temp = new();
        string baseline = temp.WriteSolid("baseline.png", 64, 64, Color.FromArgb(40, 80, 120));
        string candidate = temp.WriteSolid(
            "candidate.png", 64, 64, Color.FromArgb(40, 80, 120),
            bitmap =>
            {
                for (int y = 8; y < 24; y++)
                {
                    for (int x = 8; x < 24; x++)
                    {
                        bitmap.SetPixel(x, y, Color.White);
                    }
                }
            });

        VisualDiffResult result = new VisualDiffer().Compare(
            baseline, candidate, Options(0.5, 0.9, 1.0, 0.1));

        // Global mean stays high, but the worst 16x16 block collapses.
        result.LocalSsimMin.Should().BeLessThan(result.Ssim);
        result.LocalSsimMin.Should().BeLessThan(0.9);
        result.Passed.Should().BeFalse("the local SSIM criterion must catch the localized change");
        result.Reasoning.Should().Contain("local SSIM min");
    }

    [Fact]
    public void Dimension_mismatch_fails_explicitly_and_writes_diff_map_when_requested()
    {
        using TempImages temp = new();
        string baseline = temp.WriteSolid("baseline.png", 10, 10, Color.Black);
        string candidate = temp.WriteSolid("candidate.png", 12, 10, Color.Black);
        string diffMap = Path.Combine(temp.DirectoryPath, "diff.png");

        VisualDiffResult result = new VisualDiffer().Compare(
            baseline, candidate, Options(0.98, 0.85, 0.005, 0.1, null, diffMap));

        result.Passed.Should().BeFalse();
        result.Ssim.Should().Be(0.0);
        result.PixelDiffRatio.Should().Be(1.0);
        result.DiffMapPath.Should().Be(diffMap);
        result.Reasoning.Should().Contain("dimensions differ");
        File.Exists(diffMap).Should().BeTrue();
    }

    [Fact]
    public void Diff_map_is_written_for_same_sized_images_when_path_is_provided()
    {
        using TempImages temp = new();
        string baseline = temp.WriteSolid("baseline.png", 10, 10, Color.Black);
        string candidate = temp.WriteSolid(
            "candidate.png", 10, 10, Color.Black,
            bitmap => bitmap.SetPixel(0, 0, Color.White));
        string diffMap = Path.Combine(temp.DirectoryPath, "diff.png");

        VisualDiffResult result = new VisualDiffer().Compare(
            baseline, candidate, Options(0.0, 0.0, 1.0, 0.0, null, diffMap));

        result.DiffMapPath.Should().Be(diffMap);
        File.Exists(diffMap).Should().BeTrue();
    }

    private sealed class TempImages : IDisposable
    {
        public TempImages()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "winsight-visual-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public string WriteSolid(
            string fileName,
            int width,
            int height,
            Color color,
            Action<Bitmap>? mutate = null)
        {
            string path = Path.Combine(DirectoryPath, fileName);
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(color);
            }

            mutate?.Invoke(bitmap);
            bitmap.Save(path, ImageFormat.Png);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
