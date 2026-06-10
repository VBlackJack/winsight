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

using System.Text.Json;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.Cdp;

/// <summary>
/// Parses the JSON body of a CDP <c>/json/list</c> response into
/// <see cref="CdpTarget"/> records. Kept free of any I/O so it is unit
/// testable against captured payloads.
/// </summary>
public static class CdpTargetParser
{
    /// <summary>
    /// Parse the target list. Entries missing the mandatory <c>id</c> field
    /// are skipped; all other fields degrade to empty strings or null.
    /// </summary>
    /// <exception cref="JsonException">The body is not valid JSON or not an array.</exception>
    public static IReadOnlyList<CdpTarget> Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                $"Expected a JSON array from /json/list, got {document.RootElement.ValueKind}.");
        }

        List<CdpTarget> targets = new();
        foreach (JsonElement entry in document.RootElement.EnumerateArray())
        {
            string? id = ReadString(entry, "id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }
            targets.Add(new CdpTarget(
                Id: id,
                Title: ReadString(entry, "title") ?? string.Empty,
                Url: ReadString(entry, "url") ?? string.Empty,
                Type: ReadString(entry, "type") ?? string.Empty,
                WebSocketDebuggerUrl: ReadString(entry, "webSocketDebuggerUrl")));
        }
        return targets;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }
        return null;
    }
}
