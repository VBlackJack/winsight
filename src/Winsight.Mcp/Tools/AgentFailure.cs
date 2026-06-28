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

using Winsight.Core.Models;

namespace Winsight.Mcp.Tools;

internal sealed record RepairableFailureData(
    string Code,
    string Message,
    string? Cause,
    string? SuggestedTool,
    object? SuggestedArgs,
    object? Candidates,
    bool Retryable);

internal static class AgentFailure
{
    public static OperationResult Repairable(
        string code,
        string message,
        string? cause = null,
        string? suggestedTool = null,
        object? suggestedArgs = null,
        object? candidates = null,
        bool retryable = true)
    {
        return OperationResult.FailureWith(
            message,
            new RepairableFailureData(
                code,
                message,
                cause,
                suggestedTool,
                suggestedArgs,
                candidates,
                retryable));
    }
}
