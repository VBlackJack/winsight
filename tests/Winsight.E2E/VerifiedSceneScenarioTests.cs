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
using Winsight.Mcp;
using Winsight.Mcp.Tools;
using Winsight.Core.Modules.Vision;
using Xunit;

namespace Winsight.E2E;

[Trait("Category", "E2E")]
public sealed class VerifiedSceneScenarioTests
{
    [Fact]
    public async Task Managed_boolean_postcondition_uses_wire_boolean_spelling()
    {
        using Process process = Process.Start(new ProcessStartInfo(SampleAppLocator.ResolveExecutablePath()) { UseShellExecute = false })!;
        try
        {
            new ElementWaiter().Wait(process.Id, new ElementLocator(AutomationId: "FeatureToggle"), WaitCondition.Exists).Success.Should().BeTrue();
            SceneStore store = new();
            using JsonDocument observed = JsonDocument.Parse(VerifiedSceneTool.Observe(store, new SessionTarget(), process.Id));
            string id = observed.RootElement.GetProperty("data").GetProperty("id").GetString()!;
            SceneElement toggle = store.Get(id)!.Capture.Elements.Single(n => n.Locator.AutomationId == "FeatureToggle");
            using JsonDocument result = JsonDocument.Parse(await VerifiedSceneTool.Act(store, new SceneAction(id, toggle.Ref, "activate",
                new SceneExpectation("managed", "true", TypeName: "Winsight.Samples.SimpleCounter.ViewModels.CounterViewModel", FieldName: "_featureEnabled")),
                TestContext.Current.CancellationToken));
            result.RootElement.GetProperty("verified").GetBoolean().Should().BeTrue(result.RootElement.ToString());
        }
        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
    }

    [Fact]
    public async Task Profile_preview_execution_and_hash_invalidation_are_enforced()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("winsight-profile-");
        using Process process = Process.Start(new ProcessStartInfo(SampleAppLocator.ResolveExecutablePath()) { UseShellExecute = false })!;
        try
        {
            new ElementWaiter().Wait(process.Id, new ElementLocator(AutomationId: "CountLabel"), WaitCondition.Exists).Success.Should().BeTrue();
            ApplicationProfiles profiles = new(directory.FullName);
            SessionTarget target = new();
            ProfileStep[] steps = [new(new ElementLocator(AutomationId: "IncrementButton"), "activate",
                new SceneExpectation("name", "Count: 1", new ElementLocator(AutomationId: "CountLabel")))];
            using JsonDocument saved = JsonDocument.Parse(ApplicationProfileTool.Save(profiles, target, "counter", steps, process.Id));
            saved.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(saved.RootElement.ToString());
            using JsonDocument preview = JsonDocument.Parse(await ApplicationProfileTool.Run(profiles, new SceneStore(), target,
                "counter", processId: process.Id, cancellationToken: TestContext.Current.CancellationToken));
            preview.RootElement.GetProperty("data").GetProperty("dryRun").GetBoolean().Should().BeTrue();
            using JsonDocument ran = JsonDocument.Parse(await ApplicationProfileTool.Run(profiles, new SceneStore(), target,
                "counter", processId: process.Id, dryRun: false, cancellationToken: TestContext.Current.CancellationToken));
            ran.RootElement.GetProperty("verified").GetBoolean().Should().BeTrue(ran.RootElement.ToString());
            profiles.Save(profiles.Load("counter") with { Sha256 = "changed" });
            using JsonDocument invalid = JsonDocument.Parse(await ApplicationProfileTool.Run(profiles, new SceneStore(), target,
                "counter", processId: process.Id, dryRun: false, cancellationToken: TestContext.Current.CancellationToken));
            invalid.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            ElementState count = (ElementState)new ElementStateReader().Read(process.Id, new ElementLocator(AutomationId: "CountLabel")).Data!;
            count.Name.Should().Be("Count: 1");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
            string profilePath = Path.Combine(directory.FullName, "counter.json");
            if (File.Exists(profilePath)) File.Delete(profilePath);
            directory.Delete();
        }
    }

    private sealed class FixedGrounder(GroundedElement result) : IElementGrounder
    {
        public Task<GroundedElement> FindAsync(string screenshotPath, string query, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    [Fact]
    public async Task Annotated_capture_and_vision_reference_reject_changed_pixels()
    {
        using Process process = Process.Start(new ProcessStartInfo(SampleAppLocator.ResolveExecutablePath()) { UseShellExecute = false })!;
        try
        {
            new ElementWaiter().Wait(process.Id, new ElementLocator(AutomationId: "CountLabel"), WaitCondition.Exists).Success.Should().BeTrue();
            SceneStore store = new();
            using JsonDocument observed = JsonDocument.Parse(VerifiedSceneTool.Observe(store, new SessionTarget(), process.Id, "Winsight Sample - Counter"));
            string id = observed.RootElement.GetProperty("data").GetProperty("id").GetString()!;
            SceneStore.Entry scene = store.Get(id)!;
            SceneElement button = scene.Capture.Elements.Single(n => n.Locator.AutomationId == "IncrementButton");
            ModelContextProtocol.Protocol.CallToolResult captureResult = SceneScreenshotTool.Capture(store, id, button.Window.Handle);
            captureResult.IsError.Should().NotBeTrue();
            string captureJson = captureResult.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().First().Text;
            using JsonDocument screenshot = JsonDocument.Parse(captureJson);
            screenshot.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(screenshot.RootElement.ToString());
            File.Exists(screenshot.RootElement.GetProperty("data").GetProperty("annotatedPath").GetString()).Should().BeTrue();
            captureResult.Content.OfType<ModelContextProtocol.Protocol.ImageContentBlock>().Should().ContainSingle();
            FixedGrounder grounder = new(new GroundedElement(button.Bounds.Left - button.Window.Bounds.Left + button.Bounds.Width / 2,
                button.Bounds.Top - button.Window.Bounds.Top + button.Bounds.Height / 2, 10, 10, 0.99, "Fixture coordinates", "fixture"));
            using JsonDocument located = JsonDocument.Parse(await SceneVisionTool.Locate(store, grounder, id, button.Window.Handle, "increment", TestContext.Current.CancellationToken));
            located.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(located.RootElement.ToString());
            string visionId = located.RootElement.GetProperty("data").GetProperty("id").GetString()!;
            new ActionInvoker().Click(process.Id, button.Locator).Success.Should().BeTrue();
            new ElementWaiter().Wait(process.Id, new ElementLocator(Name: "Count: 1"), WaitCondition.Exists).Success.Should().BeTrue();
            using JsonDocument result = JsonDocument.Parse(await VerifiedSceneTool.Act(store, new SceneAction(visionId, "v1", "activate",
                new SceneExpectation("name", "Count: 2", new ElementLocator(AutomationId: "CountLabel"))), TestContext.Current.CancellationToken));
            result.RootElement.GetProperty("dispatched").GetBoolean().Should().BeFalse(result.RootElement.ToString());
        }
        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispatch_is_verified_once_or_remains_explicitly_unconfirmed(bool impossibleExpectation)
    {
        using Process process = Process.Start(new ProcessStartInfo(SampleAppLocator.ResolveExecutablePath()) { UseShellExecute = false })!;
        try
        {
            new ElementWaiter().Wait(process.Id, new ElementLocator(AutomationId: "CountLabel"), WaitCondition.Exists).Success.Should().BeTrue();
            SceneStore store = new();
            SessionTarget target = new();
            using JsonDocument observation = JsonDocument.Parse(VerifiedSceneTool.Observe(store, target, process.Id, "Winsight Sample - Counter"));
            string id = observation.RootElement.GetProperty("data").GetProperty("id").GetString()!;
            SceneStore.Entry scene = store.Get(id)!;
            SceneElement button = scene.Capture.Elements.Single(n => n.Locator.AutomationId == "IncrementButton");
            SceneAction request = new(id, button.Ref, "activate", new SceneExpectation("name", impossibleExpectation ? "Count: 999" : "Count: 1",
                new ElementLocator(AutomationId: "CountLabel")), TimeoutMs: 1000);
            using JsonDocument preview = JsonDocument.Parse(await VerifiedSceneTool.Act(store, request with { DryRun = true }, TestContext.Current.CancellationToken));
            preview.RootElement.GetProperty("dispatched").GetBoolean().Should().BeFalse();
            store.Get(id).Should().NotBeNull();
            using JsonDocument result = JsonDocument.Parse(await VerifiedSceneTool.Act(store, request, TestContext.Current.CancellationToken));
            result.RootElement.GetProperty("dispatched").GetBoolean().Should().BeTrue(result.RootElement.ToString());
            result.RootElement.GetProperty("verified").GetBoolean().Should().Be(!impossibleExpectation, result.RootElement.ToString());
            using JsonDocument repeated = JsonDocument.Parse(await VerifiedSceneTool.Act(store, request, TestContext.Current.CancellationToken));
            repeated.RootElement.GetProperty("dispatched").GetBoolean().Should().BeFalse();
            ElementState count = (ElementState)new ElementStateReader().Read(process.Id, new ElementLocator(AutomationId: "CountLabel")).Data!;
            count.Name.Should().Be("Count: 1");
        }
        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
    }

    [Fact]
    public void Changed_toggle_state_is_rejected_before_dispatch()
    {
        using Process process = Process.Start(new ProcessStartInfo(SampleAppLocator.ResolveExecutablePath()) { UseShellExecute = false })!;
        try
        {
            new ElementWaiter().Wait(process.Id, new ElementLocator(AutomationId: "FeatureToggle"), WaitCondition.Exists).Success.Should().BeTrue();
            UiaSceneDriver driver = new();
            SceneElement toggle = driver.Capture(process.Id, "Winsight Sample - Counter", 200).Elements.Single(n => n.Locator.AutomationId == "FeatureToggle");
            new ActionInvoker().Click(process.Id, toggle.Locator).Success.Should().BeTrue();
            bool dispatched = false;
            OperationResult result = driver.Act(toggle, new SceneAction("o", toggle.Ref, "activate", new SceneExpectation("exists")), [], () => dispatched = true);
            result.Success.Should().BeFalse();
            dispatched.Should().BeFalse();
        }
        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
    }
}
