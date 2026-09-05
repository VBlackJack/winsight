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

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Model;

namespace Winsight.Setup;

/// <summary>Edits only the Winsight registration, preserving other client settings.</summary>
public static class ClientConfiguration
{
    private const string ServerName = "winsight";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Transform(string input, string executable, bool toml, bool remove = false)
    {
        return toml ? TransformToml(input, executable, remove) : TransformJson(input, executable, remove);
    }

    private static string TransformJson(string input, string executable, bool remove)
    {
        JsonObject root = string.IsNullOrWhiteSpace(input) ? new() :
            JsonNode.Parse(input) as JsonObject ?? throw new InvalidDataException("Expected a JSON object.");
        JsonObject servers = root["mcpServers"] as JsonObject ??
            (root.ContainsKey("mcpServers") ? throw new InvalidDataException("Invalid mcpServers object.") : new());
        JsonNode? existing = servers[ServerName];
        if (existing is not null)
        {
            string? command = existing["command"]?.GetValue<string>();
            if (!string.Equals(command, executable, StringComparison.OrdinalIgnoreCase))
            {
                if (remove) return input;
                throw new InvalidDataException("Another Winsight registration already exists. Remove it in the client before retrying.");
            }
            if (!remove) return input;
            servers.Remove(ServerName);
        }
        else if (remove) return input;
        else servers[ServerName] = new JsonObject { ["command"] = executable, ["args"] = new JsonArray() };
        if (root["mcpServers"] is null) root["mcpServers"] = servers;
        return root.ToJsonString(JsonOptions) + Environment.NewLine;
    }

    private static string TransformToml(string input, string executable, bool remove)
    {
        TomlTable root = TomlSerializer.Deserialize<TomlTable>(input) ?? new();
        TomlTable servers = root.TryGetValue("mcp_servers", out object? value)
            ? value as TomlTable ?? throw new InvalidDataException("Invalid mcp_servers table.") : new();
        if (servers.TryGetValue(ServerName, out object? existing))
        {
            string? command = existing is TomlTable table && table.TryGetValue("command", out object? raw) ? raw as string : null;
            if (!string.Equals(command, executable, StringComparison.OrdinalIgnoreCase))
            {
                if (remove) return input;
                throw new InvalidDataException("Another Winsight registration already exists. Remove it in Codex before retrying.");
            }
            if (!remove) return input;
            servers.Remove(ServerName);
        }
        else if (remove) return input;
        else servers[ServerName] = new TomlTable { ["command"] = executable, ["args"] = new TomlArray() };
        root["mcp_servers"] = servers;
        return TomlSerializer.Serialize(root);
    }

    /// <summary>Prepare all edits before writing; preserve backups and roll back only unchanged writes.</summary>
    public static void Apply(IReadOnlyList<string> paths, string executable, bool remove = false, string? manifestPath = null)
    {
        List<Edit> edits = new();
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;
            string input = original is null ? string.Empty : new UTF8Encoding(false, true).GetString(original).TrimStart('\uFEFF');
            string output = Transform(input, executable, path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase), remove);
            if (input != output) edits.Add(new Edit(path, original, Encoding.UTF8.GetBytes(output)));
        }
        if (manifestPath is not null && !remove)
        {
            byte[]? previousBytes = File.Exists(manifestPath) ? File.ReadAllBytes(manifestPath) : null;
            string[] previous = previousBytes is null ? [] : JsonSerializer.Deserialize<string[]>(previousBytes) ?? [];
            byte[] updated = JsonSerializer.SerializeToUtf8Bytes(previous.Concat(paths).Distinct(StringComparer.OrdinalIgnoreCase));
            edits.Add(new Edit(manifestPath, previousBytes, updated));
        }
        List<Edit> completed = new();
        try
        {
            foreach (Edit edit in edits)
            {
                Write(edit.Path, edit.Original, edit.Updated);
                completed.Add(edit);
            }
        }
        catch
        {
            foreach (Edit edit in completed.AsEnumerable().Reverse())
            {
                // Never overwrite a newer edit made by the client during rollback.
                if (File.Exists(edit.Path) && File.ReadAllBytes(edit.Path).AsSpan().SequenceEqual(edit.Updated))
                {
                    if (edit.Original is null) File.Delete(edit.Path);
                    else Write(edit.Path, edit.Updated, edit.Original);
                }
            }
            throw;
        }
    }

    private static void Write(string path, byte[]? expected, byte[] updated)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".winsight-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, updated);
            if (expected is null) File.Move(temporary, path, overwrite: false);
            else
            {
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
                    throw new IOException("Client configuration changed during setup. Close the client and retry.");
                File.Replace(temporary, path, path + ".winsight-" + Guid.NewGuid().ToString("N") + ".bak");
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record Edit(string Path, byte[]? Original, byte[] Updated);
}
