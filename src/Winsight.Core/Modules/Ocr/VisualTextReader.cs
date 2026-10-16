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
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;

namespace Winsight.Core.Modules.Ocr;

/// <summary>
/// Reads the text a window draws, for UIs that expose none through UI
/// Automation (iced, winit, egui, games). Uses the OCR engine built into
/// Windows: offline, no model to install, but only for languages whose OCR
/// component is present (Settings, Time and language). The capture is enlarged
/// before recognition, because interface text is smaller than the engine
/// expects; rectangles are mapped back to window coordinates.
/// </summary>
public sealed class VisualTextReader
{
    public const int DefaultScale = 2;
    public const int MaxScale = 4;
    private const string NoEngineCode = "ocr_unavailable";

    private readonly WindowCapture _capture;

    public VisualTextReader(WindowCapture capture)
    {
        _capture = capture;
    }

    /// <summary>The BCP-47 tags the installed OCR components recognize.</summary>
    public static IReadOnlyList<string> AvailableLanguages() =>
        OcrEngine.AvailableRecognizerLanguages.Select(language => language.LanguageTag).ToList();

    public OperationResult Read(
        int processId,
        string? windowTitle = null,
        WindowRegion? region = null,
        string? language = null,
        int scale = DefaultScale)
    {
        if (scale is < 1 or > MaxScale)
        {
            return OperationResult.Failure(string.Create(
                CultureInfo.InvariantCulture, $"scale must be between 1 and {MaxScale} (got {scale})."));
        }
        if (CreateEngine(language, out string? engineError) is not { } engine)
        {
            return OperationResult.Failure(engineError!);
        }
        using CapturedFrame frame = _capture.CaptureFrame(processId, windowTitle, region);
        int effectiveScale = FitScale(frame.Bitmap.Width, frame.Bitmap.Height, scale, (int)OcrEngine.MaxImageDimension);
        using SoftwareBitmap image = ToSoftwareBitmap(frame.Bitmap, effectiveScale);
        OcrResult recognized = engine.RecognizeAsync(image).AsTask().GetAwaiter().GetResult();

        List<VisualTextLine> lines = recognized.Lines
            .Select(line =>
            {
                List<VisualTextWord> words = line.Words
                    .Select(word => new VisualTextWord(
                        word.Text,
                        OcrGeometry.ToWindow(
                            word.BoundingRect.X, word.BoundingRect.Y,
                            word.BoundingRect.Width, word.BoundingRect.Height,
                            effectiveScale, frame.Region)))
                    .ToList();
                return new VisualTextLine(line.Text, OcrGeometry.Union(words.Select(w => w.Bounds)), words);
            })
            .ToList();
        return OperationResult.OkWith(new VisualTextResult(
            engine.RecognizerLanguage.LanguageTag,
            string.Join('\n', lines.Select(line => line.Text)),
            lines,
            frame.Region,
            effectiveScale));
    }

    private static OcrEngine? CreateEngine(string? language, out string? error)
    {
        OcrEngine? engine = string.IsNullOrWhiteSpace(language)
            ? OcrEngine.TryCreateFromUserProfileLanguages()
            : OcrEngine.IsLanguageSupported(new Windows.Globalization.Language(language))
                ? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(language))
                : null;
        error = engine is null
            ? $"{NoEngineCode}: no Windows OCR component for " +
              $"{(string.IsNullOrWhiteSpace(language) ? "the user profile languages" : $"\"{language}\"")}. " +
              $"Installed: {string.Join(", ", AvailableLanguages())}. Add one under Settings, Time and language."
            : null;
        return engine;
    }

    // Enlarging past the engine's maximum dimension would make recognition fail.
    internal static int FitScale(int width, int height, int requested, int maxDimension)
    {
        int largest = Math.Max(width, height);
        int fitting = Math.Max(1, maxDimension / Math.Max(1, largest));
        return Math.Max(1, Math.Min(requested, fitting));
    }

    private static SoftwareBitmap ToSoftwareBitmap(Bitmap source, int scale)
    {
        using Bitmap scaled = new(source.Width * scale, source.Height * scale, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, 0, 0, scaled.Width, scaled.Height);
        }
        Rectangle all = new(0, 0, scaled.Width, scaled.Height);
        BitmapData data = scaled.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] pixels = new byte[scaled.Width * scaled.Height * 4];
            for (int row = 0; row < scaled.Height; row++)
            {
                Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * scaled.Width * 4, scaled.Width * 4);
            }
            return SoftwareBitmap.CreateCopyFromBuffer(
                pixels.AsBuffer(), BitmapPixelFormat.Bgra8, scaled.Width, scaled.Height, BitmapAlphaMode.Premultiplied);
        }
        finally
        {
            scaled.UnlockBits(data);
        }
    }
}

/// <summary>Maps OCR rectangles from the enlarged capture back to window coordinates.</summary>
internal static class OcrGeometry
{
    public static WindowRegion ToWindow(double x, double y, double width, double height, int scale, WindowRegion region)
    {
        int left = (int)Math.Floor(x / scale) + region.X;
        int top = (int)Math.Floor(y / scale) + region.Y;
        int right = (int)Math.Ceiling((x + width) / scale) + region.X;
        int bottom = (int)Math.Ceiling((y + height) / scale) + region.Y;
        return new WindowRegion(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    public static WindowRegion Union(IEnumerable<WindowRegion> parts)
    {
        List<WindowRegion> all = parts.ToList();
        if (all.Count == 0)
        {
            return new WindowRegion(0, 0, 0, 0);
        }
        int left = all.Min(part => part.X);
        int top = all.Min(part => part.Y);
        int right = all.Max(part => part.X + part.Width);
        int bottom = all.Max(part => part.Y + part.Height);
        return new WindowRegion(left, top, right - left, bottom - top);
    }
}
