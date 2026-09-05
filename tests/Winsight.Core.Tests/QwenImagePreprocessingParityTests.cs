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

using FluentAssertions;
using Winsight.Core.Modules.Vision;
using Xunit;

namespace Winsight.Core.Tests;

/// <summary>
/// Compares the hand-rolled C# Qwen preprocessing against the reference HF-processor
/// dump. Skipped unless WINSIGHT_VISION_REF_DIR (with pixel_values.f32) and
/// WINSIGHT_VISION_TEST_IMAGE are supplied.
/// </summary>
public class QwenImagePreprocessingParityTests
{
    private readonly ITestOutputHelper _output;

    public QwenImagePreprocessingParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Vision")]
    public void PixelValues_match_the_reference_processor()
    {
        string? refDir = Environment.GetEnvironmentVariable("WINSIGHT_VISION_REF_DIR");
        string? imagePath = Environment.GetEnvironmentVariable("WINSIGHT_VISION_TEST_IMAGE");
        if (string.IsNullOrWhiteSpace(refDir) || string.IsNullOrWhiteSpace(imagePath))
        {
            Assert.Skip("Requires WINSIGHT_VISION_REF_DIR and WINSIGHT_VISION_TEST_IMAGE.");
        }

        QwenPreprocessResult result = QwenImagePreprocessing.Build(
            imagePath,
            QwenImagePreprocessing.DefaultMinPixels,
            QwenImagePreprocessing.DefaultMaxPixels);

        byte[] raw = File.ReadAllBytes(Path.Combine(refDir, "pixel_values.f32"));
        float[] reference = new float[raw.Length / 4];
        Buffer.BlockCopy(raw, 0, reference, 0, raw.Length);

        result.PixelValues.Length.Should().Be(reference.Length);
        double maxAbs = 0;
        double sumAbs = 0;
        for (int i = 0; i < reference.Length; i++)
        {
            double diff = Math.Abs(result.PixelValues[i] - reference[i]);
            maxAbs = Math.Max(maxAbs, diff);
            sumAbs += diff;
        }
        double meanAbs = sumAbs / reference.Length;
        _output.WriteLine(
            $"grid=({result.GridT},{result.GridH},{result.GridW}) resized={result.ResizedWidth}x{result.ResizedHeight} " +
            $"maxAbs={maxAbs:E3} meanAbs={meanAbs:E3}");

        meanAbs.Should().BeLessThan(0.05);
    }
}
