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
/// Outcome of a single step in a batch: its position, the action that ran, the
/// optional caller label, and the wrapped success/error/data of the underlying
/// primitive.
/// </summary>
public sealed record StepResult(
    int Index,
    string Action,
    string? Label,
    bool Success,
    string? Error,
    object? Data);

/// <summary>
/// Result of a <c>run_steps</c> batch: a per-step journal plus a summary.
/// <see cref="FailedAtIndex"/> is the index of the first failing step (null when
/// all executed steps succeeded). With stop-on-error, steps after the failure
/// are not executed and therefore absent from <see cref="Steps"/>.
/// <see cref="Exceptions"/> is null unless causal exception capture was requested;
/// when requested it lists the exceptions the target process threw during the
/// batch (an empty list means none were observed). <see cref="TimedOut"/> is true
/// when a batch duration budget was hit before all steps ran.
/// </summary>
public sealed record BatchResult(
    bool Success,
    int Completed,
    int Total,
    int? FailedAtIndex,
    IReadOnlyList<StepResult> Steps,
    IReadOnlyList<DiagnosticEvent>? Exceptions = null,
    bool TimedOut = false,
    string? TracePath = null,
    bool DryRun = false,
    bool ForegroundChanged = false);

public sealed record BatchReplay(
    DateTime CapturedAtUtc,
    IReadOnlyList<BatchStep> Steps,
    int? DefaultProcessId,
    bool StopOnError,
    int? MaxDurationMs,
    BatchResult Result);
