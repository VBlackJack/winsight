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
}
