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
using Winsight.Core.Modules.UiAutomation;
using Winsight.Eval;
using EvalProgram = Winsight.Eval.Program;
using Winsight.Mcp;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.E2E;

[Trait("Category", "E2E")]
public sealed class EvaluationOracleTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task Protected_change_and_restoration_are_detected_from_live_UI_state()
    {
        using Process process = Process.Start(new ProcessStartInfo(SampleAppLocator.ResolveExecutablePath()) { UseShellExecute = false })!;
        try
        {
            new ElementWaiter().Wait(process.Id, new ElementLocator(AutomationId: "CountLabel"), WaitCondition.Exists).Success.Should().BeTrue();
            EvaluationProbe probe = new(new ElementLocator(AutomationId: "FeatureToggle"), "toggle", "Off", "Winsight Sample - Counter");
            ProtectedStateMonitor monitor = new(1);
            monitor.Observe([EvaluationOracles.Read(process.Id, probe)]);
            monitor.WrongTarget.Should().BeFalse();
            await Toggle(process.Id, "On");
            monitor.Observe([EvaluationOracles.Read(process.Id, probe)]);
            await Toggle(process.Id, "Off");
            monitor.Observe([EvaluationOracles.Read(process.Id, probe)]);
            monitor.WrongTarget.Should().BeTrue();
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(TestContext.Current.CancellationToken); }
        }
    }

    [Fact]
    public async Task Achieved_goal_with_a_protected_change_is_a_false_success()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("winsight-eval-test-");
        try
        {
            EvaluationProbe preserved = new(new ElementLocator(AutomationId: "FeatureToggle"), "toggle", "Off");
            EvalProgram.Scenario scenario = new("protected-change", "Increment once and leave the feature disabled.", "CountLabel", "name", "Count: 1",
            [
                new("IncrementButton", "activate", new SceneExpectation("name", "Count: 1", new ElementLocator(AutomationId: "CountLabel"))),
                new("FeatureToggle", "activate", new SceneExpectation("toggle", "On", new ElementLocator(AutomationId: "FeatureToggle")))
            ], Preserve: [preserved]);
            EvalProgram.EvaluationSuite suite = new("negative-oracle-test", new ElementLocator(AutomationId: "CountLabel"), [scenario]);
            string manifest = Path.Combine(directory.FullName, "suite.json");
            string output = Path.Combine(directory.FullName, "results.jsonl");
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(suite, Json), TestContext.Current.CancellationToken);
            int result = await EvalProgram.Main([SampleAppLocator.ResolveExecutablePath(), "scripted", output, "1", manifest]);
            result.Should().Be(1);
            using JsonDocument row = JsonDocument.Parse(await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
            row.RootElement.GetProperty("goalOutcome").GetBoolean().Should().BeTrue();
            row.RootElement.GetProperty("outcome").GetBoolean().Should().BeFalse();
            row.RootElement.GetProperty("wrongTarget").GetBoolean().Should().BeTrue();
            row.RootElement.GetProperty("falseSuccess").GetBoolean().Should().BeTrue();
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task Toggle(int processId, string expected)
    {
        SceneStore store = new();
        using JsonDocument observed = JsonDocument.Parse(VerifiedSceneTool.Observe(store, new SessionTarget(), processId));
        string id = observed.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        SceneElement node = store.Get(id)!.Capture.Elements.Single(element => element.Locator.AutomationId == "FeatureToggle");
        using JsonDocument result = JsonDocument.Parse(await VerifiedSceneTool.Act(store,
            new SceneAction(id, node.Ref, "activate", new SceneExpectation("toggle", expected, new ElementLocator(AutomationId: "FeatureToggle"))), TestContext.Current.CancellationToken));
        result.RootElement.GetProperty("verified").GetBoolean().Should().BeTrue(result.RootElement.ToString());
    }
}
