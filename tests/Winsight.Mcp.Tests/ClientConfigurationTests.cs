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

using System.Text.Json.Nodes;
using FluentAssertions;
using Tomlyn;
using Tomlyn.Model;
using Winsight.Setup;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class ClientConfigurationTests
{
    private const string Executable = @"C:\Users\Example User\Programs\Winsight\winsight-mcp.exe";

    [Fact]
    public void Client_home_overrides_are_respected()
    {
        string[] names = ["CODEX_HOME", "CLAUDE_CONFIG_DIR", "GEMINI_CLI_HOME"];
        string?[] original = names.Select(Environment.GetEnvironmentVariable).ToArray();
        string fixture = Path.Combine(Path.GetTempPath(), "winsight-client-home");
        try
        {
            foreach (string name in names) Environment.SetEnvironmentVariable(name, fixture);
            Winsight.Setup.Program.ResolvePaths("codex,claude-code,gemini").Should().Equal(
                Path.Combine(fixture, "config.toml"), Path.Combine(fixture, ".claude.json"), Path.Combine(fixture, ".gemini", "settings.json"));
        }
        finally
        {
            for (int i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], original[i]);
        }
    }

    [Fact]
    public void Failed_manifest_write_rolls_back_the_client_registration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "winsight-setup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string client = Path.Combine(directory, "client.json");
            File.WriteAllText(client, "{ \"keep\": 7 }");
            byte[] original = File.ReadAllBytes(client);
            string manifest = Path.Combine(directory, "blocked.json");
            Directory.CreateDirectory(manifest);
            Action install = () => ClientConfiguration.Apply([client], Executable, manifestPath: manifest);
            install.Should().Throw<IOException>();
            File.ReadAllBytes(client).Should().Equal(original);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Install_is_idempotent_and_uninstall_preserves_other_settings(bool toml)
    {
        string original = toml ? "model = 'example'\n[mcp_servers.other]\ncommand = 'other.exe'\n" :
            "{\"theme\":\"dark\",\"mcpServers\":{\"other\":{\"command\":\"other.exe\"}}}";
        string installed = ClientConfiguration.Transform(original, Executable, toml);
        ClientConfiguration.Transform(installed, Executable, toml).Should().Be(installed);
        string removed = ClientConfiguration.Transform(installed, Executable, toml, remove: true);
        if (toml)
        {
            TomlTable model = TomlSerializer.Deserialize<TomlTable>(removed)!;
            model["model"].Should().Be("example");
            ((TomlTable)model["mcp_servers"]!).ContainsKey("other").Should().BeTrue();
            ((TomlTable)model["mcp_servers"]!).ContainsKey("winsight").Should().BeFalse();
        }
        else
        {
            JsonNode model = JsonNode.Parse(removed)!;
            model["theme"]!.GetValue<string>().Should().Be("dark");
            model["mcpServers"]!["other"]!["command"]!.GetValue<string>().Should().Be("other.exe");
            model["mcpServers"]!["winsight"].Should().BeNull();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Conflicting_registration_is_not_overwritten_or_removed(bool toml)
    {
        string input = ClientConfiguration.Transform("", "other.exe", toml);
        Action install = () => ClientConfiguration.Transform(input, Executable, toml);
        install.Should().Throw<InvalidDataException>();
        ClientConfiguration.Transform(input, Executable, toml, remove: true).Should().Be(input);
    }

    [Fact]
    public void Invalid_second_client_leaves_first_file_byte_exact()
    {
        string directory = Path.Combine(Path.GetTempPath(), "winsight-setup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string first = Path.Combine(directory, "settings.json");
            string second = Path.Combine(directory, "config.toml");
            File.WriteAllText(first, "{ \"preserve\": 7 }");
            byte[] before = File.ReadAllBytes(first);
            File.WriteAllText(second, "not = [valid");
            Action install = () => ClientConfiguration.Apply([first, second], Executable);
            install.Should().Throw<Exception>();
            File.ReadAllBytes(first).Should().Equal(before);
            Directory.GetFiles(directory, "*.bak").Should().BeEmpty();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Four_client_files_are_backed_up_and_removed_without_losing_other_entries()
    {
        string directory = Path.Combine(Path.GetTempPath(), "winsight-setup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string[] paths = ["config.toml", "desktop.json", "code.json", "gemini.json"];
            paths = paths.Select(name => Path.Combine(directory, name)).ToArray();
            foreach (string path in paths) File.WriteAllText(path, path.EndsWith(".toml", StringComparison.Ordinal) ? "keep = 7" : "{\"keep\":7}");
            byte[][] originals = paths.Select(File.ReadAllBytes).ToArray();
            ClientConfiguration.Apply(paths, Executable);
            foreach (string path in paths) Directory.GetFiles(directory, Path.GetFileName(path) + ".winsight-*.bak").Should().ContainSingle();
            foreach (string path in paths) File.ReadAllText(path).Should().Contain("winsight");
            ClientConfiguration.Apply(paths, Executable, remove: true);
            foreach (string path in paths) File.ReadAllText(path).Should().Contain("keep").And.NotContain("winsight");
            for (int i = 0; i < paths.Length; i++)
                Directory.GetFiles(directory, Path.GetFileName(paths[i]) + ".winsight-*.bak")
                    .Select(File.ReadAllBytes).Should().Contain(bytes => bytes.SequenceEqual(originals[i]));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
