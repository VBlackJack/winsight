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

using System.Drawing;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.Vision;

[SupportedOSPlatform("windows")]
public sealed class AnthropicVisionGrounder : IElementGrounder
{
    public const string DefaultApiUrl = "https://api.anthropic.com/v1/messages";
    public const string DefaultModel = "claude-sonnet-4-6";
    public const string ApiVersion = "2023-06-01";
    public const int DefaultTimeoutMs = 60_000;
    private const int MaxResponseTokens = 512;
    private const int MaxAttempts = 3;
    private const int RetryBaseDelayMs = 300;
    private const string MissingKeyMessage =
        "ANTHROPIC_API_KEY environment variable is not set. " +
        "Vision grounding requires an Anthropic API key.";

    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly string _apiUrl;
    private readonly int _timeoutMs;

    public AnthropicVisionGrounder(
        HttpClient http,
        string? apiKey,
        string? model = null,
        string? apiUrl = null,
        int timeoutMs = DefaultTimeoutMs)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        _apiUrl = string.IsNullOrWhiteSpace(apiUrl) ? DefaultApiUrl : apiUrl;
        _timeoutMs = timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs;
    }

    public async Task<GroundedElement> FindAsync(
        string screenshotPath,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenshotPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException(MissingKeyMessage);
        }

        var imageBytes = await File.ReadAllBytesAsync(screenshotPath, cancellationToken);
        (int width, int height) = ReadDimensions(screenshotPath);
        var prompt = BuildPrompt(query, width, height);
        var requestJson = BuildRequestJson(imageBytes, prompt);

        string responseBody = await SendWithRetryAsync(requestJson, cancellationToken)
            .ConfigureAwait(false);
        return AnthropicResponseParser.ParseEnvelope(responseBody, _model);
    }

    /// <summary>
    /// Send the request with a bounded per-attempt timeout and a short retry on
    /// transient failures (HTTP 429 and 5xx), so a slow or rate-limited API does
    /// not hang the agent or fail a recoverable call.
    /// </summary>
    private async Task<string> SendWithRetryAsync(string requestJson, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, _apiUrl);
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", ApiVersion);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using CancellationTokenSource timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeoutMs);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The linked token tripped on the timeout, not on the caller's token.
                throw new TimeoutException(
                    $"Anthropic API call did not complete within {_timeoutMs} ms.");
            }

            using (response)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return body;
                }
                if (IsTransient(response.StatusCode) && attempt < MaxAttempts)
                {
                    await Task.Delay(RetryBaseDelayMs * attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                throw new HttpRequestException(
                    $"Anthropic API returned {(int)response.StatusCode}: {body}");
            }
        }
    }

    private static bool IsTransient(System.Net.HttpStatusCode status) =>
        status == System.Net.HttpStatusCode.TooManyRequests || (int)status >= 500;

    private string BuildRequestJson(byte[] imageBytes, string prompt)
    {
        var base64 = Convert.ToBase64String(imageBytes);
        var payload = new
        {
            model = _model,
            max_tokens = MaxResponseTokens,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image",
                            source = new
                            {
                                type = "base64",
                                media_type = "image/png",
                                data = base64,
                            },
                        },
                        new
                        {
                            type = "text",
                            text = prompt,
                        },
                    },
                },
            },
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string BuildPrompt(string query, int width, int height)
    {
        return
            "You are a precise UI element locator for Windows desktop application screenshots." +
            $"\n\nThe screenshot is {width}x{height} pixels with origin (0,0) at the top-left corner." +
            "\n\nLocate the element that best matches this description:" +
            $"\n\"{query}\"" +
            "\n\nRespond with ONLY a JSON object on a single line, no prose, no code fence, no commentary:" +
            "\n{\"x\":CENTER_X,\"y\":CENTER_Y,\"width\":BBOX_W,\"height\":BBOX_H," +
            "\"confidence\":FLOAT_BETWEEN_0_AND_1,\"reasoning\":\"one short sentence\"}" +
            "\n\nx and y are pixel coordinates of the bounding box CENTER (not the top-left)." +
            "\nIf no element clearly matches, set confidence to 0 and explain why in reasoning.";
    }

    private static (int Width, int Height) ReadDimensions(string path)
    {
        using var image = Image.FromFile(path);
        return (image.Width, image.Height);
    }
}
