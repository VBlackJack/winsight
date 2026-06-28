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
using System.Runtime.CompilerServices;
using System.Diagnostics;
using ModelContextProtocol.Protocol;
using Winsight.Core.Models;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

/// <summary>
/// Outer guard applied at the tool boundary so no exception ever escapes to the
/// MCP transport. An unhandled exception in a tool kills the entire .NET host
/// process, which closes the stdio transport and renders every other tool
/// permanently unusable for the rest of the agent's session — observed in the
/// wild on UIA work that surfaced a COMException. Wrapping every tool in this
/// helper turns any throw into a structured OperationResult the agent can act on.
/// </summary>
internal static class ToolSafety
{
    /// <summary>
    /// Run a sync tool body. On success, return its serialized JSON shape.
    /// On any exception (excluding truly fatal ones the runtime cannot recover
    /// from), return an OperationResult.Failure JSON carrying the message.
    /// </summary>
    public static string SafeInvoke(Func<string> body, [CallerMemberName] string toolName = "")
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            string result = body();
            TraceRecorder.RecordToolResult(toolName, stopwatch.Elapsed, result);
            return result;
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            // Mirror the message to stderr so a human attaching to the host
            // can see the original stack. Stdout is reserved for protocol.
            Console.Error.WriteLine($"[winsight] tool exception: {ex}");
            string result = JsonSerializer.Serialize(
                OperationResult.Failure(ex.Message),
                JsonOptions.Default);
            TraceRecorder.RecordToolResult(toolName, stopwatch.Elapsed, result, isError: true);
            return result;
        }
    }

    /// <summary>
    /// Async overload with the same contract as <see cref="SafeInvoke(Func{string})"/>.
    /// </summary>
    public static async Task<string> SafeInvokeAsync(
        Func<Task<string>> body,
        [CallerMemberName] string toolName = "")
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            string result = await body().ConfigureAwait(false);
            TraceRecorder.RecordToolResult(toolName, stopwatch.Elapsed, result);
            return result;
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            Console.Error.WriteLine($"[winsight] tool exception: {ex}");
            string result = JsonSerializer.Serialize(
                OperationResult.Failure(ex.Message),
                JsonOptions.Default);
            TraceRecorder.RecordToolResult(toolName, stopwatch.Elapsed, result, isError: true);
            return result;
        }
    }

    /// <summary>
    /// Variant for tools that return rich content (text plus an inline image).
    /// Same guard contract as <see cref="SafeInvoke(Func{string})"/>: any
    /// non-fatal throw becomes an error <see cref="CallToolResult"/> whose single
    /// text block carries the OperationResult.Failure JSON, so the agent reads
    /// the same error shape whether the tool returns a string or content.
    /// </summary>
    public static CallToolResult SafeInvokeContent(
        Func<CallToolResult> body,
        [CallerMemberName] string toolName = "")
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            CallToolResult result = body();
            TraceRecorder.RecordContentResult(toolName, stopwatch.Elapsed, result);
            return result;
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            Console.Error.WriteLine($"[winsight] tool exception: {ex}");
            CallToolResult result = new()
            {
                IsError = true,
                Content =
                {
                    new TextContentBlock
                    {
                        Text = JsonSerializer.Serialize(
                            OperationResult.Failure(ex.Message), JsonOptions.Default),
                    },
                },
            };
            TraceRecorder.RecordContentResult(toolName, stopwatch.Elapsed, result);
            return result;
        }
    }

    private static bool IsFatal(Exception ex) =>
        ex is OutOfMemoryException
            or StackOverflowException
            or ThreadAbortException;
}
