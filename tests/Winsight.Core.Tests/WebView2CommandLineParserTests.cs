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
using Winsight.Core.Modules.WebView2;
using Xunit;

namespace Winsight.Core.Tests;

public class WebView2CommandLineParserTests
{
    [Fact]
    public void Returns_null_for_null_command_line()
    {
        WebView2CommandLineParser.TryGetDebuggingPort(null).Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_empty_command_line()
    {
        WebView2CommandLineParser.TryGetDebuggingPort(string.Empty).Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_flag_is_absent()
    {
        const string CommandLine = "\"C:\\Program Files\\msedgewebview2.exe\" --type=renderer --no-sandbox";
        WebView2CommandLineParser.TryGetDebuggingPort(CommandLine).Should().BeNull();
    }

    [Fact]
    public void Parses_port_from_typical_command_line()
    {
        const string CommandLine =
            "\"msedgewebview2.exe\" --type=renderer --remote-debugging-port=9222 --user-data-dir=...";
        WebView2CommandLineParser.TryGetDebuggingPort(CommandLine).Should().Be(9222);
    }

    [Theory]
    [InlineData("--remote-debugging-port=1", 1)]
    [InlineData("--remote-debugging-port=65535", 65535)]
    [InlineData("--REMOTE-DEBUGGING-PORT=8080", 8080)]
    public void Parses_full_port_range_and_is_case_insensitive(string commandLine, int expected)
    {
        WebView2CommandLineParser.TryGetDebuggingPort(commandLine).Should().Be(expected);
    }

    [Fact]
    public void Rejects_non_numeric_port_value()
    {
        WebView2CommandLineParser.TryGetDebuggingPort("--remote-debugging-port=abc").Should().BeNull();
    }
}
