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

namespace Winsight.Core.Modules.VisualRegression;

[SupportedOSPlatform("windows")]
public sealed class VisualDiffer
{
    private const int GaussianRadius = 5;
    private const double GaussianSigma = 1.5;
    private const double LuminanceRange = 255.0;
    private const double C1 = 0.01 * LuminanceRange * 0.01 * LuminanceRange;
    private const double C2 = 0.03 * LuminanceRange * 0.03 * LuminanceRange;

    // Largest possible YIQ-weighted squared color delta for 8-bit channels (pixelmatch).
    private const double MaxYiqDelta = 35215.0;
    // Local SSIM is the worst NxN block mean; a small localized change collapses one block.
    private const int LocalBlockSize = 16;

    public VisualDiffResult Compare(
        string baselinePath,
        string candidatePath,
        VisualDiffOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselinePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (!File.Exists(baselinePath))
        {
            throw new FileNotFoundException("Baseline image does not exist.", baselinePath);
        }

        if (!File.Exists(candidatePath))
        {
            throw new FileNotFoundException("Candidate image does not exist.", candidatePath);
        }

        using Bitmap baseline = LoadArgbBitmap(baselinePath);
        using Bitmap candidate = LoadArgbBitmap(candidatePath);

        if (baseline.Width != candidate.Width || baseline.Height != candidate.Height)
        {
            WriteDimensionMismatchMapIfRequested(baseline, candidate, options.DiffMapPath);
            string mismatchReasoning =
                $"Image dimensions differ: baseline={baseline.Width}x{baseline.Height}, " +
                $"candidate={candidate.Width}x{candidate.Height}.";
            return new VisualDiffResult(
                Passed: false,
                Ssim: 0.0,
                LocalSsimMin: 0.0,
                PixelDiffRatio: 1.0,
                DiffMapPath: options.DiffMapPath,
                Reasoning: mismatchReasoning,
                Verdict: VisualDiffVerdicts.Fail,
                BaselinePath: baselinePath,
                CandidatePath: candidatePath);
        }

        int width = baseline.Width;
        int height = baseline.Height;
        int pixelCount = checked(width * height);
        byte[] baselineBytes = CopyBitmapBytes(baseline);
        byte[] candidateBytes = CopyBitmapBytes(candidate);
        bool[] ignored = BuildIgnoredMask(width, height, options.IgnoreRegions);

        var baselineLuma = new double[pixelCount];
        var candidateLuma = new double[pixelCount];
        var baselineLumaSquared = new double[pixelCount];
        var candidateLumaSquared = new double[pixelCount];
        var lumaProduct = new double[pixelCount];
        var mask = new double[pixelCount];

        long includedPixels = 0;
        var rgbDeltas = new byte[pixelCount];

        for (int i = 0; i < pixelCount; i++)
        {
            if (ignored[i])
            {
                continue;
            }

            int byteIndex = i * 4;
            byte baselineBlue = baselineBytes[byteIndex];
            byte baselineGreen = baselineBytes[byteIndex + 1];
            byte baselineRed = baselineBytes[byteIndex + 2];
            byte candidateBlue = candidateBytes[byteIndex];
            byte candidateGreen = candidateBytes[byteIndex + 1];
            byte candidateRed = candidateBytes[byteIndex + 2];

            rgbDeltas[i] = MaxDelta(
                baselineRed, baselineGreen, baselineBlue,
                candidateRed, candidateGreen, candidateBlue);

            double baselineGray = ToLuminance(baselineRed, baselineGreen, baselineBlue);
            double candidateGray = ToLuminance(candidateRed, candidateGreen, candidateBlue);

            baselineLuma[i] = baselineGray;
            candidateLuma[i] = candidateGray;
            baselineLumaSquared[i] = baselineGray * baselineGray;
            candidateLumaSquared[i] = candidateGray * candidateGray;
            lumaProduct[i] = baselineGray * candidateGray;
            mask[i] = 1.0;
            includedPixels++;
        }

        if (includedPixels == 0)
        {
            throw new ArgumentException(
                "IgnoreRegions exclude every pixel; at least one pixel must remain comparable.",
                nameof(options));
        }

        // Pixel difference: pixelmatch-style YIQ color distance with anti-aliased-edge
        // detection, so ClearType sub-pixel rendering on text is not counted as change.
        long divergentPixels = CountDivergentPixels(
            baselineBytes, candidateBytes, ignored, width, height, options.ColorThreshold);

        double[] ssimMap = ComputeSsimMap(
            baselineLuma,
            candidateLuma,
            baselineLumaSquared,
            candidateLumaSquared,
            lumaProduct,
            mask,
            width,
            height);

        double ssimTotal = 0.0;
        for (int i = 0; i < pixelCount; i++)
        {
            if (!ignored[i])
            {
                ssimTotal += ssimMap[i];
            }
        }

        double ssim = NormalizeUnitScore(ssimTotal / includedPixels);
        double localSsimMin = ComputeLocalSsimMin(ssimMap, ignored, width, height, ssim);
        double pixelDiffRatio = (double)divergentPixels / includedPixels;
        bool passed = ssim >= options.SsimThreshold
            && localSsimMin >= options.LocalSsimThreshold
            && pixelDiffRatio <= options.PixelDiffThreshold;

        if (!string.IsNullOrWhiteSpace(options.DiffMapPath))
        {
            WriteDiffMap(options.DiffMapPath, width, height, ignored, rgbDeltas, ssimMap);
        }

        string reasoning = FormattableString.Invariant(
            $"SSIM {ssim:F6} (threshold {options.SsimThreshold:F6}); local SSIM min {localSsimMin:F6} (threshold {options.LocalSsimThreshold:F6}); pixel diff ratio {pixelDiffRatio:F6} (threshold {options.PixelDiffThreshold:F6}).");

        return new VisualDiffResult(
            Passed: passed,
            Ssim: ssim,
            LocalSsimMin: localSsimMin,
            PixelDiffRatio: pixelDiffRatio,
            DiffMapPath: options.DiffMapPath,
            Reasoning: reasoning,
            Verdict: passed ? VisualDiffVerdicts.Pass : VisualDiffVerdicts.Fail,
            BaselinePath: baselinePath,
            CandidatePath: candidatePath);
    }

    // --- pixelmatch-style pixel difference -------------------------------------------

    private static long CountDivergentPixels(
        byte[] a, byte[] b, bool[] ignored, int width, int height, double colorThreshold)
    {
        double threshold = colorThreshold * colorThreshold * MaxYiqDelta;
        long divergent = 0;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int i = rowOffset + x;
                if (ignored[i])
                {
                    continue;
                }

                double delta = ColorDelta(a, b, i * 4, i * 4, yOnly: false);
                if (Math.Abs(delta) <= threshold)
                {
                    continue;
                }

                // Skip pixels that are anti-aliasing edges in either image (subpixel text).
                bool antialiased = Antialiased(a, b, x, y, width, height)
                    || Antialiased(b, a, x, y, width, height);
                if (!antialiased)
                {
                    divergent++;
                }
            }
        }
        return divergent;
    }

    private static double ColorDelta(byte[] img1, byte[] img2, int k, int m, bool yOnly)
    {
        double r1 = img1[k + 2], g1 = img1[k + 1], b1 = img1[k];
        double r2 = img2[m + 2], g2 = img2[m + 1], b2 = img2[m];

        double y1 = Rgb2Y(r1, g1, b1);
        double y2 = Rgb2Y(r2, g2, b2);
        double y = y1 - y2;
        if (yOnly)
        {
            return y;
        }

        double i = Rgb2I(r1, g1, b1) - Rgb2I(r2, g2, b2);
        double q = Rgb2Q(r1, g1, b1) - Rgb2Q(r2, g2, b2);
        double delta = (0.5053 * y * y) + (0.299 * i * i) + (0.1957 * q * q);
        return y1 > y2 ? -delta : delta;
    }

    private static bool Antialiased(byte[] img, byte[] other, int x1, int y1, int width, int height)
    {
        int x0 = Math.Max(x1 - 1, 0);
        int y0 = Math.Max(y1 - 1, 0);
        int x2 = Math.Min(x1 + 1, width - 1);
        int y2 = Math.Min(y1 + 1, height - 1);
        int pos = (y1 * width) + x1;
        int zeroes = (x1 == x0 || x1 == x2 || y1 == y0 || y1 == y2) ? 1 : 0;

        double min = 0.0;
        double max = 0.0;
        int minX = 0, minY = 0, maxX = 0, maxY = 0;

        for (int y = y0; y <= y2; y++)
        {
            for (int x = x0; x <= x2; x++)
            {
                if (x == x1 && y == y1)
                {
                    continue;
                }

                double delta = ColorDelta(img, img, pos * 4, ((y * width) + x) * 4, yOnly: true);
                if (delta == 0.0)
                {
                    zeroes++;
                    if (zeroes > 2)
                    {
                        return false;
                    }
                }
                else if (delta < min)
                {
                    min = delta;
                    minX = x;
                    minY = y;
                }
                else if (delta > max)
                {
                    max = delta;
                    maxX = x;
                    maxY = y;
                }
            }
        }

        if (min == 0.0 || max == 0.0)
        {
            return false;
        }

        return (HasManySiblings(img, minX, minY, width, height)
                && HasManySiblings(other, minX, minY, width, height))
            || (HasManySiblings(img, maxX, maxY, width, height)
                && HasManySiblings(other, maxX, maxY, width, height));
    }

    private static bool HasManySiblings(byte[] img, int x1, int y1, int width, int height)
    {
        int x0 = Math.Max(x1 - 1, 0);
        int y0 = Math.Max(y1 - 1, 0);
        int x2 = Math.Min(x1 + 1, width - 1);
        int y2 = Math.Min(y1 + 1, height - 1);
        int pos = ((y1 * width) + x1) * 4;
        int zeroes = (x1 == x0 || x1 == x2 || y1 == y0 || y1 == y2) ? 1 : 0;

        for (int y = y0; y <= y2; y++)
        {
            for (int x = x0; x <= x2; x++)
            {
                if (x == x1 && y == y1)
                {
                    continue;
                }

                int p = ((y * width) + x) * 4;
                if (img[pos] == img[p] && img[pos + 1] == img[p + 1]
                    && img[pos + 2] == img[p + 2] && img[pos + 3] == img[p + 3])
                {
                    zeroes++;
                    if (zeroes > 2)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static double Rgb2Y(double r, double g, double b) =>
        (r * 0.29889531) + (g * 0.58662247) + (b * 0.11448223);

    private static double Rgb2I(double r, double g, double b) =>
        (r * 0.59597799) - (g * 0.27417610) - (b * 0.32180189);

    private static double Rgb2Q(double r, double g, double b) =>
        (r * 0.21147017) - (g * 0.52261711) + (b * 0.31114694);

    // --- local SSIM ------------------------------------------------------------------

    private static double ComputeLocalSsimMin(
        double[] ssimMap, bool[] ignored, int width, int height, double globalSsim)
    {
        double min = double.MaxValue;
        bool any = false;
        for (int by = 0; by < height; by += LocalBlockSize)
        {
            for (int bx = 0; bx < width; bx += LocalBlockSize)
            {
                double sum = 0.0;
                int count = 0;
                int yEnd = Math.Min(by + LocalBlockSize, height);
                int xEnd = Math.Min(bx + LocalBlockSize, width);
                for (int y = by; y < yEnd; y++)
                {
                    int rowOffset = y * width;
                    for (int x = bx; x < xEnd; x++)
                    {
                        int i = rowOffset + x;
                        if (ignored[i])
                        {
                            continue;
                        }
                        sum += ssimMap[i];
                        count++;
                    }
                }

                // Only score blocks with enough comparable pixels to be meaningful.
                if (count >= (LocalBlockSize * LocalBlockSize) / 2)
                {
                    double blockMean = sum / count;
                    if (blockMean < min)
                    {
                        min = blockMean;
                    }
                    any = true;
                }
            }
        }

        return any ? NormalizeUnitScore(Math.Clamp(min, -1.0, 1.0)) : globalSsim;
    }

    // --- shared helpers (unchanged) --------------------------------------------------

    private static Bitmap LoadArgbBitmap(string path)
    {
        using var source = new Bitmap(path);
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(clone);
        graphics.DrawImageUnscaled(source, 0, 0);
        return clone;
    }

    private static byte[] CopyBitmapBytes(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(data.Stride) * bitmap.Height;
            var buffer = new byte[bytes];
            Marshal.Copy(data.Scan0, buffer, 0, bytes);
            if (data.Stride == bitmap.Width * 4)
            {
                return buffer;
            }

            return CopyTightlyPackedRows(buffer, data.Stride, bitmap.Width, bitmap.Height);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static byte[] CopyTightlyPackedRows(
        byte[] source,
        int stride,
        int width,
        int height)
    {
        int rowBytes = width * 4;
        var target = new byte[checked(rowBytes * height)];
        int absoluteStride = Math.Abs(stride);
        for (int y = 0; y < height; y++)
        {
            int sourceOffset = stride >= 0
                ? y * absoluteStride
                : (height - 1 - y) * absoluteStride;
            Buffer.BlockCopy(source, sourceOffset, target, y * rowBytes, rowBytes);
        }

        return target;
    }

    private static bool[] BuildIgnoredMask(
        int width,
        int height,
        IReadOnlyList<Rectangle> ignoreRegions)
    {
        var ignored = new bool[checked(width * height)];
        foreach (Rectangle region in ignoreRegions)
        {
            if (region.Width <= 0 || region.Height <= 0)
            {
                continue;
            }

            int left = Math.Clamp(region.Left, 0, width);
            int top = Math.Clamp(region.Top, 0, height);
            int right = Math.Clamp(region.Right, 0, width);
            int bottom = Math.Clamp(region.Bottom, 0, height);
            for (int y = top; y < bottom; y++)
            {
                int rowOffset = y * width;
                for (int x = left; x < right; x++)
                {
                    ignored[rowOffset + x] = true;
                }
            }
        }

        return ignored;
    }

    private static double[] ComputeSsimMap(
        double[] x,
        double[] y,
        double[] xSquared,
        double[] ySquared,
        double[] xy,
        double[] mask,
        int width,
        int height)
    {
        double[] kernel = BuildGaussianKernel();
        double[] denominator = GaussianBlur(mask, width, height, kernel);
        double[] meanX = GaussianBlurNormalized(x, denominator, width, height, kernel);
        double[] meanY = GaussianBlurNormalized(y, denominator, width, height, kernel);
        double[] meanXSquared = GaussianBlurNormalized(xSquared, denominator, width, height, kernel);
        double[] meanYSquared = GaussianBlurNormalized(ySquared, denominator, width, height, kernel);
        double[] meanXY = GaussianBlurNormalized(xy, denominator, width, height, kernel);
        var ssim = new double[x.Length];

        for (int i = 0; i < ssim.Length; i++)
        {
            if (denominator[i] <= 0.0)
            {
                ssim[i] = 1.0;
                continue;
            }

            double muX = meanX[i];
            double muY = meanY[i];
            double sigmaX = Math.Max(0.0, meanXSquared[i] - (muX * muX));
            double sigmaY = Math.Max(0.0, meanYSquared[i] - (muY * muY));
            double sigmaXY = meanXY[i] - (muX * muY);

            double numerator = ((2.0 * muX * muY) + C1) * ((2.0 * sigmaXY) + C2);
            double denominatorValue = ((muX * muX) + (muY * muY) + C1) * (sigmaX + sigmaY + C2);
            ssim[i] = denominatorValue == 0.0
                ? 1.0
                : Math.Clamp(numerator / denominatorValue, -1.0, 1.0);
        }

        return ssim;
    }

    private static double[] GaussianBlurNormalized(
        double[] input,
        double[] denominator,
        int width,
        int height,
        double[] kernel)
    {
        double[] blurred = GaussianBlur(input, width, height, kernel);
        for (int i = 0; i < blurred.Length; i++)
        {
            blurred[i] = denominator[i] <= 0.0 ? 0.0 : blurred[i] / denominator[i];
        }

        return blurred;
    }

    private static double[] GaussianBlur(
        double[] input,
        int width,
        int height,
        double[] kernel)
    {
        var temp = new double[input.Length];
        var output = new double[input.Length];

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                double sum = 0.0;
                for (int k = -GaussianRadius; k <= GaussianRadius; k++)
                {
                    int sampleX = x + k;
                    if (sampleX >= 0 && sampleX < width)
                    {
                        sum += input[rowOffset + sampleX] * kernel[k + GaussianRadius];
                    }
                }

                temp[rowOffset + x] = sum;
            }
        }

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                double sum = 0.0;
                for (int k = -GaussianRadius; k <= GaussianRadius; k++)
                {
                    int sampleY = y + k;
                    if (sampleY >= 0 && sampleY < height)
                    {
                        sum += temp[(sampleY * width) + x] * kernel[k + GaussianRadius];
                    }
                }

                output[rowOffset + x] = sum;
            }
        }

        return output;
    }

    private static double[] BuildGaussianKernel()
    {
        var kernel = new double[(GaussianRadius * 2) + 1];
        double sum = 0.0;
        for (int i = -GaussianRadius; i <= GaussianRadius; i++)
        {
            double value = Math.Exp(-(i * i) / (2.0 * GaussianSigma * GaussianSigma));
            kernel[i + GaussianRadius] = value;
            sum += value;
        }

        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] /= sum;
        }

        return kernel;
    }

    private static void WriteDiffMap(
        string diffMapPath,
        int width,
        int height,
        bool[] ignored,
        byte[] rgbDeltas,
        double[] ssimMap)
    {
        var directory = Path.GetDirectoryName(diffMapPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var diff = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        BitmapData data = diff.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(data.Stride) * height;
            var buffer = new byte[bytes];
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                int bufferOffset = y * data.Stride;
                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = rowOffset + x;
                    int byteIndex = bufferOffset + (x * 4);
                    if (ignored[pixelIndex])
                    {
                        buffer[byteIndex + 3] = 0;
                        continue;
                    }

                    double rgbScore = rgbDeltas[pixelIndex] / 255.0;
                    double ssimScore = Math.Clamp(1.0 - ssimMap[pixelIndex], 0.0, 1.0);
                    byte intensity = (byte)Math.Round(Math.Max(rgbScore, ssimScore) * 255.0);
                    buffer[byteIndex] = 0;
                    buffer[byteIndex + 1] = 0;
                    buffer[byteIndex + 2] = intensity;
                    buffer[byteIndex + 3] = 255;
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, bytes);
        }
        finally
        {
            diff.UnlockBits(data);
        }

        diff.Save(diffMapPath, ImageFormat.Png);
    }

    private static void WriteDimensionMismatchMapIfRequested(
        Bitmap baseline,
        Bitmap candidate,
        string? diffMapPath)
    {
        if (string.IsNullOrWhiteSpace(diffMapPath))
        {
            return;
        }

        int width = Math.Max(baseline.Width, candidate.Width);
        int height = Math.Max(baseline.Height, candidate.Height);
        var ignored = new bool[checked(width * height)];
        var rgbDeltas = new byte[ignored.Length];
        Array.Fill<byte>(rgbDeltas, 255);
        var ssimMap = new double[ignored.Length];
        WriteDiffMap(diffMapPath, width, height, ignored, rgbDeltas, ssimMap);
    }

    private static double ToLuminance(byte red, byte green, byte blue) =>
        (0.299 * red) + (0.587 * green) + (0.114 * blue);

    private static double NormalizeUnitScore(double value)
    {
        if (value > 1.0 - 1e-12)
        {
            return 1.0;
        }

        if (value < 1e-12 && value > 0.0)
        {
            return 0.0;
        }

        return value;
    }

    private static byte MaxDelta(
        byte baselineRed,
        byte baselineGreen,
        byte baselineBlue,
        byte candidateRed,
        byte candidateGreen,
        byte candidateBlue)
    {
        int red = Math.Abs(baselineRed - candidateRed);
        int green = Math.Abs(baselineGreen - candidateGreen);
        int blue = Math.Abs(baselineBlue - candidateBlue);
        return (byte)Math.Max(red, Math.Max(green, blue));
    }
}
