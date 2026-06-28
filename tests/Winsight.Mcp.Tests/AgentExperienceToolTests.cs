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
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class AgentExperienceToolTests
{
    [Fact]
    public void Guide_exposes_the_preferred_agent_loop()
    {
        string json = AgentExperienceTool.GetWinsightGuide();

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement data = document.RootElement.GetProperty("data");
        data.GetProperty("preferredTools").GetProperty("perceive").GetString()
            .Should().Be("observe");
        data.GetProperty("defaultLoop").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(step => step!.Contains("dryRun=true", StringComparison.Ordinal));
    }

    [Fact]
    public void Observe_without_target_returns_repairable_missing_target_error()
    {
        string json = AgentExperienceTool.Observe(
            new WindowDiscovery(),
            new AffordanceScanner(),
            new WindowCapture(),
            new SessionTarget(),
            new SessionMemory());

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("data").GetProperty("code").GetString().Should().Be("missing_target");
        root.GetProperty("data").GetProperty("suggestedFix").GetProperty("tool").GetString()
            .Should().Be("set_target");
    }

    [Fact]
    public void Session_memory_records_and_finds_observed_locators()
    {
        SessionMemory memory = new();
        UiInteractable element = new(
            ControlType: "Button",
            AutomationId: "IncrementButton",
            Name: "Increment",
            Value: null,
            ToggleState: null,
            IsSelected: null,
            IsEnabled: true,
            IsVisible: true,
            X: 1,
            Y: 2,
            Width: 10,
            Height: 20,
            WindowTitle: "Sample",
            RecommendedLocator: "automationId=\"IncrementButton\"");

        memory.RecordObservation(123, "Sample", new[] { element });

        IReadOnlyList<LocatorMemoryMatch> matches = memory.Find("increment", processId: 123);
        matches.Should().ContainSingle();
        matches[0].Entry.RecommendedLocator.Should().Be("automationId=\"IncrementButton\"");
    }

    [Fact]
    public void Click_and_wait_honors_global_dry_run_before_touching_uia()
    {
        string? before = Environment.GetEnvironmentVariable(ToolAccessPolicy.DryRunVariable);
        Environment.SetEnvironmentVariable(ToolAccessPolicy.DryRunVariable, "true");
        try
        {
            string json = AgentExperienceTool.ClickAndWait(
                new ActionInvoker(),
                new ElementWaiter(),
                new UiSnapshotter(new WindowDiscovery(), new AffordanceScanner()),
                new SessionTarget(),
                automationId: "DoesNotMatter",
                processId: Environment.ProcessId);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            root.GetProperty("success").GetBoolean().Should().BeTrue();
            root.GetProperty("data").GetProperty("dryRun").GetBoolean().Should().BeTrue();
            root.GetProperty("data").GetProperty("action").GetString().Should().Be("click_and_wait");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ToolAccessPolicy.DryRunVariable, before);
        }
    }
}
