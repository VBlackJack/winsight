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

using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Diagnostics;
using Winsight.Core.Modules.Orchestration;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Core.Modules.UserActivity;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class AuditFixRegressionTests
{
    [Fact]
    public void Replay_steps_rejects_paths_outside_the_trace_directory()
    {
        string outsideFile = Path.Combine(Path.GetTempPath(), $"winsight-outside-{Guid.NewGuid():N}.json");
        File.WriteAllText(outsideFile, "{}");
        try
        {
            string json = RunStepsTool.ReplaySteps(
                new StepRunner(new ActionInvoker(), new ElementWaiter(), new ElementStateReader(), new WindowCapture()),
                new DiagnosticsObserver(),
                new SessionTarget(),
                outsideFile);
            using JsonDocument doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            doc.RootElement.GetProperty("error").GetString().Should().Contain("trace directory");
        }
        finally
        {
            if (File.Exists(outsideFile)) File.Delete(outsideFile);
        }
    }

    [Fact]
    public void Replay_steps_rejects_sibling_prefix_bypass_directory()
    {
        string traceRoot = Path.Combine(Path.GetTempPath(), "winsight", "traces2");
        string siblingFile = Path.Combine(traceRoot, "escape.json");
        TraceRecorder.IsAllowedReplayPath(siblingFile, out _, out string error).Should().BeFalse();
        error.Should().Contain("trace directory");
    }

    [Fact]
    public void Replay_steps_rejects_non_json_extensions()
    {
        string nonJson = Path.Combine(Path.GetTempPath(), "winsight", "traces", "notes.txt");
        TraceRecorder.IsAllowedReplayPath(nonJson, out _, out string error).Should().BeFalse();
        error.Should().Contain(".json");
    }

    [Fact]
    public void Click_and_wait_does_not_leak_primary_locator_fields_into_separate_wait_target()
    {
        string? priorDryRun = Environment.GetEnvironmentVariable(ToolAccessPolicy.DryRunVariable);
        Environment.SetEnvironmentVariable(ToolAccessPolicy.DryRunVariable, "1");
        try
        {
            using Process self = Process.GetCurrentProcess();
            string json = AgentExperienceTool.ClickAndWait(
                new ActionInvoker(),
                new ElementWaiter(),
                new UiSnapshotter(new WindowDiscovery(), new AffordanceScanner()),
                new SessionTarget(),
                name: "Submit",
                controlType: "Button",
                nameContains: "Sub",
                index: 1,
                condition: "exists",
                waitAutomationId: "StatusLabel",
                processId: self.Id);

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement waitLocator = doc.RootElement
                .GetProperty("data")
                .GetProperty("preview")
                .GetProperty("waitLocator");
            waitLocator.GetProperty("automationId").GetString().Should().Be("StatusLabel");
            waitLocator.TryGetProperty("name", out _).Should().BeFalse();
            waitLocator.TryGetProperty("controlType", out _).Should().BeFalse();
            waitLocator.TryGetProperty("nameContains", out _).Should().BeFalse();
            waitLocator.TryGetProperty("index", out _).Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ToolAccessPolicy.DryRunVariable, priorDryRun);
        }
    }

    [Fact]
    public void Type_and_assert_does_not_leak_primary_locator_fields_into_separate_assert_target()
    {
        string? priorDryRun = Environment.GetEnvironmentVariable(ToolAccessPolicy.DryRunVariable);
        Environment.SetEnvironmentVariable(ToolAccessPolicy.DryRunVariable, "1");
        try
        {
            using Process self = Process.GetCurrentProcess();
            string json = AgentExperienceTool.TypeAndAssert(
                new ActionInvoker(),
                new ElementStateReader(),
                new SessionTarget(),
                text: "hello",
                @operator: "equals",
                expected: "Saved",
                name: "InputBox",
                controlType: "Edit",
                nameContains: "Input",
                assertAutomationId: "StatusText",
                processId: self.Id);

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement assertLocator = doc.RootElement
                .GetProperty("data")
                .GetProperty("preview")
                .GetProperty("assertLocator");
            assertLocator.GetProperty("automationId").GetString().Should().Be("StatusText");
            assertLocator.TryGetProperty("name", out _).Should().BeFalse();
            assertLocator.TryGetProperty("controlType", out _).Should().BeFalse();
            assertLocator.TryGetProperty("nameContains", out _).Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ToolAccessPolicy.DryRunVariable, priorDryRun);
        }
    }

    [Fact]
    public async Task Act_verified_blocks_without_consuming_observation_when_user_yield_triggers()
    {
        string? priorYield = Environment.GetEnvironmentVariable(UserActivityPolicy.YieldToUserVariable);
        // Very large yield window guarantees the current interactive session looks active
        // unless idle for > 24 hours; skip assertion only if machine has truly been idle > 24h.
        Environment.SetEnvironmentVariable(UserActivityPolicy.YieldToUserVariable, "86400000");
        try
        {
            if (UserActivityPolicy.CheckYield() is null)
            {
                return;
            }

            using Process self = Process.GetCurrentProcess();
            SceneStore store = new();
            WindowIdentity window = new(self.Id, 1, new WindowBounds(0, 0, 100, 100));
            SceneElement element = new(
                "e1", "uia", "Button", "Go", null, true, true, false, null,
                new WindowBounds(10, 10, 40, 20), ["activate"], window,
                new ElementLocator(AutomationId: "GoBtn"), "1.2.3");
            SceneStore.Entry entry = store.Add(
                self.Id,
                self.StartTime.ToUniversalTime().Ticks,
                null,
                new SceneCapture([], [element], false, "uia"));

            SceneAction request = new(
                entry.Id,
                "e1",
                "activate",
                new SceneExpectation("exists", Locator: new ElementLocator(AutomationId: "GoBtn")));

            string response = await VerifiedSceneTool.Act(store, request, TestContext.Current.CancellationToken);
            using JsonDocument doc = JsonDocument.Parse(response);
            doc.RootElement.GetProperty("status").GetString().Should().Be("blocked");
            doc.RootElement.GetProperty("dispatched").GetBoolean().Should().BeFalse();
            doc.RootElement.GetProperty("message").GetString().Should().Contain("user_active");
            store.Get(entry.Id).Should().NotBeNull("observation must remain unconsumed when blocked by user yield");
        }
        finally
        {
            Environment.SetEnvironmentVariable(UserActivityPolicy.YieldToUserVariable, priorYield);
        }
    }
}
