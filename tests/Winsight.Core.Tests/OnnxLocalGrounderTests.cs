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

public class OnnxLocalGrounderTests
{
    [Fact]
    public async Task FindAsync_reports_missing_model_path_when_onnx_backend_is_selected()
    {
        string screenshotPath = CreateTempScreenshotFile();
        var grounder = new OnnxLocalGrounder(new OnnxLocalGrounderOptions(null, null));

        Func<Task> act = () => grounder.FindAsync(screenshotPath, "increment button");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*WINSIGHT_VISION_ONNX_PATH*");
    }

    [Fact]
    public async Task FindAsync_reports_missing_model_directory()
    {
        string screenshotPath = CreateTempScreenshotFile();
        string missingModelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var grounder = new OnnxLocalGrounder(new OnnxLocalGrounderOptions(missingModelPath, "cpu"));

        Func<Task> act = () => grounder.FindAsync(screenshotPath, "increment button");

        await act.Should().ThrowAsync<DirectoryNotFoundException>()
            .WithMessage("*WINSIGHT_VISION_ONNX_PATH*");
    }

    [Fact]
    public async Task FindAsync_rejects_unknown_execution_provider()
    {
        string screenshotPath = CreateTempScreenshotFile();
        string modelPath = Directory.CreateTempSubdirectory("winsight-onnx-test-").FullName;
        var grounder = new OnnxLocalGrounder(new OnnxLocalGrounderOptions(modelPath, "cuda"));

        Func<Task> act = () => grounder.FindAsync(screenshotPath, "increment button");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*WINSIGHT_VISION_EP*");
    }

    [Theory]
    [InlineData("dml")]
    [InlineData("directml")]
    public async Task FindAsync_rejects_directml_until_the_backend_implements_it(string provider)
    {
        string screenshotPath = CreateTempScreenshotFile();
        string modelPath = Directory.CreateTempSubdirectory("winsight-onnx-test-").FullName;
        var grounder = new OnnxLocalGrounder(new OnnxLocalGrounderOptions(modelPath, provider));

        Func<Task> act = () => grounder.FindAsync(screenshotPath, "increment button");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must be 'cpu'*");
    }

    [Fact]
    public async Task FindAsync_fails_explicitly_when_model_files_are_absent()
    {
        // Config is valid (directory exists) but holds no ONNX model: the local
        // backend must fail loudly while loading, never silently fall back to cloud.
        string screenshotPath = CreateTempScreenshotFile();
        string modelPath = Directory.CreateTempSubdirectory("winsight-onnx-test-").FullName;
        using OnnxLocalGrounder grounder = new(new OnnxLocalGrounderOptions(modelPath, "cpu"));

        Func<Task> act = () => grounder.FindAsync(screenshotPath, "increment button");

        await act.Should().ThrowAsync<Exception>();
    }

    private static string CreateTempScreenshotFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"winsight-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [137, 80, 78, 71]);
        return path;
    }
}
