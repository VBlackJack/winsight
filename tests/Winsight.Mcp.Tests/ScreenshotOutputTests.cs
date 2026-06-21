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
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public class ScreenshotOutputTests
{
    [Fact]
    public void ResolveDirectory_uses_the_environment_variable_when_set()
    {
        string? original = Environment.GetEnvironmentVariable(ScreenshotOutput.OutputDirectoryVariable);
        try
        {
            string custom = Path.Combine(Path.GetTempPath(), "winsight-test-output");
            Environment.SetEnvironmentVariable(ScreenshotOutput.OutputDirectoryVariable, custom);

            ScreenshotOutput.ResolveDirectory().Should().Be(custom);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScreenshotOutput.OutputDirectoryVariable, original);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveDirectory_falls_back_to_temp_when_unset_or_blank(string? value)
    {
        string? original = Environment.GetEnvironmentVariable(ScreenshotOutput.OutputDirectoryVariable);
        try
        {
            Environment.SetEnvironmentVariable(ScreenshotOutput.OutputDirectoryVariable, value);

            string expected = Path.Combine(Path.GetTempPath(), "winsight");
            ScreenshotOutput.ResolveDirectory().Should().Be(expected);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScreenshotOutput.OutputDirectoryVariable, original);
        }
    }

    [Fact]
    public void BuildTimestampedPath_combines_directory_prefix_and_png_extension()
    {
        string? original = Environment.GetEnvironmentVariable(ScreenshotOutput.OutputDirectoryVariable);
        try
        {
            string custom = Path.Combine(Path.GetTempPath(), "winsight-test-output");
            Environment.SetEnvironmentVariable(ScreenshotOutput.OutputDirectoryVariable, custom);

            string path = ScreenshotOutput.BuildTimestampedPath("screenshot-");

            Path.GetDirectoryName(path).Should().Be(custom);
            Path.GetFileName(path).Should().StartWith("screenshot-").And.EndWith(".png");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScreenshotOutput.OutputDirectoryVariable, original);
        }
    }

    [Fact]
    public void BuildTimestampedPath_rejects_a_blank_prefix()
    {
        Action act = () => ScreenshotOutput.BuildTimestampedPath("  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildTimestampedPath_prunes_old_captures_of_the_same_prefix()
    {
        string? original = Environment.GetEnvironmentVariable(ScreenshotOutput.OutputDirectoryVariable);
        string directory = Path.Combine(Path.GetTempPath(), $"winsight-prune-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable(ScreenshotOutput.OutputDirectoryVariable, directory);

            // Seed 60 captures of the pruned prefix plus one of an unrelated prefix.
            DateTime stamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 60; i++)
            {
                string seeded = Path.Combine(directory, $"screenshot-{i:D3}.png");
                File.WriteAllText(seeded, "x");
                File.SetLastWriteTimeUtc(seeded, stamp.AddSeconds(i));
            }
            string keepOtherPrefix = Path.Combine(directory, "vision-keep.png");
            File.WriteAllText(keepOtherPrefix, "x");

            ScreenshotOutput.BuildTimestampedPath("screenshot-");

            Directory.GetFiles(directory, "screenshot-*.png").Should().HaveCount(50,
                "the retention cap keeps the most recent 50 of the same prefix");
            File.Exists(keepOtherPrefix).Should().BeTrue("pruning is scoped to the matching prefix");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScreenshotOutput.OutputDirectoryVariable, original);
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
