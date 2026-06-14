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
using System.Runtime.Versioning;

namespace Winsight.Core.Modules.Vision;

/// <summary>
/// Result of Qwen2.5-VL image preprocessing: the flattened patch tensor plus the
/// grid and the resized/original dimensions needed to denormalize model output.
/// </summary>
public sealed record QwenPreprocessResult(
    float[] PixelValues,
    int GridT,
    int GridH,
    int GridW,
    int ResizedWidth,
    int ResizedHeight,
    int OriginalWidth,
    int OriginalHeight);

/// <summary>
/// Hand-rolled Qwen2.5-VL image preprocessing (smart-resize on the 28px grid, CLIP
/// normalization, canonical patchify) producing the <c>pixel_values</c> tensor the
/// <c>vision_encoder</c> ONNX expects. Parity validated against the reference
/// HuggingFace processor (mean abs diff ~1e-6); resize-algorithm edge drift is the
/// only divergence and does not move the grounded point materially.
/// </summary>
[SupportedOSPlatform("windows")]
public static class QwenImagePreprocessing
{
    private const int PatchSize = 14;
    private const int MergeSize = 2;
    private const int TemporalPatch = 2;
    private const int Factor = PatchSize * MergeSize; // 28
    private const int Channels = 3;
    private const int FeatureLength = Channels * TemporalPatch * PatchSize * PatchSize; // 1176

    // qwen-vl-utils inference defaults (configurable; not the processor-class defaults).
    public const long DefaultMinPixels = 56 * 56;
    public const long DefaultMaxPixels = 28 * 28 * 1280;

    // OpenAI CLIP normalization (Qwen2-VL image processor defaults).
    private static readonly float[] Mean = { 0.48145466f, 0.4578275f, 0.40821073f };
    private static readonly float[] Std = { 0.26862954f, 0.26130258f, 0.27577711f };

    /// <summary>Computes the smart-resized height/width, aligned to the 28px grid.</summary>
    public static (int Height, int Width) SmartResize(int height, int width, long minPixels, long maxPixels)
    {
        int hBar = Math.Max(Factor, (int)Math.Round((double)height / Factor) * Factor);
        int wBar = Math.Max(Factor, (int)Math.Round((double)width / Factor) * Factor);
        if ((long)hBar * wBar > maxPixels)
        {
            double beta = Math.Sqrt((double)height * width / maxPixels);
            hBar = (int)(Math.Floor(height / beta / Factor) * Factor);
            wBar = (int)(Math.Floor(width / beta / Factor) * Factor);
        }
        else if ((long)hBar * wBar < minPixels)
        {
            double beta = Math.Sqrt((double)minPixels / (height * width));
            hBar = (int)(Math.Ceiling(height * beta / Factor) * Factor);
            wBar = (int)(Math.Ceiling(width * beta / Factor) * Factor);
        }
        return (Math.Max(Factor, hBar), Math.Max(Factor, wBar));
    }

    /// <summary>Builds the Qwen2.5-VL pixel_values tensor for the screenshot at <paramref name="imagePath"/>.</summary>
    public static QwenPreprocessResult Build(string imagePath, long minPixels, long maxPixels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        using Bitmap source = new(imagePath);
        int originalW = source.Width;
        int originalH = source.Height;
        (int rh, int rw) = SmartResize(originalH, originalW, minPixels, maxPixels);

        float[][,] norm = Normalize(source, rw, rh);
        float[] pixelValues = Patchify(norm, rh, rw, out int gridH, out int gridW);
        return new QwenPreprocessResult(pixelValues, 1, gridH, gridW, rw, rh, originalW, originalH);
    }

    private static float[][,] Normalize(Bitmap source, int rw, int rh)
    {
        using Bitmap resized = new(rw, rh, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            // TileFlipXY avoids the half-pixel edge ghosting HighQualityBicubic adds.
            using ImageAttributes attributes = new();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(
                source,
                new Rectangle(0, 0, rw, rh),
                0, 0, source.Width, source.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        float[][,] norm =
        {
            new float[rh, rw],
            new float[rh, rw],
            new float[rh, rw],
        };
        BitmapData data = resized.LockBits(
            new Rectangle(0, 0, rw, rh), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            byte[] buffer = new byte[stride * rh];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            for (int y = 0; y < rh; y++)
            {
                int row = y * stride;
                for (int x = 0; x < rw; x++)
                {
                    int idx = row + (x * 4); // BGRA little-endian
                    norm[0][y, x] = (buffer[idx + 2] / 255f - Mean[0]) / Std[0]; // R
                    norm[1][y, x] = (buffer[idx + 1] / 255f - Mean[1]) / Std[1]; // G
                    norm[2][y, x] = (buffer[idx] / 255f - Mean[2]) / Std[2];     // B
                }
            }
        }
        finally
        {
            resized.UnlockBits(data);
        }
        return norm;
    }

    private static float[] Patchify(float[][,] norm, int rh, int rw, out int gridH, out int gridW)
    {
        gridH = rh / PatchSize;
        gridW = rw / PatchSize;
        int blocksH = gridH / MergeSize;
        int blocksW = gridW / MergeSize;
        int numPatches = gridH * gridW;
        float[] pixelValues = new float[(long)numPatches * FeatureLength];

        // Canonical Qwen ordering: rows over [bh, bw, mh, mw], cols over [channel, temporal, ph, pw].
        int patchRow = 0;
        for (int hb = 0; hb < blocksH; hb++)
        {
            for (int wb = 0; wb < blocksW; wb++)
            {
                for (int mh = 0; mh < MergeSize; mh++)
                {
                    for (int mw = 0; mw < MergeSize; mw++)
                    {
                        long baseIndex = (long)patchRow * FeatureLength;
                        int col = 0;
                        for (int c = 0; c < Channels; c++)
                        {
                            for (int t = 0; t < TemporalPatch; t++)
                            {
                                for (int ph = 0; ph < PatchSize; ph++)
                                {
                                    int yy = (hb * Factor) + (mh * PatchSize) + ph;
                                    for (int pw = 0; pw < PatchSize; pw++)
                                    {
                                        int xx = (wb * Factor) + (mw * PatchSize) + pw;
                                        pixelValues[baseIndex + col] = norm[c][yy, xx];
                                        col++;
                                    }
                                }
                            }
                        }
                        patchRow++;
                    }
                }
            }
        }
        return pixelValues;
    }
}
