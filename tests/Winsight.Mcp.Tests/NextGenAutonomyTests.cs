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
using Winsight.Mcp.Tools;
using Winsight.Setup;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class NextGenAutonomyTests
{
    [Fact]
    public void Session_target_supports_named_aliases_and_switching()
    {
        SessionTarget target = new();
        target.Set(1001, "source");
        target.Set(2002, "app");

        target.ProcessId.Should().Be(2002);
        target.Aliases.Should().ContainKey("source").WhoseValue.Should().Be(1001);
        target.Aliases.Should().ContainKey("app").WhoseValue.Should().Be(2002);

        target.TrySwitch("source", out int pid, out string err).Should().BeTrue();
        err.Should().BeEmpty();
        pid.Should().Be(1001);
        target.ProcessId.Should().Be(1001);

        target.TrySwitch("missing", out _, out string missingErr).Should().BeFalse();
        missingErr.Should().Contain("source").And.Contain("app");

        target.Clear("source");
        target.Aliases.Should().NotContainKey("source");
        target.ProcessId.Should().Be(1001);

        target.Clear();
        target.ProcessId.Should().BeNull();
        target.Aliases.Should().BeEmpty();
    }

    [Fact]
    public async Task Act_verified_background_only_blocks_pointer_and_focus_actions_without_consuming_observation()
    {
        using Process self = Process.GetCurrentProcess();
        SceneStore store = new();
        WindowIdentity window = new(self.Id, 1, new WindowBounds(0, 0, 200, 200));
        SceneElement element = new(
            "e1", "uia", "Button", "Go", null, true, true, false, null,
            new WindowBounds(10, 10, 40, 20), ["activate", "right_click", "focus", "key_chord"], window,
            new ElementLocator(AutomationId: "GoBtn"), "1.2.3");
        SceneStore.Entry entry = store.Add(
            self.Id,
            self.StartTime.ToUniversalTime().Ticks,
            null,
            new SceneCapture([], [element], false, "uia"));

        SceneAction pointerReq = new(
            entry.Id, "e1", "right_click",
            new SceneExpectation("any_change"),
            BackgroundOnly: true);
        string response = await VerifiedSceneTool.Act(store, pointerReq, TestContext.Current.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(response);
        doc.RootElement.GetProperty("status").GetString().Should().Be("blocked");
        doc.RootElement.GetProperty("dispatched").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("message").GetString().Should().Contain("backgroundOnly");
        store.Get(entry.Id).Should().NotBeNull();
    }

    [Fact]
    public void Accessibility_auditor_flags_missing_names_ids_zero_size_focus_and_overlaps()
    {
        WindowIdentity win = new(1, 100, new WindowBounds(0, 0, 800, 600));
        SceneElement good = new("e1", "uia", "Button", "Save", null, true, true, false, null, new WindowBounds(10, 10, 80, 30), ["activate", "focus"], win, new ElementLocator(AutomationId: "SaveBtn"), "1");
        SceneElement unnamedNoId = new("e2", "uia", "Button", "", null, true, true, false, null, new WindowBounds(100, 10, 80, 30), ["activate"], win, new ElementLocator(), "2");
        SceneElement overlapping = new("e3", "uia", "Button", "Cancel", null, true, true, false, null, new WindowBounds(105, 12, 80, 30), ["activate", "focus"], win, new ElementLocator(AutomationId: "CancelBtn"), "3");
        SceneElement zeroSize = new("e4", "uia", "CheckBox", "Agree", null, true, true, false, null, new WindowBounds(0, 0, 0, 0), ["activate", "focus"], win, new ElementLocator(AutomationId: "AgreeChk"), "4");

        UiAccessibilityAuditReport report = UiAccessibilityAuditor.Audit(new SceneCapture([], [good, unnamedNoId, overlapping, zeroSize], false, "uia"));
        report.InteractiveElements.Should().Be(4);
        report.Findings.Should().Contain(f => f.Rule == "missing_accessible_name" && f.ElementRef == "e2");
        report.Findings.Should().Contain(f => f.Rule == "missing_automation_id" && f.ElementRef == "e2");
        report.Findings.Should().Contain(f => f.Rule == "not_keyboard_focusable" && f.ElementRef == "e2");
        report.Findings.Should().Contain(f => f.Rule == "zero_size_visible" && f.ElementRef == "e4");
        report.Findings.Should().Contain(f => f.Rule == "overlapping_interactables" && f.ElementRef == "e2+e3");
        report.Score.Should().BeLessThan(100);
    }

    [Fact]
    public async Task Watch_ui_events_validates_duration_and_max_events_bounds()
    {
        using Process self = Process.GetCurrentProcess();
        string badDuration = await WatchUiEventsTool.WatchUiEvents(
            new UiEventWatcher(), new SessionTarget(), durationMs: 10, processId: self.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        using (JsonDocument doc = JsonDocument.Parse(badDuration))
        {
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            doc.RootElement.GetProperty("error").GetString().Should().Contain("durationMs");
        }

        string badMax = await WatchUiEventsTool.WatchUiEvents(
            new UiEventWatcher(), new SessionTarget(), maxEvents: 0, processId: self.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        using (JsonDocument doc = JsonDocument.Parse(badMax))
        {
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            doc.RootElement.GetProperty("error").GetString().Should().Contain("maxEvents");
        }
    }

    [Fact]
    public void Setup_client_configuration_writes_optional_autonomy_env_for_json_and_toml()
    {
        Dictionary<string, string> env = Winsight.Setup.Program.ParseEnvironmentOptions(["--compact", "--yield-ms", "3000"]);
        env.Should().ContainKey("WINSIGHT_TOOL_PROFILE").WhoseValue.Should().Be("compact");
        env.Should().ContainKey("WINSIGHT_YIELD_TO_USER_MS").WhoseValue.Should().Be("3000");

        string json = ClientConfiguration.Transform("{}", @"C:\Winsight\winsight-mcp.exe", toml: false, environment: env);
        using (JsonDocument doc = JsonDocument.Parse(json))
        {
            JsonElement serverEnv = doc.RootElement.GetProperty("mcpServers").GetProperty("winsight").GetProperty("env");
            serverEnv.GetProperty("WINSIGHT_TOOL_PROFILE").GetString().Should().Be("compact");
            serverEnv.GetProperty("WINSIGHT_YIELD_TO_USER_MS").GetString().Should().Be("3000");
        }

        string toml = ClientConfiguration.Transform(string.Empty, @"C:\Winsight\winsight-mcp.exe", toml: true, environment: env);
        toml.Should().Contain("WINSIGHT_TOOL_PROFILE = \"compact\"");
        toml.Should().Contain("WINSIGHT_YIELD_TO_USER_MS = \"3000\"");
    }

    [Fact]
    public void Save_verified_history_as_profile_promotes_recorded_steps()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"winsight-hist-{Guid.NewGuid():N}");
        try
        {
            ApplicationProfiles profiles = new(tempDir);
            SessionMemory memory = new();
            using Process self = Process.GetCurrentProcess();
            memory.RecordVerifiedStep(self.Id, new ProfileStep(
                new ElementLocator(AutomationId: "OkBtn"),
                "activate",
                new SceneExpectation("name", "Done", new ElementLocator(AutomationId: "Status"))));

            string json = ApplicationProfileTool.SaveVerifiedHistory(profiles, new SessionTarget(), memory, "recorded-flow", self.Id);
            using JsonDocument doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            profiles.List().Should().ContainSingle(p => p.Name == "recorded-flow" && p.StepCount == 1);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Wait_any_validates_branches_and_matches_window_closed_immediately()
    {
        WaitAnyTool.ValidateBranch(new WaitAnyBranch("unknown")).Should().NotBeNull();
        WaitAnyTool.ValidateBranch(new WaitAnyBranch("window_open")).Should().NotBeNull();
        WaitAnyTool.ValidateBranch(new WaitAnyBranch("ocr_contains", ExpectedText: "Done")).Should().BeNull();

        using Process self = Process.GetCurrentProcess();
        string json = await WaitAnyTool.WaitAny(
            new SceneStore(),
            new SessionTarget(),
            [
                new WaitAnyBranch("window_open", Label: "never", WindowTitle: "__nonexistent_title_xyz__"),
                new WaitAnyBranch("window_closed", Label: "already-gone", WindowTitle: "__nonexistent_title_xyz__"),
            ],
            timeoutMs: 1000,
            processId: self.Id,
            cancellationToken: TestContext.Current.CancellationToken);

        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("data").GetProperty("matchedIndex").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("data").GetProperty("label").GetString().Should().Be("already-gone");
    }

    [Fact]
    public void Capture_scene_crop_rectangle_clamps_to_image_bounds()
    {
        WindowDescriptor window = new(1, 10, "App", "App", true, 100, 100, 400, 300);
        System.Drawing.Rectangle rect = SceneScreenshotTool.ComputeCropRectangle(
            new WindowBounds(105, 105, 40, 20), window, 400, 300, paddingPx: 24);
        rect.X.Should().Be(0);
        rect.Y.Should().Be(0);
        rect.Right.Should().Be(69);
        rect.Bottom.Should().Be(49);
    }
}
