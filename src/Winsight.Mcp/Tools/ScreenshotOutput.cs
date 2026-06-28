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

namespace Winsight.Mcp.Tools;

/// <summary>
/// Resolves the directory and timestamped file path for screenshots written by
/// the capture tools. The output directory is configurable via the
/// WINSIGHT_OUTPUT_DIR environment variable so callers (such as a supervisor
/// agent reading captures from a mounted folder) can redirect them out of
/// %TEMP%. Falls back to %TEMP%\winsight when the variable is unset or blank.
/// </summary>
internal static class ScreenshotOutput
{
    public const string OutputDirectoryVariable = "WINSIGHT_OUTPUT_DIR";
    private const string DefaultSubdirectory = "winsight";
    private const string Extension = ".png";
    private const string TimestampFormat = "yyyyMMdd-HHmmss-fff";

    // Cap accumulated captures per prefix so a long agent session does not fill
    // the output directory. Pruning is best-effort and never fails a capture.
    private const int MaxRetainedPerPrefix = 50;

    /// <summary>
    /// Returns the configured WINSIGHT_OUTPUT_DIR, or %TEMP%\winsight when it is
    /// unset or whitespace. The directory is not created here; WindowCapture
    /// creates it on write.
    /// </summary>
    public static string ResolveDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable(OutputDirectoryVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), DefaultSubdirectory)
            : configured;
    }

    /// <summary>
    /// Builds a timestamped PNG path under <see cref="ResolveDirectory"/> using
    /// the given file-name prefix (for example "screenshot-").
    /// </summary>
    public static string BuildTimestampedPath(string filePrefix, bool pruneOldCaptures = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePrefix);
        string directory = ResolveDirectory();
        if (pruneOldCaptures)
        {
            PruneOldCaptures(directory, filePrefix);
        }
        string timestamp = DateTime.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        return Path.Combine(directory, filePrefix + timestamp + Extension);
    }

    /// <summary>
    /// Best-effort retention: keep only the most recent <see cref="MaxRetainedPerPrefix"/>
    /// captures sharing the prefix, deleting older ones. Never throws — disk
    /// hygiene must not break a capture.
    /// </summary>
    private static void PruneOldCaptures(string directory, string filePrefix)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }
            FileInfo[] existing = new DirectoryInfo(directory).GetFiles(filePrefix + "*" + Extension);
            if (existing.Length <= MaxRetainedPerPrefix)
            {
                return;
            }
            foreach (FileInfo file in existing
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(MaxRetainedPerPrefix))
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The file is in use or not ours to delete; skip it.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Enumerating the directory failed; pruning is optional.
        }
    }
}
