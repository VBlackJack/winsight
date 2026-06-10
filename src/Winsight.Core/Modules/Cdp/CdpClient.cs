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

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.Cdp;

/// <summary>
/// Minimal Chrome DevTools Protocol client over a raw <see cref="ClientWebSocket"/>.
/// One instance maps to one attached page target; commands are sent
/// sequentially with incrementing ids and event notification frames are
/// skipped while waiting for the matching response. Deliberately dependency
/// free — no Playwright/Puppeteer, so agents can drive embedded WebView2
/// content (including xterm.js terminals) through Winsight alone.
/// </summary>
public sealed class CdpClient : IAsyncDisposable
{
    private const string EvaluateMethod = "Runtime.evaluate";
    private const string DispatchKeyEventMethod = "Input.dispatchKeyEvent";
    private const string FocusScriptPrefix =
        "(() => { const el = document.querySelector(";
    private const string FocusScriptSuffix =
        "); if (!el) return false; el.focus(); return true; })()";

    private readonly ClientWebSocket _socket;
    private readonly int _commandTimeoutMs;
    private long _nextCommandId;

    private CdpClient(ClientWebSocket socket, int commandTimeoutMs)
    {
        _socket = socket;
        _commandTimeoutMs = commandTimeoutMs;
    }

    /// <summary>
    /// Connect to the page target <paramref name="targetId"/> on the CDP
    /// endpoint listening on <paramref name="port"/>.
    /// </summary>
    public static async Task<CdpClient> ConnectAsync(
        int port,
        string targetId,
        int? commandTimeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

        int timeout = commandTimeoutMs ?? CdpDefaults.ResolveCommandTimeoutMs();
        ClientWebSocket socket = new();
        try
        {
            using CancellationTokenSource timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            await socket
                .ConnectAsync(CdpDefaults.PageWebSocketUri(port, targetId), timeoutSource.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
        return new CdpClient(socket, timeout);
    }

    /// <summary>
    /// Evaluate a JavaScript expression in the page, awaiting promises and
    /// returning the result by value.
    /// </summary>
    public async Task<CdpEvaluationResult> EvaluateAsync(
        string expression,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        Dictionary<string, object?> parameters = new()
        {
            ["expression"] = expression,
            ["returnByValue"] = true,
            ["awaitPromise"] = true,
        };
        using JsonDocument response = await SendCommandAsync(
            EvaluateMethod, parameters, cancellationToken).ConfigureAwait(false);
        return CdpResponseParser.ParseEvaluation(GetResult(response));
    }

    /// <summary>
    /// Type text into the page as real key events. When
    /// <paramref name="focusSelector"/> is provided, the matching element is
    /// focused first and an <see cref="InvalidOperationException"/> is thrown
    /// if no element matches.
    /// </summary>
    /// <returns>The number of key events dispatched.</returns>
    public async Task<int> TypeTextAsync(
        string text,
        string? focusSelector = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (!string.IsNullOrWhiteSpace(focusSelector))
        {
            await FocusAsync(focusSelector, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<CdpKeyEvent> sequence = CdpKeyEventFactory.CreateSequence(text);
        foreach (CdpKeyEvent keyEvent in sequence)
        {
            Dictionary<string, object?> parameters = new()
            {
                ["type"] = keyEvent.EventType,
                ["key"] = keyEvent.Key,
            };
            if (keyEvent.Code is not null)
            {
                parameters["code"] = keyEvent.Code;
            }
            if (keyEvent.Text is not null)
            {
                parameters["text"] = keyEvent.Text;
                parameters["unmodifiedText"] = keyEvent.Text;
            }
            if (keyEvent.WindowsVirtualKeyCode is int virtualKeyCode)
            {
                parameters["windowsVirtualKeyCode"] = virtualKeyCode;
                parameters["nativeVirtualKeyCode"] = virtualKeyCode;
            }
            using JsonDocument response = await SendCommandAsync(
                DispatchKeyEventMethod, parameters, cancellationToken).ConfigureAwait(false);
            await Task.Delay(CdpDefaults.InterKeyDelayMs, cancellationToken).ConfigureAwait(false);
        }
        return sequence.Count;
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                using CancellationTokenSource closeTimeout = new(_commandTimeoutMs);
                await _socket
                    .CloseAsync(WebSocketCloseStatus.NormalClosure, null, closeTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // Best-effort close; the endpoint may already be gone.
                Console.Error.WriteLine($"[winsight] CDP close failed: {ex.Message}");
            }
        }
        _socket.Dispose();
    }

    private async Task FocusAsync(string selector, CancellationToken cancellationToken)
    {
        string selectorLiteral = JsonSerializer.Serialize(selector);
        string script = FocusScriptPrefix + selectorLiteral + FocusScriptSuffix;
        CdpEvaluationResult focusResult =
            await EvaluateAsync(script, cancellationToken).ConfigureAwait(false);
        if (!focusResult.Success)
        {
            throw new InvalidOperationException(
                $"Focus script threw: {focusResult.ExceptionMessage}");
        }
        if (focusResult.ValueJson != "true")
        {
            throw new InvalidOperationException(
                $"No element matches selector '{selector}' in the target page.");
        }
    }

    private async Task<JsonDocument> SendCommandAsync(
        string method,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        long commandId = Interlocked.Increment(ref _nextCommandId);
        Dictionary<string, object?> frame = new()
        {
            ["id"] = commandId,
            ["method"] = method,
            ["params"] = parameters,
        };
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(frame);

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_commandTimeoutMs);

        await _socket
            .SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, timeoutSource.Token)
            .ConfigureAwait(false);

        while (true)
        {
            string message = await ReceiveMessageAsync(timeoutSource.Token).ConfigureAwait(false);
            JsonDocument document = JsonDocument.Parse(message);
            long? frameId = CdpResponseParser.TryReadId(document.RootElement);
            if (frameId != commandId)
            {
                // Event notification or stale response — not ours, keep waiting.
                document.Dispose();
                continue;
            }

            string? error = CdpResponseParser.TryReadError(document.RootElement);
            if (error is not null)
            {
                document.Dispose();
                throw new CdpCommandException(method, error);
            }
            return document;
        }
    }

    private async Task<string> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[CdpDefaults.ReceiveBufferBytes];
        using MemoryStream accumulated = new();
        while (true)
        {
            WebSocketReceiveResult result = await _socket
                .ReceiveAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException("CDP endpoint closed the connection.");
            }
            accumulated.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(accumulated.ToArray());
            }
        }
    }

    private static JsonElement GetResult(JsonDocument response)
    {
        if (response.RootElement.TryGetProperty("result", out JsonElement result))
        {
            return result;
        }
        return response.RootElement;
    }
}
