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
using Winsight.Core.Models;
using Winsight.Core.Modules.Vision;
using Xunit;

namespace Winsight.Core.Tests;

/// <summary>
/// Integration coverage for the local ONNX grounder. Skipped unless a provisioned
/// model directory is supplied via WINSIGHT_VISION_ONNX_PATH (the ~4-6 GB model is not
/// committed and not available in CI). WINSIGHT_VISION_TEST_IMAGE points at a screenshot.
/// </summary>
public class OnnxLocalGrounderIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public OnnxLocalGrounderIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task FindAsync_grounds_an_element_in_a_real_screenshot()
    {
        string? modelPath = Environment.GetEnvironmentVariable("WINSIGHT_VISION_ONNX_PATH");
        string? imagePath = Environment.GetEnvironmentVariable("WINSIGHT_VISION_TEST_IMAGE");
        if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(imagePath))
        {
            return; // no provisioned model -> skip
        }

        using OnnxLocalGrounder grounder = new(new OnnxLocalGrounderOptions(modelPath, "cpu"));

        GroundedElement result = await grounder.FindAsync(
            imagePath,
            "Click the Increment button.",
            TestContext.Current.CancellationToken);

        _output.WriteLine($"grounded point = ({result.X}, {result.Y}); source={result.Source}");
        result.Source.Should().Be(OnnxLocalGrounder.SourceModel);
        result.X.Should().BeGreaterThan(0);
        result.Y.Should().BeGreaterThan(0);
        result.Width.Should().Be(0);
        result.Height.Should().Be(0);
    }
}
