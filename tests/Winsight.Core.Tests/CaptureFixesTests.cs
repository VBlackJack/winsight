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
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Ocr;
using Xunit;

namespace Winsight.Core.Tests;

/// <summary>
/// Screen-capture blit geometry (resize-border mask), OCR same-script language
/// fallback, uniform-capture detection, and the wire shape of the warning fields.
/// </summary>
public sealed class CaptureFixesTests
{
    private const int InsetPx = 7;
    private const int WindowLeft = 100;
    private const int WindowTop = 50;
    private const int WindowWidth = 800;
    private const int WindowHeight = 600;
    private const int BitmapSize = 64;
    private const int BlockSize = 12;

    private static readonly WindowBounds Window = new(WindowLeft, WindowTop, WindowWidth, WindowHeight);

    private static readonly IReadOnlyList<(string Tag, string Script)> Installed =
    [
        ("fr-FR", "Latn"),
        ("en-GB", "Latn"),
        ("ja-JP", "Jpan"),
    ];

    // ---- blit geometry ----

    [Fact]
    public void Blit_uses_the_dwm_frame_when_contained()
    {
        // Typical Win10/11 frame: 7 px invisible border left, right and bottom, none on top.
        WindowBounds frame = new(WindowLeft + InsetPx, WindowTop, WindowWidth - 2 * InsetPx, WindowHeight - InsetPx);

        ScreenBlitRect blit = ScreenBlitGeometry.ComputeBlitRect(Window, frame);

        blit.Should().Be(new ScreenBlitRect(
            SourceLeft: WindowLeft + InsetPx,
            SourceTop: WindowTop,
            DestX: InsetPx,
            DestY: 0,
            Width: WindowWidth - 2 * InsetPx,
            Height: WindowHeight - InsetPx));
    }

    [Fact]
    public void Blit_falls_back_to_the_window_rect_without_a_frame()
    {
        ScreenBlitGeometry.ComputeBlitRect(Window, null)
            .Should().Be(new ScreenBlitRect(WindowLeft, WindowTop, 0, 0, WindowWidth, WindowHeight));
    }

    [Fact]
    public void Blit_falls_back_to_the_window_rect_for_an_empty_frame()
    {
        ScreenBlitGeometry.ComputeBlitRect(Window, new WindowBounds(WindowLeft, WindowTop, 0, 0))
            .Should().Be(new ScreenBlitRect(WindowLeft, WindowTop, 0, 0, WindowWidth, WindowHeight));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 0, 1)]
    public void Blit_falls_back_to_the_window_rect_for_a_frame_not_contained(
        int leftDelta, int topDelta, int widthDelta, int heightDelta)
    {
        WindowBounds frame = new(
            WindowLeft + leftDelta, WindowTop + topDelta, WindowWidth + widthDelta, WindowHeight + heightDelta);

        ScreenBlitGeometry.ComputeBlitRect(Window, frame)
            .Should().Be(new ScreenBlitRect(WindowLeft, WindowTop, 0, 0, WindowWidth, WindowHeight));
    }

    // ---- OCR language selection ----

    [Fact]
    public void Ocr_selector_prefers_the_exact_tag()
    {
        OcrLanguageSelector.Select("fr-FR", "Latn", Installed, "en-GB").Should().Be("fr-FR");
    }

    [Fact]
    public void Ocr_selector_prefers_the_same_primary_language_other_region()
    {
        OcrLanguageSelector.Select("en-US", "Latn", Installed, "fr-FR").Should().Be("en-GB");
    }

    [Fact]
    public void Ocr_selector_prefers_the_user_profile_recognizer_of_the_same_script()
    {
        IReadOnlyList<(string Tag, string Script)> installed = [("de-DE", "Latn"), ("fr-FR", "Latn")];

        OcrLanguageSelector.Select("es-ES", "Latn", installed, "fr-FR").Should().Be("fr-FR");
    }

    [Fact]
    public void Ocr_selector_ignores_a_user_profile_recognizer_of_another_script()
    {
        IReadOnlyList<(string Tag, string Script)> installed = [("de-DE", "Latn"), ("ja-JP", "Jpan")];

        OcrLanguageSelector.Select("es-ES", "Latn", installed, "ja-JP").Should().Be("de-DE");
    }

    [Fact]
    public void Ocr_selector_takes_the_first_installed_recognizer_of_the_script()
    {
        OcrLanguageSelector.Select("de-DE", "Latn", Installed, null).Should().Be("fr-FR");
    }

    [Fact]
    public void Ocr_selector_returns_none_for_a_script_with_no_recognizer()
    {
        OcrLanguageSelector.Select("ru-RU", "Cyrl", Installed, "fr-FR").Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Ocr_selector_returns_none_for_an_empty_script(string? script)
    {
        OcrLanguageSelector.Select("xx-XX", script, Installed, "fr-FR").Should().BeNull();
    }

    // ---- uniformity ----

    private static Bitmap Filled(Color color)
    {
        Bitmap bitmap = new(BitmapSize, BitmapSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }

    private static Rectangle Whole => new(0, 0, BitmapSize, BitmapSize);

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    public void Single_color_bitmaps_are_uniform(int red, int green, int blue)
    {
        using Bitmap bitmap = Filled(Color.FromArgb(red, green, blue));

        CaptureWarnings.IsUniform(bitmap, Whole).Should().BeTrue();
        CaptureWarnings.EvaluateUniform(bitmap, Whole)
            .Should().StartWith(CaptureWarnings.RegionUniformCode + ": ");
    }

    [Fact]
    public void A_distinct_block_makes_the_bitmap_not_uniform()
    {
        using Bitmap bitmap = Filled(Color.Black);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.FillRectangle(Brushes.White, 8, 8, BlockSize, BlockSize);
        }

        CaptureWarnings.IsUniform(bitmap, Whole).Should().BeFalse();
        CaptureWarnings.EvaluateUniform(bitmap, Whole).Should().BeNull();
    }

    [Fact]
    public void Tolerance_boundary_is_inclusive()
    {
        int baseValue = 100;
        using Bitmap within = Filled(Color.FromArgb(baseValue, baseValue, baseValue));
        within.SetPixel(0, 0, Color.FromArgb(
            baseValue + CaptureWarnings.UniformChannelTolerance, baseValue, baseValue));
        using Bitmap beyond = Filled(Color.FromArgb(baseValue, baseValue, baseValue));
        beyond.SetPixel(0, 0, Color.FromArgb(
            baseValue + CaptureWarnings.UniformChannelTolerance + 1, baseValue, baseValue));
        // The grid samples (0, 0) first, which is the reference; these differ on a later sample.
        using Bitmap withinSecond = Filled(Color.FromArgb(baseValue, baseValue, baseValue));
        withinSecond.SetPixel(CaptureWarnings.UniformSampleStep, 0, Color.FromArgb(
            baseValue + CaptureWarnings.UniformChannelTolerance, baseValue, baseValue));
        using Bitmap beyondSecond = Filled(Color.FromArgb(baseValue, baseValue, baseValue));
        beyondSecond.SetPixel(CaptureWarnings.UniformSampleStep, 0, Color.FromArgb(
            baseValue + CaptureWarnings.UniformChannelTolerance + 1, baseValue, baseValue));

        CaptureWarnings.IsUniform(within, Whole).Should().BeTrue();
        CaptureWarnings.IsUniform(withinSecond, Whole).Should().BeTrue();
        CaptureWarnings.IsUniform(beyondSecond, Whole).Should().BeFalse();
        CaptureWarnings.IsUniform(beyond, Whole).Should().BeFalse();
    }

    [Fact]
    public void Only_the_evaluated_area_counts_so_a_transparent_mask_strip_is_ignored()
    {
        using Bitmap bitmap = new(BitmapSize, BitmapSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        Rectangle blitted = new(InsetPx, 0, BitmapSize - 2 * InsetPx, BitmapSize - InsetPx);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.FillRectangle(Brushes.White, blitted);
        }

        CaptureWarnings.IsUniform(bitmap, blitted).Should().BeTrue();
        CaptureWarnings.IsUniform(bitmap, Whole).Should().BeFalse();
    }

    [Fact]
    public void Join_skips_empty_warnings()
    {
        CaptureWarnings.Join(null, "a", "", "b").Should().Be("a" + CaptureWarnings.WarningSeparator + "b");
        CaptureWarnings.Join(null, "").Should().BeNull();
    }

    // ---- wire shape ----

    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Screenshot_result_omits_a_null_warning_and_emits_a_set_one()
    {
        ScreenshotResult without = new("a.png", 1, 1, DateTime.UnixEpoch);
        ScreenshotResult with = without with { Warning = CaptureWarnings.RegionUniformWarning };

        JsonSerializer.Serialize(without, WireOptions).Should().NotContain("warning");
        JsonSerializer.Serialize(with, WireOptions).Should().Contain("\"warning\":\"region_uniform: ");
    }

    [Fact]
    public void Visual_text_result_omits_null_language_fallback_and_warning_fields()
    {
        VisualTextResult plain = new("en-US", "", [], new WindowRegion(0, 0, 1, 1), 2);
        VisualTextResult fallback = plain with { RequestedLanguage = "en-US", Warning = "ocr_language_fallback: x" };

        string plainJson = JsonSerializer.Serialize(plain, WireOptions);
        plainJson.Should().NotContain("requestedLanguage").And.NotContain("warning");
        string fallbackJson = JsonSerializer.Serialize(fallback, WireOptions);
        fallbackJson.Should().Contain("\"requestedLanguage\":\"en-US\"").And.Contain("\"warning\":\"ocr_language_fallback: ");
    }
}
