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
using Microsoft.Extensions.DependencyInjection;
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class SceneDriverSeamTests
{
    private const long WindowHandleValue = 42;
    private const int StrictElementCount = 3;
    private const int DefaultMaximum = 200;
    private static readonly string[] PointerTrio = ["drag_to", "right_click", "double_click"];
    private static readonly string[] ActivateWithPointer = ["activate", "drag_to", "right_click", "double_click"];
    private static readonly WindowBounds SomeBounds = new(0, 0, 100, 20);

    private sealed class FakeSceneDriver(params SceneCapture[] captures) : ISceneDriver
    {
        private int _next;

        public SceneCapture Capture(int processId, string? title, int maximum, TimeSpan? timeBudget = null, TimeProvider? timeProvider = null) =>
            captures[Math.Min(_next++, captures.Length - 1)];

        public OperationResult Act(SceneElement node, SceneAction action, IReadOnlyList<SceneElement> additional, Action beforeDispatch) =>
            OperationResult.Ok();
    }

    private static SceneElement Make(string reference, string role, string name, IReadOnlyList<string> actions) =>
        new(reference, "uia", role, name, null, true, true, false, null, SomeBounds, actions,
            new WindowIdentity(Environment.ProcessId, WindowHandleValue, new WindowBounds(0, 0, 800, 600)),
            new ElementLocator(Name: name, ControlType: role), "id-" + reference);

    private static SceneCapture Scene(string statusText)
    {
        WindowDescriptor win = new(Environment.ProcessId, WindowHandleValue, "App", "app", true, 0, 0, 800, 600);
        List<SceneElement> elements = [];
        for (int i = 1; i <= StrictElementCount; i++)
        {
            elements.Add(Make("e" + i, "Button", "Button " + i, ActivateWithPointer));
        }
        elements.Add(Make("e" + (StrictElementCount + 1), "Text", statusText, PointerTrio));
        return new SceneCapture([win], elements, false, "uia");
    }

    private static (SceneServices Services, SceneStore.Entry Entry) Arrange(SceneCapture before, SceneCapture after)
    {
        SceneServices baseline = SceneServices.Default;
        SceneServices services = new(
            new FakeSceneDriver(before, after), baseline.Windows, baseline.ManagedState, baseline.ElementState,
            baseline.WebView2, baseline.Capture, baseline.Invoker, baseline.VisualDiffer, baseline.Ocr,
            baseline.Profiles, baseline.Memory);
        SceneStore store = new(services);
        SceneStore.Entry entry = VerifiedSceneTool.Capture(
            store, Environment.ProcessId, null, DefaultMaximum, actionableOnly: true, filterText: null, includeOcr: false);
        return (services, entry);
    }

    [Fact]
    public void Any_change_detects_a_static_text_rename_that_the_strict_scene_does_not_contain()
    {
        (SceneServices services, SceneStore.Entry entry) = Arrange(Scene("Idle"), Scene("Saved"));

        entry.LooseBaseline.Should().NotBeNull();
        entry.Capture.Elements.Should().NotContain(e => e.Role == "Text");
        VerifiedSceneTool.Check(services, entry, new SceneExpectation("any_change")).Passed.Should().BeTrue();
    }

    [Fact]
    public void Any_change_with_hint_detects_the_same_static_text_rename()
    {
        (SceneServices services, SceneStore.Entry entry) = Arrange(Scene("Idle"), Scene("Saved"));

        VerifiedSceneTool.CheckWithHint(services, entry, new SceneExpectation("any_change")).Passed.Should().BeTrue();
    }

    [Fact]
    public void Any_change_reports_no_change_for_an_identical_second_capture()
    {
        (SceneServices services, SceneStore.Entry entry) = Arrange(Scene("Idle"), Scene("Idle"));

        VerifiedSceneTool.Check(services, entry, new SceneExpectation("any_change")).Passed.Should().BeFalse();
    }

    [Fact]
    public void Any_change_with_hint_reports_no_change_for_an_identical_second_capture()
    {
        (SceneServices services, SceneStore.Entry entry) = Arrange(Scene("Idle"), Scene("Idle"));

        VerifiedSceneTool.CheckWithHint(services, entry, new SceneExpectation("any_change")).Passed.Should().BeFalse();
    }

    [Fact]
    public void Program_registrations_resolve_the_scene_store_and_scene_services()
    {
        ServiceCollection services = new();
        Program.RegisterServices(services);
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<SceneStore>().Should().NotBeNull();
        SceneServices scene = provider.GetRequiredService<SceneServices>();
        scene.Driver.Should().BeSameAs(provider.GetRequiredService<UiaSceneDriver>());
        provider.GetRequiredService<ISceneDriver>().Should().BeSameAs(scene.Driver);
    }
}
