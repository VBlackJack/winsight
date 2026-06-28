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

internal static class AgentMacroResult
{
    public static OperationResult FromBatch(string toolName, BatchResult batch, object data)
    {
        if (batch.Success)
        {
            return OperationResult.OkWith(data);
        }

        return OperationResult.FailureWith(
            $"{toolName} failed: {DescribeBatchFailure(batch)}",
            data);
    }

    public static OperationResult FromParts(string toolName, object data, params OperationResult[] parts)
    {
        OperationResult? failure = parts.FirstOrDefault(part => !part.Success);
        if (failure is null)
        {
            return OperationResult.OkWith(data);
        }

        string detail = string.IsNullOrWhiteSpace(failure.Error)
            ? "an inner operation failed."
            : failure.Error;
        return OperationResult.FailureWith($"{toolName} failed: {detail}", data);
    }

    private static string DescribeBatchFailure(BatchResult batch)
    {
        if (batch.TimedOut)
        {
            return "batch timed out before completing all steps.";
        }

        StepResult? failed = batch.Steps.FirstOrDefault(step => !step.Success);
        if (failed is not null)
        {
            string action = string.IsNullOrWhiteSpace(failed.Action)
                ? "unknown"
                : failed.Action;
            string detail = string.IsNullOrWhiteSpace(failed.Error)
                ? "step failed without an error message."
                : failed.Error;
            return $"step {failed.Index} ({action}) failed: {detail}";
        }

        return "batch did not complete successfully.";
    }
}
