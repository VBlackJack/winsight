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
/// Pure parsing helpers for CDP response frames. Extracted from the
/// WebSocket client so the protocol logic is unit testable against
/// captured JSON payloads.
/// </summary>
public static class CdpResponseParser
{
    private const string IdProperty = "id";
    private const string ErrorProperty = "error";
    private const string MessageProperty = "message";

    /// <summary>
    /// Read the correlation id of a frame, or null when the frame is an
    /// event notification (events have a <c>method</c> but no <c>id</c>).
    /// </summary>
    public static long? TryReadId(JsonElement frame)
    {
        if (frame.ValueKind == JsonValueKind.Object &&
            frame.TryGetProperty(IdProperty, out JsonElement idElement) &&
            idElement.ValueKind == JsonValueKind.Number)
        {
            return idElement.GetInt64();
        }
        return null;
    }

    /// <summary>
    /// Extract the protocol-level error message of a response frame, or
    /// null when the command succeeded.
    /// </summary>
    public static string? TryReadError(JsonElement frame)
    {
        if (frame.ValueKind == JsonValueKind.Object &&
            frame.TryGetProperty(ErrorProperty, out JsonElement error) &&
            error.ValueKind == JsonValueKind.Object)
        {
            if (error.TryGetProperty(MessageProperty, out JsonElement message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
            return error.GetRawText();
        }
        return null;
    }

    /// <summary>
    /// Interpret the <c>result</c> payload of a <c>Runtime.evaluate</c>
    /// response (the element containing <c>result</c> and optionally
    /// <c>exceptionDetails</c>).
    /// </summary>
    public static CdpEvaluationResult ParseEvaluation(JsonElement evaluateResult)
    {
        if (evaluateResult.ValueKind == JsonValueKind.Object &&
            evaluateResult.TryGetProperty("exceptionDetails", out JsonElement exception))
        {
            return CdpEvaluationResult.Threw(DescribeException(exception));
        }

        if (evaluateResult.ValueKind != JsonValueKind.Object ||
            !evaluateResult.TryGetProperty("result", out JsonElement remoteObject))
        {
            return CdpEvaluationResult.Ok(null, null);
        }

        string? type = null;
        if (remoteObject.TryGetProperty("type", out JsonElement typeElement) &&
            typeElement.ValueKind == JsonValueKind.String)
        {
            type = typeElement.GetString();
        }

        if (remoteObject.TryGetProperty("value", out JsonElement value))
        {
            return CdpEvaluationResult.Ok(value.GetRawText(), type);
        }
        if (remoteObject.TryGetProperty("description", out JsonElement description) &&
            description.ValueKind == JsonValueKind.String)
        {
            return CdpEvaluationResult.Ok(
                JsonSerializer.Serialize(description.GetString()), type);
        }
        return CdpEvaluationResult.Ok(null, type);
    }

    private static string DescribeException(JsonElement exceptionDetails)
    {
        if (exceptionDetails.TryGetProperty("exception", out JsonElement exception) &&
            exception.TryGetProperty("description", out JsonElement description) &&
            description.ValueKind == JsonValueKind.String)
        {
            return description.GetString() ?? string.Empty;
        }
        if (exceptionDetails.TryGetProperty("text", out JsonElement text) &&
            text.ValueKind == JsonValueKind.String)
        {
            return text.GetString() ?? string.Empty;
        }
        return exceptionDetails.GetRawText();
    }
}
