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

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Winsight.Core.Models;

namespace Winsight.Mcp;

public sealed record ProfileStep(ElementLocator Target, string Action, SceneExpectation Expect,
    string? TextParameter = null, string? ItemName = null, string? WindowTitle = null);
public sealed record ApplicationProfile(string Name, string Executable, string Version, string Sha256,
    IReadOnlyList<ProfileStep> Steps, int SchemaVersion = 1);
public sealed record ApplicationProfileSummary(string Name, string Executable, string Version, string Sha256, int StepCount);

/// <summary>Explicit recipes, pinned to executable bytes. Runtime text is never persisted.</summary>
public sealed partial class ApplicationProfiles
{
    private const int MaximumBytes = 1048576;
    private readonly string _directory;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public ApplicationProfiles(string? directory = null) => _directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winsight", "profiles");

    public static (string Path, string Version, string Hash) Fingerprint(int pid)
    {
        using Process process = Process.GetProcessById(pid);
        string path = process.MainModule?.FileName ?? throw new InvalidOperationException("Executable path unavailable.");
        using FileStream stream = File.OpenRead(path);
        return (path, FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty, Convert.ToHexString(SHA256.HashData(stream)));
    }

    public void Save(ApplicationProfile profile)
    {
        string path = Resolve(profile.Name);
        if (profile.SchemaVersion != 1 || profile.Steps.Count is < 1 or > 20) throw new ArgumentException("Profile requires schema 1 and 1..20 steps.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(profile, Options);
        if (bytes.Length > MaximumBytes) throw new ArgumentException("Profile exceeds size limit.");
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            if (!File.Exists(path) && Directory.EnumerateFiles(_directory, "*.json").Take(128).Count() >= 128) throw new InvalidOperationException("Profile capacity reached.");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public ApplicationProfile Load(string name)
    {
        string path = Resolve(name);
        using FileStream stream = File.OpenRead(path);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Profile exceeds size limit.");
        ApplicationProfile profile = JsonSerializer.Deserialize<ApplicationProfile>(stream, Options) ?? throw new InvalidDataException("Empty profile.");
        if (profile.Name != name || profile.SchemaVersion != 1 || profile.Steps is null || profile.Steps.Count is < 1 or > 20) throw new InvalidDataException("Invalid profile schema.");
        return profile;
    }

    public IReadOnlyList<ApplicationProfileSummary> List()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory)) return [];
            List<ApplicationProfileSummary> items = new();
            foreach (string file in Directory.EnumerateFiles(_directory, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                try
                {
                    ApplicationProfile profile = Load(name);
                    items.Add(new ApplicationProfileSummary(profile.Name, profile.Executable, profile.Version, profile.Sha256, profile.Steps.Count));
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or ArgumentException)
                {
                    // Skip unreadable/corrupt entries rather than breaking listing.
                }
            }
            return items;
        }
    }

    public bool Delete(string name)
    {
        string path = Resolve(name);
        lock (_gate)
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
    }

    /// <summary>Best-effort discovery of saved profiles matching the running executable bytes and version.</summary>
    public IReadOnlyList<string> FindMatchingNames(int pid)
    {
        IReadOnlyList<ApplicationProfileSummary> saved = List();
        if (saved.Count == 0) return [];
        try
        {
            (string path, string version, string hash) = Fingerprint(pid);
            return saved
                .Where(p =>
                    string.Equals(p.Executable, path, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.Version, version, StringComparison.Ordinal) &&
                    string.Equals(p.Sha256, hash, StringComparison.Ordinal))
                .Select(p => p.Name)
                .ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    private string Resolve(string name)
    {
        if (name is null || !ProfileName().IsMatch(name)) throw new ArgumentException("Profile name must use 1..64 ASCII letters, digits, underscores or hyphens.");
        return Path.Combine(_directory, name + ".json");
    }
    [GeneratedRegex("\\A[a-zA-Z0-9_-]{1,64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileName();
}
