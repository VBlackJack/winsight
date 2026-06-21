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

using System.Runtime.Versioning;
using System.Text.Json;
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Diagnostics;
using Winsight.Core.Modules.Orchestration;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

[SupportedOSPlatform("windows")]
public class RunStepsToolTests
{
    private static StepRunner NewRunner() =>
        new(new ActionInvoker(), new ElementWaiter(), new ElementStateReader(), new WindowCapture());

    [Fact]
    public void Empty_steps_is_a_clear_failure()
    {
        string json = RunStepsTool.RunSteps(
            NewRunner(), new DiagnosticsObserver(), new SessionTarget(), Array.Empty<BatchStep>());

        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetString().Should().Contain("at least one step");
    }

    [Fact]
    public void Capture_exceptions_without_a_batch_process_id_fails_before_running()
    {
        // No desktop needed: the validation rejects the call before any step or
        // EventPipe session runs.
        string json = RunStepsTool.RunSteps(
            NewRunner(),
            new DiagnosticsObserver(),
            new SessionTarget(),
            new[] { new BatchStep("click", AutomationId: "X", ProcessId: 123) },
            processId: null,
            stopOnError: true,
            captureExceptions: true);

        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetString()
            .Should().Contain("captureExceptions requires a process id");
    }
}
