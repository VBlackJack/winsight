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
using Winsight.Mcp;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.E2E;

[Trait("Category", "E2E")]
public class AgentExperienceScenarioTests
{
    private const string IncrementButtonId = "IncrementButton";
    private const string CountLabelId = "CountLabel";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Preferred_agent_loop_observes_plans_dry_runs_acts_and_repairs()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        try
        {
            var waiter = new ElementWaiter();
            OperationResult appeared = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: CountLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);

            var discovery = new WindowDiscovery();
            var scanner = new AffordanceScanner();
            var capture = new WindowCapture();
            var target = new SessionTarget();
            var memory = new SessionMemory();
            var runner = new StepRunner(new ActionInvoker(), waiter, new ElementStateReader(), capture);

            string setJson = SessionTargetTool.SetTarget(target, discovery, process.Id);
            Parse(setJson).RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

            string observeJson = AgentExperienceTool.Observe(discovery, scanner, capture, target, memory);
            using JsonDocument observeDoc = Parse(observeJson);
            observeDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            observeDoc.RootElement.GetProperty("data").GetProperty("elements").EnumerateArray()
                .Select(AutomationIdOf)
                .Should().Contain(IncrementButtonId);

            string planJson = AgentExperienceTool.PlanSteps(scanner, target, memory, "click increment");
            using JsonDocument planDoc = Parse(planJson);
            JsonElement stepElement = planDoc.RootElement
                .GetProperty("data")
                .GetProperty("suggestedRun")
                .GetProperty("steps")[0];
            BatchStep plannedStep = JsonSerializer.Deserialize<BatchStep>(stepElement.GetRawText(), JsonOptions)!;
            plannedStep.Action.Should().Be("click");
            plannedStep.AutomationId.Should().Be(IncrementButtonId);

            string dryRunJson = RunStepsTool.RunSteps(
                runner,
                new DiagnosticsObserver(),
                target,
                new[] { plannedStep },
                dryRun: true);
            using JsonDocument dryRunDoc = Parse(dryRunJson);
            dryRunDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            dryRunDoc.RootElement.GetProperty("dryRun").GetBoolean().Should().BeTrue();

            string actJson = AgentExperienceTool.ActAndObserve(
                runner,
                new UiSnapshotter(discovery, scanner),
                scanner,
                capture,
                target,
                memory,
                plannedStep);
            using JsonDocument actDoc = Parse(actJson);
            actDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(actDoc.RootElement.ToString());
            actDoc.RootElement.GetProperty("data").GetProperty("action").GetProperty("success").GetBoolean()
                .Should().BeTrue();

            string repairJson = AgentExperienceTool.RepairLocator(scanner, target, memory, "increment");
            using JsonDocument repairDoc = Parse(repairJson);
            repairDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            repairDoc.RootElement.GetProperty("data").GetProperty("currentUi").EnumerateArray()
                .Should().Contain(candidate =>
                    AutomationIdOf(candidate.GetProperty("element")) == IncrementButtonId);

            string clickAndWaitJson = AgentExperienceTool.ClickAndWait(
                new ActionInvoker(),
                waiter,
                new UiSnapshotter(discovery, scanner),
                target,
                automationId: IncrementButtonId);
            using JsonDocument clickAndWaitDoc = Parse(clickAndWaitJson);
            clickAndWaitDoc.RootElement.GetProperty("success").GetBoolean()
                .Should().BeTrue(clickAndWaitDoc.RootElement.ToString());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    private static string? AutomationIdOf(JsonElement element) =>
        element.TryGetProperty("automationId", out JsonElement id)
            ? id.GetString()
            : null;
}
