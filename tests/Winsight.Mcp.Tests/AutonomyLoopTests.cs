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
using ModelContextProtocol.Protocol;
using Winsight.Core.Models;
using Winsight.Core.Modules.Lifecycle;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class AutonomyLoopTests
{
    [Theory]
    [InlineData("any_change")]
    [InlineData("visual_change")]
    public void Exploratory_scene_expectations_validate_without_locator(string kind)
    {
        SceneAction request = new("obs", "e1", "activate", new SceneExpectation(kind), ObserveAfter: true);
        VerifiedSceneTool.Validate(request).Should().BeNull();
    }

    [Theory]
    [InlineData("value_contains")]
    [InlineData("name_contains")]
    public void Contains_expectations_require_locator_and_expected(string kind)
    {
        SceneAction valid = new("obs", "e1", "activate",
            new SceneExpectation(kind, Expected: "Saved", Locator: new ElementLocator(AutomationId: "Status")));
        VerifiedSceneTool.Validate(valid).Should().BeNull();

        SceneAction missingExpected = new("obs", "e1", "activate",
            new SceneExpectation(kind, Locator: new ElementLocator(AutomationId: "Status")));
        VerifiedSceneTool.Validate(missingExpected).Should().NotBeNull();
    }

    [Fact]
    public void Saved_app_profiles_reject_exploratory_expectations()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"winsight-prof-{Guid.NewGuid():N}");
        try
        {
            ApplicationProfiles profiles = new(tempDir);
            using Process self = Process.GetCurrentProcess();
            ProfileStep exploratoryStep = new(
                new ElementLocator(AutomationId: "Btn"),
                "activate",
                new SceneExpectation("any_change"));

            string json = ApplicationProfileTool.Save(profiles, new SessionTarget(), "demo", [exploratoryStep], self.Id, dryRun: true);
            using JsonDocument doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            doc.RootElement.GetProperty("error").GetString().Should().Contain("exploratory");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Compute_diff_detects_added_removed_and_changed_elements()
    {
        WindowIdentity win = new(1, 100, new WindowBounds(0, 0, 800, 600));
        SceneElement keepSame = new("e1", "uia", "Button", "OK", null, true, true, false, null, new WindowBounds(0, 0, 10, 10), ["activate"], win, new ElementLocator(AutomationId: "Ok"), "id-1");
        SceneElement willChange = new("e2", "uia", "Text", "Count: 0", null, true, true, false, null, new WindowBounds(0, 0, 10, 10), [], win, new ElementLocator(AutomationId: "Count"), "id-2");
        SceneElement willRemove = new("e3", "uia", "Button", "Cancel", null, true, true, false, null, new WindowBounds(0, 0, 10, 10), ["activate"], win, new ElementLocator(AutomationId: "Cancel"), "id-3");

        SceneElement changed = willChange with { Name = "Count: 1" };
        SceneElement added = new("e4", "uia", "Text", "Saved", null, true, true, false, null, new WindowBounds(0, 0, 10, 10), [], win, new ElementLocator(AutomationId: "Saved"), "id-4");

        SceneCapture before = new([], [keepSame, willChange, willRemove], false, "uia");
        SceneCapture after = new([], [keepSame, changed, added], false, "uia");

        VerifiedSceneTool.SceneDiff diff = VerifiedSceneTool.ComputeDiff(before, after);
        diff.HasChanges.Should().BeTrue();
        diff.ElementsAdded.Should().ContainSingle(x => x.Contains("Saved"));
        diff.ElementsRemoved.Should().ContainSingle(x => x.Contains("Cancel"));
        diff.ElementsChanged.Should().ContainSingle(x => x.Contains("Count: 1"));

        VerifiedSceneTool.ComputeDiff(before, before).HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Capture_scene_returns_structured_error_result_when_observation_or_window_missing()
    {
        SceneStore store = new();
        CallToolResult expired = SceneScreenshotTool.Capture(store, "missing-obs");
        expired.IsError.Should().BeTrue();
        expired.Content.OfType<TextContentBlock>().First().Text.Should().Contain("Observation expired");

        using Process self = Process.GetCurrentProcess();
        WindowDescriptor win = new(self.Id, 99999, "Main", self.ProcessName, true, 0, 0, 800, 600, WindowClassifier.KindMain, "MainClass");
        SceneStore.Entry entry = store.Add(self.Id, self.StartTime.ToUniversalTime().Ticks, null, new SceneCapture([win], [], false, "uia"));

        CallToolResult unknownHandle = SceneScreenshotTool.Capture(store, entry.Id, windowHandle: 12345);
        unknownHandle.IsError.Should().BeTrue();
        unknownHandle.Content.OfType<TextContentBlock>().First().Text.Should().Contain("99999");
    }

    [Fact]
    public void List_and_delete_app_profiles_manage_saved_workflows()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"winsight-prof-{Guid.NewGuid():N}");
        try
        {
            ApplicationProfiles profiles = new(tempDir);
            ProfileStep step = new(
                new ElementLocator(AutomationId: "IncrementButton"),
                "activate",
                new SceneExpectation("name", "Count: 1", new ElementLocator(AutomationId: "CountLabel")));
            profiles.Save(new ApplicationProfile("counter-inc", @"C:\app.exe", "1.0.0", "ABC", [step]));

            string listedJson = ApplicationProfileTool.List(profiles);
            using (JsonDocument doc = JsonDocument.Parse(listedJson))
            {
                doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
                JsonElement items = doc.RootElement.GetProperty("data");
                items.GetArrayLength().Should().Be(1);
                items[0].GetProperty("name").GetString().Should().Be("counter-inc");
                items[0].GetProperty("stepCount").GetInt32().Should().Be(1);
            }

            string deletedJson = ApplicationProfileTool.Delete(profiles, "counter-inc");
            using (JsonDocument doc = JsonDocument.Parse(deletedJson))
            {
                doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("data").GetProperty("deleted").GetBoolean().Should().BeTrue();
            }

            profiles.List().Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Close_app_clears_session_target_when_closing_already_exited_target()
    {
        SessionTarget target = new();
        // Use a non-existent PID; Close reports alreadyExited=true (success) and clears the bound target.
        const int missingPid = 999_999_991;
        target.Set(missingPid);
        target.ProcessId.Should().Be(missingPid);

        string json = LifecycleTool.CloseApp(new AppLauncher(new WindowDiscovery()), target);
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        target.ProcessId.Should().BeNull();
    }

    [Fact]
    public void Session_memory_detects_repeated_failure_loops_and_resets_on_different_code()
    {
        SessionMemory memory = new();
        memory.RecordFailure("act_verified", "stale", "stale 1", "observe_scene").Should().BeNull();
        LoopWarning? second = memory.RecordFailure("act_verified", "stale", "stale 2", "observe_scene");
        second.Should().NotBeNull();
        second!.ConsecutiveFailures.Should().Be(2);
        second.PivotSuggestion.Should().Contain("capture_scene");

        // A different code resets the consecutive streak.
        memory.RecordFailure("act_verified", "user_active", "busy", "wait_for_user_idle").Should().BeNull();
    }

    [Fact]
    public void Enrich_capture_filters_actionable_elements_text_and_emits_sparse_surface_hint()
    {
        using Process self = Process.GetCurrentProcess();
        WindowDescriptor win = new(self.Id, 42, "CustomApp", self.ProcessName, true, 0, 0, 800, 600);
        WindowIdentity winId = new(self.Id, 42, new WindowBounds(0, 0, 800, 600));
        SceneElement label = new("e1", "uia", "Text", "Static Label", null, true, true, false, null, new WindowBounds(0, 0, 100, 20), [], winId, new ElementLocator(AutomationId: "Lbl"), "1");
        SceneElement button = new("e2", "uia", "Button", "Save Project", null, true, true, false, null, new WindowBounds(0, 30, 100, 30), ["activate"], winId, new ElementLocator(AutomationId: "SaveBtn"), "2");

        SceneCapture raw = new([win], [label, button], false, "uia");
        SceneCapture enriched = VerifiedSceneTool.EnrichCapture(
            SceneServices.Default, self.Id, null, raw, actionableOnly: true, filterText: "save");

        enriched.Elements.Should().ContainSingle(e => e.Ref == "e2");
        enriched.SurfaceHint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Application_profiles_auto_matches_running_executable_fingerprint()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"winsight-match-{Guid.NewGuid():N}");
        try
        {
            ApplicationProfiles profiles = new(tempDir);
            using Process self = Process.GetCurrentProcess();
            (string path, string version, string hash) = ApplicationProfiles.Fingerprint(self.Id);
            ProfileStep step = new(
                new ElementLocator(AutomationId: "Ok"),
                "activate",
                new SceneExpectation("exists", Locator: new ElementLocator(AutomationId: "Ok")));
            profiles.Save(new ApplicationProfile("self-match", path, version, hash, [step]));
            profiles.Save(new ApplicationProfile("other-app", @"C:\other.exe", "9.9.9", "DEAD", [step]));

            IReadOnlyList<string> matches = profiles.FindMatchingNames(self.Id);
            matches.Should().ContainSingle().Which.Should().Be("self-match");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
