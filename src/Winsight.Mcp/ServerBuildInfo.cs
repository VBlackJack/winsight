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

namespace Winsight.Mcp;

/// <summary>A server binary: its product version (with the commit) and the time its file was written.</summary>
internal sealed record BuildStamp(string Version, DateTime WrittenUtc);

/// <summary>
/// What an agent needs to notice that its tool list is stale: the build this
/// process runs, the exact tools it registered, and whether a newer build sits
/// in the source output that <c>tools\run-mcp.ps1</c> copied it from. A client
/// session keeps the server process, and so the tool list, it started with.
/// </summary>
internal static class ServerBuildInfo
{
    public const string SourceBuildDirVariable = "WINSIGHT_SOURCE_BUILD_DIR";
    private const string ServerFileName = "winsight-mcp.dll";
    private const string UnknownVersion = "unknown";

    // Copies keep the file time; a later time on the source means a rebuild,
    // allowing for coarse file system timestamps.
    private static readonly TimeSpan WriteTimeTolerance = TimeSpan.FromSeconds(2);

    private const string RestartHint =
        "A newer Winsight build exists than the one this session runs. Restart the client session to load it " +
        "and its tool list; until then, tools described elsewhere may be missing here.";

    public static object Describe(IEnumerable<string> registeredTools)
    {
        List<string> tools = registeredTools.Order(StringComparer.Ordinal).ToList();
        string runningPath = typeof(ServerBuildInfo).Assembly.Location;
        BuildStamp? running = ReadStamp(runningPath);
        string? sourceDirectory = Environment.GetEnvironmentVariable(SourceBuildDirVariable);
        BuildStamp? source = string.IsNullOrWhiteSpace(sourceDirectory)
            ? null
            : ReadStamp(Path.Combine(sourceDirectory, ServerFileName));
        bool? newer = IsNewer(running, source);
        using Process process = Process.GetCurrentProcess();
        return new
        {
            version = running?.Version ?? UnknownVersion,
            buildTimeUtc = running?.WrittenUtc,
            processStartUtc = process.StartTime.ToUniversalTime(),
            location = runningPath,
            toolCount = tools.Count,
            registeredTools = tools,
            sourceBuild = source is null ? null : new { version = source.Version, buildTimeUtc = source.WrittenUtc },
            newerBuildAvailable = newer,
            restartHint = newer == true ? RestartHint : null,
        };
    }

    /// <summary>Null when either build is unknown; true when the source is a different, later build.</summary>
    internal static bool? IsNewer(BuildStamp? running, BuildStamp? source)
    {
        if (running is null || source is null)
        {
            return null;
        }
        return !string.Equals(running.Version, source.Version, StringComparison.Ordinal) ||
            source.WrittenUtc - running.WrittenUtc > WriteTimeTolerance;
    }

    private static BuildStamp? ReadStamp(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }
        string version = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? UnknownVersion;
        return new BuildStamp(version, File.GetLastWriteTimeUtc(path));
    }
}
