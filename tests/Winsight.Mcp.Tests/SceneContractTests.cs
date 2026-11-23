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

using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class SceneContractTests
{
    [Fact]
    public async Task Dom_scene_refuses_an_unowned_endpoint()
    {
        using System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess();
        string response = await DomSceneTool.Observe(new SceneStore(), new SessionTarget(), 12345, "unowned", process.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(response);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetString().Should().Contain("not exposed");
    }

    private static readonly SceneCapture Empty = new([], [], false, "uia");
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Observation_expires_and_cannot_be_consumed_after_expiry()
    {
        Clock clock = new();
        SceneStore store = new(clock);
        SceneStore.Entry entry = store.Add(1, 2, null, Empty);
        clock.Now = clock.Now.AddMinutes(2);
        store.Get(entry.Id).Should().BeNull();
        store.Consume(entry.Id).Should().BeFalse();
    }

    [Fact]
    public void Observation_is_consumed_exactly_once()
    {
        SceneStore store = new();
        SceneStore.Entry entry = store.Add(1, 2, null, Empty);
        store.Consume(entry.Id).Should().BeTrue();
        store.Consume(entry.Id).Should().BeFalse();
        store.Get(entry.Id).Should().BeNull();
    }

    [Fact]
    public void Capacity_evicts_oldest()
    {
        Clock clock = new();
        SceneStore store = new(clock);
        SceneStore.Entry first = store.Add(1, 2, null, Empty);
        for (int i = 0; i < 16; i++) { clock.Now = clock.Now.AddSeconds(1); store.Add(1, 2, null, Empty); }
        store.Get(first.Id).Should().BeNull();
    }

    [Theory]
    [InlineData("set_value", null)]
    [InlineData("scroll", "sideways")]
    [InlineData("key_chord", "InvalidKey")]
    [InlineData("select_item", null)]
    [InlineData("drag_to", null)]
    public void Invalid_action_parameters_are_rejected_before_dispatch(string action, string? text)
    {
        SceneAction request = new("observation", "e1", action,
            new SceneExpectation("exists", Locator: new ElementLocator(AutomationId: "target")), Text: text);
        VerifiedSceneTool.Validate(request).Should().NotBeNull();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("managed")]
    [InlineData("window_open")]
    [InlineData("window_closed")]
    [InlineData("value")]
    [InlineData("selected")]
    public void Incomplete_expectation_is_rejected(string kind)
        => VerifiedSceneTool.Validate(new SceneAction("o", "e1", "activate", new SceneExpectation(kind))).Should().NotBeNull();

    [Fact]
    public void Indexed_or_partial_expectations_are_rejected()
        => VerifiedSceneTool.Validate(new SceneAction("o", "e1", "activate", new SceneExpectation("exists",
            Locator: new ElementLocator(AutomationId: "id", Index: 0)))).Should().NotBeNull();

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:foo")]
    public void Profile_names_cannot_escape_storage(string name)
    {
        ApplicationProfiles profiles = new();
        Action load = () => profiles.Load(name);
        load.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Unknown_observation_never_dispatches()
    {
        string response = await VerifiedSceneTool.Act(new SceneStore(), new SceneAction("missing", "e1", "activate",
            new SceneExpectation("exists", Locator: new ElementLocator(AutomationId: "x"))), TestContext.Current.CancellationToken);
        response.Should().Contain("\"dispatched\":false").And.Contain("stale");
    }
}
