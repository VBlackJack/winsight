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

namespace Winsight.Core.Modules.Vision;

public static class AnthropicResponseParser
{
    public static GroundedElement ParseEnvelope(string anthropicResponseJson, string sourceModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(anthropicResponseJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceModel);

        using var doc = JsonDocument.Parse(anthropicResponseJson);
        if (!doc.RootElement.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Anthropic response has no content array.");
        }

        string? text = null;
        foreach (var block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeProp))
            {
                continue;
            }
            if (typeProp.GetString() != "text")
            {
                continue;
            }
            if (block.TryGetProperty("text", out var textProp))
            {
                text = textProp.GetString();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("Anthropic response has no text content block.");
        }

        return ParseGrounded(text, sourceModel);
    }

    public static GroundedElement ParseGrounded(string modelText, string sourceModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelText);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceModel);

        var json = ExtractFirstJsonObject(modelText);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new GroundedElement(
            X: GetInt(root, "x"),
            Y: GetInt(root, "y"),
            Width: GetInt(root, "width"),
            Height: GetInt(root, "height"),
            Confidence: GetDouble(root, "confidence"),
            Reasoning: GetString(root, "reasoning") ?? string.Empty,
            Source: sourceModel);
    }

    private static string ExtractFirstJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
        {
            throw new FormatException(
                "Model response does not contain a JSON object: " + Truncate(text));
        }

        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (escape)
            {
                escape = false;
                continue;
            }
            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }
            if (c == '"')
            {
                inString = !inString;
                continue;
            }
            if (inString)
            {
                continue;
            }
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text.Substring(start, i - start + 1);
                }
            }
        }
        throw new FormatException(
            "Model response has unbalanced JSON braces: " + Truncate(text));
    }

    private static int GetInt(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element))
        {
            throw new FormatException($"Missing required property '{property}'.");
        }
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out var i)
                ? i
                : (int)Math.Round(element.GetDouble()),
            JsonValueKind.String when int.TryParse(element.GetString(), out var parsed) => parsed,
            _ => throw new FormatException($"Property '{property}' is not an integer."),
        };
    }

    private static double GetDouble(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element))
        {
            throw new FormatException($"Missing required property '{property}'.");
        }
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String when double.TryParse(element.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => throw new FormatException($"Property '{property}' is not a number."),
        };
    }

    private static string? GetString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element))
        {
            return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static string Truncate(string text)
    {
        const int Max = 200;
        return text.Length <= Max ? text : text[..Max] + "...";
    }
}
