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

namespace Winsight.Core.Models;

/// <summary>
/// Outcome of a <c>Runtime.evaluate</c> call against a CDP target.
/// <paramref name="ValueJson"/> carries the JSON-serialized value when the
/// expression succeeded and produced a by-value result; <paramref name="ExceptionMessage"/>
/// carries the script exception text when the expression threw.
/// </summary>
public sealed record CdpEvaluationResult(
    bool Success,
    string? ValueJson,
    string? ValueType,
    string? ExceptionMessage)
{
    /// <summary>Build a success result from the serialized value and its JS type.</summary>
    public static CdpEvaluationResult Ok(string? valueJson, string? valueType) =>
        new(true, valueJson, valueType, null);

    /// <summary>Build a failure result carrying the script exception text.</summary>
    public static CdpEvaluationResult Threw(string exceptionMessage) =>
        new(false, null, null, exceptionMessage);
}
