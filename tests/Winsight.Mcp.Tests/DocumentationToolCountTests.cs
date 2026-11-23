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

using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Xunit;

namespace Winsight.Mcp.Tests;

/// <summary>
/// The tool counts quoted in the READMEs and the tools reference, English and
/// French, equal the numbers the server really registers: the full set and the
/// compact profile. A stale count fails here instead of reaching a release.
/// </summary>
public sealed class DocumentationToolCountTests
{
    private const string SolutionFile = "Winsight.slnx";
    private const string CompactProfile = "compact";
    private const string CountGroup = "count";

    // Matches "71 tools", "33-tool" and "71 outils".
    private static readonly Regex CountMention = new(
        @"(?<count>\d+)(?:[ -]tools?\b|[ -]outils\b)",
        RegexOptions.CultureInvariant);

    private static readonly string[] DocumentedFiles =
    [
        "README.md",
        "README.fr.md",
        Path.Combine("docs", "tools-reference.md"),
        Path.Combine("docs", "fr", "tools-reference.md"),
    ];

    [Fact]
    public void Documented_tool_counts_match_the_registered_tools()
    {
        (int fullCount, int compactCount) = ReadRegisteredCounts();
        compactCount.Should().BeLessThan(fullCount);
        string root = RepositoryRoot();

        foreach (string relative in DocumentedFiles)
        {
            string[] lines = File.ReadAllLines(Path.Combine(root, relative));
            List<(int Line, int Count)> mentions = [];
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in CountMention.Matches(lines[i]))
                {
                    mentions.Add((i + 1, int.Parse(match.Groups[CountGroup].Value, CultureInfo.InvariantCulture)));
                }
            }

            mentions.Should().Contain(m => m.Count == fullCount,
                because: $"{relative} must quote the full tool count ({fullCount})");
            foreach ((int line, int count) in mentions)
            {
                (count == fullCount || count == compactCount).Should().BeTrue(
                    because: $"{relative}:{line} quotes {count} tools, but the server registers {fullCount} (compact profile: {compactCount})");
            }
        }
    }

    private static (int Full, int Compact) ReadRegisteredCounts()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithToolsFromAssembly(typeof(Program).Assembly);
        using ServiceProvider provider = services.BuildServiceProvider();
        McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        int full = options.ToolCollection!.Count;
        McpServerConfiguration.ApplyToolProfile(options, CompactProfile);
        return (full, options.ToolCollection!.Count);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFile)))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException($"{SolutionFile} not found above {AppContext.BaseDirectory}.");
    }
}
