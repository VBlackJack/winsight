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
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Eval;
using EvalProgram = Winsight.Eval.Program;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class EvaluationTests
{
    [Fact]
    public void Protected_state_change_remains_a_violation_after_restoration()
    {
        ProtectedStateMonitor monitor = new(2);
        monitor.Observe([true, true]);
        monitor.Observe([true, false]);
        monitor.Observe([true, true]);
        monitor.WrongTarget.Should().BeTrue();
    }

    [Fact]
    public void Missing_read_cannot_become_a_measured_zero()
    {
        ProtectedStateMonitor monitor = new(1);
        monitor.Observe([true]);
        monitor.Observe([null]);
        monitor.Observe([true]);
        monitor.WrongTarget.Should().BeNull();
    }

    [Fact]
    public void Confirmed_violation_wins_over_missing_read()
    {
        ProtectedStateMonitor monitor = new(2);
        monitor.Observe([null, false]);
        monitor.WrongTarget.Should().BeTrue();
    }

    [Fact]
    public void No_probes_or_no_samples_are_unknown()
    {
        new ProtectedStateMonitor(0).WrongTarget.Should().BeNull();
        new ProtectedStateMonitor(1).WrongTarget.Should().BeNull();
    }

    [Fact]
    public void Changing_probe_count_is_refused()
    {
        ProtectedStateMonitor monitor = new(1);
        Action observe = () => monitor.Observe([true, true]);
        observe.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Partial_oracle_locator_is_refused()
    {
        Action validate = () => EvaluationOracles.Validate(new(new ElementLocator(NameContains: "Count"), "name", "Count: 1"));
        validate.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void External_suite_requires_protected_state()
    {
        EvalProgram.Scenario scenario = new("save", "Save a disposable note.", "Status", "name", "Saved", []);
        Action validate = () => EvalProgram.ValidateScenario(scenario, external: true);
        validate.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Incomplete_external_suite_cannot_fall_back_to_builtin_scenarios()
    {
        using JsonDocument json = JsonDocument.Parse("""{"name":"external","ready":{"automationId":"Ready"}}""");
        EvalProgram.EvaluationSuite suite = json.RootElement.Deserialize<EvalProgram.EvaluationSuite>(JsonSerializerOptions.Web)!;
        Action select = () => EvalProgram.SelectScenarios(suite);
        select.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Codex_usage_counts_input_and_output_without_double_counting_cache()
    {
        using JsonDocument json = JsonDocument.Parse("""{"type":"turn.completed","usage":{"input_tokens":100,"cached_input_tokens":80,"output_tokens":7}}""");
        CodexDecisionSource.ReadUsage(json.RootElement).Should().Be(107);
    }

    [Fact]
    public async Task Codex_metadata_notices_do_not_invalidate_a_completed_turn()
    {
        using StreamReader reader = Events("""
            {"type":"item.completed","item":{"type":"error","message":"Metadata missing; using fallback metadata."}}
            {"type":"turn.completed","usage":{"input_tokens":100,"output_tokens":7}}
            """);
        (await CodexDecisionSource.ReadEvents(reader, TestContext.Current.CancellationToken)).Should().Be(107);
    }

    [Fact]
    public async Task Codex_command_events_invalidate_decision_only_trials()
    {
        using StreamReader reader = Events("""{"type":"item.started","item":{"type":"command_execution"}}""");
        Func<Task> read = async () => await CodexDecisionSource.ReadEvents(reader, TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Codex_failed_turn_keeps_the_provider_failure_reason()
    {
        using StreamReader reader = Events("""{"type":"turn.failed","error":{"message":"Unsupported model."}}""");
        Func<Task> read = async () => await CodexDecisionSource.ReadEvents(reader, TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<InvalidDataException>().WithMessage("*Unsupported model*");
    }

    [Fact]
    public void Codex_expectation_uses_the_observed_locator_instead_of_model_supplied_metadata()
    {
        SceneCapture scene = Scene();
        EvalProgram.Decision decision = CodexDecisionSource.ParseDecision("""
            {"done":false,"target":"e1","action":"activate","text":null,"itemName":null,
             "expectKind":"name","expectTarget":"e2","expected":"Count: 1","windowTitle":null}
            """, "fresh-observation", scene, 42);
        decision.Action!.Observation.Should().Be("fresh-observation");
        decision.Action.Expect.Locator!.AutomationId.Should().Be("CountLabel");
        decision.Tokens.Should().Be(42);
    }

    [Fact]
    public void Unknown_codex_expectation_reference_is_refused()
    {
        Action parse = () => CodexDecisionSource.ParseDecision("""
            {"done":false,"target":"e1","action":"activate","text":null,"itemName":null,
             "expectKind":"name","expectTarget":"unknown","expected":"Count: 1","windowTitle":null}
            """, "fresh", Scene(), null);
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Trial_allowlist_refuses_keyboard_actions_even_on_an_allowed_control()
    {
        EvalProgram.Scenario scenario = new("port", "Change only the port.", "Port", "value", "2222", [],
            AllowedTargets: ["IncrementButton"], AllowedActions: ["set_value"]);
        SceneAction action = new("observation", "e1", "key_chord", new SceneExpectation("name", "Count: 1", new ElementLocator(AutomationId: "CountLabel")), Text: "Enter");
        Action validate = () => EvalProgram.ValidateTrialAction(scenario, action, Scene());
        validate.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Trial_allowlist_checks_additional_targets_instead_of_only_the_primary_target()
    {
        EvalProgram.Scenario scenario = new("select", "Select only an allowed item.", "Result", "name", "Selected", [],
            AllowedTargets: ["IncrementButton"], AllowedActions: ["select_many"]);
        SceneAction action = new("observation", "e1", "select_many", new SceneExpectation("name", "Selected", new ElementLocator(AutomationId: "Result")), Targets: ["e2"]);
        Action validate = () => EvalProgram.ValidateTrialAction(scenario, action, Scene());
        validate.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Trial_allowlist_refuses_unknown_observations()
    {
        EvalProgram.Scenario scenario = new("port", "Change only the port.", "Port", "value", "2222", [],
            AllowedTargets: ["Port"], AllowedActions: ["set_value"]);
        SceneAction action = new("unknown", "e1", "set_value", new SceneExpectation("value", "2222", new ElementLocator(AutomationId: "Port")), Text: "2222");
        Action validate = () => EvalProgram.ValidateTrialAction(scenario, action, null);
        validate.Should().Throw<InvalidDataException>();
    }

    private static SceneCapture Scene()
    {
        WindowBounds bounds = new(0, 0, 100, 100);
        WindowIdentity window = new(10, 20, bounds);
        return new SceneCapture([], [
            new SceneElement("e1", "uia", "Button", "Increment", null, true, true, false, null, bounds,
                ["activate"], window, new ElementLocator(AutomationId: "IncrementButton"), "button"),
            new SceneElement("e2", "uia", "Text", "Count: 0", null, true, true, false, null, bounds,
                [], window, new ElementLocator(AutomationId: "CountLabel"), "label")
        ], false, "uia");
    }

    private static StreamReader Events(string lines) => new(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(lines)));
}
