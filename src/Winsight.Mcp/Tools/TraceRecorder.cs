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
using ModelContextProtocol.Protocol;
using Winsight.Core.Models;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

internal static class TraceRecorder
{
    public const string TraceEnabledVariable = "WINSIGHT_TRACE";
    public const string TraceDirectoryVariable = "WINSIGHT_TRACE_DIR";

    private static readonly object Gate = new();

    public static bool IsEnabled =>
        ResolveDirectory(create: false) is not null;

    public static string? CurrentDirectory => ResolveDirectory(create: false);

    public static void RecordToolResult(
        string toolName,
        TimeSpan elapsed,
        string payload,
        bool isError = false)
    {
        string? directory = ResolveDirectory(create: true);
        if (directory is null)
        {
            return;
        }

        (bool? success, string? error) = TryReadOperationResult(payload);
        var entry = new
        {
            timestampUtc = DateTime.UtcNow,
            toolName,
            durationMs = (long)elapsed.TotalMilliseconds,
            isError,
            success,
            error,
            payloadBytes = Encoding.UTF8.GetByteCount(payload),
        };
        AppendJsonLine(Path.Combine(directory, $"winsight-{DateTime.UtcNow:yyyyMMdd}.jsonl"), entry);
    }

    public static void RecordContentResult(string toolName, TimeSpan elapsed, CallToolResult result)
    {
        string payload = result.Content
            .OfType<TextContentBlock>()
            .FirstOrDefault()
            ?.Text ?? string.Empty;
        RecordToolResult(toolName, elapsed, payload, result.IsError == true);
    }

    public static string? WriteReplay(BatchReplay replay, bool force)
    {
        string? directory = ResolveDirectory(create: true);
        if (directory is null && force)
        {
            directory = ResolveDefaultDirectory(create: true);
        }
        if (directory is null)
        {
            return null;
        }

        string path = Path.Combine(directory, $"run-steps-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
        string json = JsonSerializer.Serialize(replay, JsonOptions.Default);
        File.WriteAllText(path, json);
        return path;
    }

    private static string? ResolveDirectory(bool create)
    {
        string? configured = Environment.GetEnvironmentVariable(TraceDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string full = Path.GetFullPath(configured);
            if (create)
            {
                Directory.CreateDirectory(full);
            }
            return full;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable(TraceEnabledVariable)))
        {
            return ResolveDefaultDirectory(create);
        }

        return null;
    }

    private static string ResolveDefaultDirectory(bool create)
    {
        string full = Path.Combine(Path.GetTempPath(), "winsight", "traces");
        if (create)
        {
            Directory.CreateDirectory(full);
        }
        return full;
    }

    private static void AppendJsonLine(string path, object entry)
    {
        string json = JsonSerializer.Serialize(entry, JsonOptions.Default);
        lock (Gate)
        {
            File.AppendAllText(path, json + Environment.NewLine);
        }
    }

    private static (bool? Success, string? Error) TryReadOperationResult(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return (null, null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            bool? success = root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("success", out JsonElement successElement) &&
                successElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? successElement.GetBoolean()
                    : null;
            string? error = root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("error", out JsonElement errorElement) &&
                errorElement.ValueKind == JsonValueKind.String
                    ? errorElement.GetString()
                    : null;
            return (success, error);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static bool IsTruthy(string? raw)
    {
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "on", StringComparison.OrdinalIgnoreCase);
    }
}
