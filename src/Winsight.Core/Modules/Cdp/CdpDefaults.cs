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

namespace Winsight.Core.Modules.Cdp;

/// <summary>
/// Centralized constants for the CDP module so no endpoint, timeout, or
/// buffer size is hardcoded at call sites. The command timeout can be
/// overridden per call via tool parameters and globally via the
/// <c>WINSIGHT_CDP_TIMEOUT_MS</c> environment variable.
/// </summary>
public static class CdpDefaults
{
    /// <summary>Loopback host where WebView2 exposes its debugging endpoint.</summary>
    public const string Host = "127.0.0.1";

    /// <summary>Name of the environment variable overriding the default command timeout.</summary>
    public const string TimeoutEnvironmentVariable = "WINSIGHT_CDP_TIMEOUT_MS";

    /// <summary>Fallback command timeout when no override is provided.</summary>
    public const int FallbackCommandTimeoutMs = 10_000;

    /// <summary>Upper bound for a single CDP command timeout.</summary>
    public const int MaxCommandTimeoutMs = 120_000;

    /// <summary>Maximum size of one CDP response frame accumulated in memory.</summary>
    public const int MaxReceiveMessageBytes = 4 * 1024 * 1024;

    /// <summary>Delay between successive key events when typing, in milliseconds.</summary>
    public const int InterKeyDelayMs = 10;

    /// <summary>Receive buffer size for WebSocket frames.</summary>
    public const int ReceiveBufferBytes = 65_536;

    /// <summary>
    /// Resolve the effective default command timeout: environment override
    /// when set and valid, fallback constant otherwise.
    /// </summary>
    public static int ResolveCommandTimeoutMs()
    {
        string? raw = Environment.GetEnvironmentVariable(TimeoutEnvironmentVariable);
        if (int.TryParse(raw, out int parsed) && parsed > 0 && parsed <= MaxCommandTimeoutMs)
        {
            return parsed;
        }
        return FallbackCommandTimeoutMs;
    }

    public static int NormalizeCommandTimeoutMs(int? commandTimeoutMs)
    {
        int timeout = commandTimeoutMs ?? ResolveCommandTimeoutMs();
        if (timeout <= 0 || timeout > MaxCommandTimeoutMs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(commandTimeoutMs),
                $"CDP timeout must be between 1 and {MaxCommandTimeoutMs} ms.");
        }
        return timeout;
    }

    /// <summary>HTTP URL listing the debuggable targets of an endpoint.</summary>
    public static Uri TargetListUri(int port) =>
        new($"http://{Host}:{port}/json/list");

    /// <summary>WebSocket URL attaching to a page target.</summary>
    public static Uri PageWebSocketUri(int port, string targetId) =>
        new($"ws://{Host}:{port}/devtools/page/{targetId}");
}
