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

    [Fact]
    public async Task Run_verified_steps_validates_and_previews_in_dry_run()
    {
        using Process self = Process.GetCurrentProcess();
        VerifiedBatchStep[] steps =
        [
            new(new ElementLocator(AutomationId: "NameBox"), "set_value", new SceneExpectation("value", "Alice", new ElementLocator(AutomationId: "NameBox")), Text: "Alice"),
            new(new ElementLocator(AutomationId: "SaveBtn"), "activate", new SceneExpectation("any_change")),
        ];

        string json = await VerifiedBatchTool.RunVerifiedSteps(
            new SceneStore(),
            new SessionTarget(),
            steps,
            processId: self.Id,
            dryRun: true,
            cancellationToken: TestContext.Current.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("status").GetString().Should().Be("preview");
        doc.RootElement.GetProperty("dryRun").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("total").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Run_verified_steps_rejects_ambiguous_or_partial_target_locators_before_dispatch()
    {
        using Process self = Process.GetCurrentProcess();
        VerifiedBatchStep[] invalidSteps =
        [
            new(new ElementLocator(NameContains: "Save"), "activate", new SceneExpectation("any_change")),
        ];

        string json = await VerifiedBatchTool.RunVerifiedSteps(
            new SceneStore(),
            new SessionTarget(),
            invalidSteps,
            processId: self.Id,
            dryRun: false,
            cancellationToken: TestContext.Current.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("status").GetString().Should().Be("blocked");
        doc.RootElement.GetProperty("failedAtIndex").GetInt32().Should().Be(0);
    }

    [Fact]
    public void File_dialog_picker_prefers_standard_32770_dialog_or_explicit_title()
    {
        WindowDescriptor main = new(1, 10, "MyApp", "MyApp", false, 0, 0, 800, 600, WindowClassifier.KindMain, "HwndWrapper");
        WindowDescriptor openDialog = new(1, 20, "Open", "MyApp", true, 100, 100, 500, 400, WindowClassifier.KindDialog, "#32770", OwnerHandle: 10, IsModal: true);
        WindowDescriptor customModal = new(1, 30, "Export Report", "MyApp", false, 120, 120, 400, 300, WindowClassifier.KindDialog, "CustomDlg", OwnerHandle: 10, IsModal: true);

        FileDialogTool.PickDialog([main, customModal, openDialog], null)!.WindowHandle.Should().Be(20);
        FileDialogTool.PickDialog([main, customModal, openDialog], "Export")!.WindowHandle.Should().Be(30);
        FileDialogTool.PickDialog([main], null).Should().BeNull();
    }

    [Fact]
    public void Select_file_dialog_respects_dry_run_and_validates_arguments()
    {
        string? priorDryRun = Environment.GetEnvironmentVariable(ToolAccessPolicy.DryRunVariable);
        Environment.SetEnvironmentVariable(ToolAccessPolicy.DryRunVariable, "1");
        try
        {
            using Process self = Process.GetCurrentProcess();
            string emptyPath = FileDialogTool.SelectFileDialog(new WindowDiscovery(), new ActionInvoker(), new SessionTarget(), "   ", processId: self.Id);
            using (JsonDocument doc = JsonDocument.Parse(emptyPath))
            {
                doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            }

            string dry = FileDialogTool.SelectFileDialog(new WindowDiscovery(), new ActionInvoker(), new SessionTarget(), @"C:\temp\report.txt", processId: self.Id);
            using (JsonDocument doc = JsonDocument.Parse(dry))
            {
                doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("data").GetProperty("dryRun").GetBoolean().Should().BeTrue();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(ToolAccessPolicy.DryRunVariable, priorDryRun);
        }
    }

    [Fact]
    public void Filtered_observation_followed_by_noop_does_not_verify_any_change()
    {
        using Process self = Process.GetCurrentProcess();
        WindowDescriptor win = new(self.Id, 100, "VMware Workstation", self.ProcessName, true, 0, 0, 1200, 800, WindowClassifier.KindMain, "VMUIFrame");
        WindowIdentity winId = new(self.Id, 100, new WindowBounds(0, 0, 1200, 800));

        List<SceneElement> rawBefore = new();
        // 5 elements matching filterText="VM" and actionableOnly=true
        for (int i = 1; i <= 5; i++)
        {
            rawBefore.Add(new SceneElement(
                $"e{i}", "uia", "MenuItem", i == 1 ? "VM" : $"VM Action {i}", null,
                true, true, false, null, new WindowBounds(10 * i, 30, 50, 20), ["activate"], winId,
                new ElementLocator(Name: i == 1 ? "VM" : $"VM Action {i}", ControlType: "MenuItem"), $"vm-{i}"));
        }
        // 46 other elements (including File menu item)
        rawBefore.Add(new SceneElement(
            "e6", "uia", "MenuItem", "File", null,
            true, true, false, null, new WindowBounds(5, 30, 40, 20), ["activate"], winId,
            new ElementLocator(Name: "File", ControlType: "MenuItem"), "file-1"));
        for (int i = 7; i <= 51; i++)
        {
            rawBefore.Add(new SceneElement(
                $"e{i}", "uia", "Button", $"Toolbar {i}", null,
                true, true, false, null, new WindowBounds(10 * i, 60, 40, 20), ["activate"], winId,
                new ElementLocator(Name: $"Toolbar {i}", ControlType: "Button"), $"btn-{i}"));
        }

        SceneCaptureFilter filter = new(WindowTitle: "VMware Workstation", MaxResults: 40, ActionableOnly: true, FilterText: "VM", IncludeOcr: false);
        SceneCapture beforeFiltered = VerifiedSceneTool.EnrichCapture(
            SceneServices.Default, self.Id, filter.WindowTitle,
            new SceneCapture([win], rawBefore, false, "uia"),
            filter.ActionableOnly, filter.FilterText, filter.IncludeOcr);
        beforeFiltered.Elements.Should().HaveCount(5);

        // No-op action: only focus moved to the unrelated "File" menu item in the 51-element tree.
        List<SceneElement> rawAfter = rawBefore
            .Select(e => e.Identity == "file-1" ? e with { Focused = true } : e)
            .ToList();
        SceneCapture afterRaw = new([win], rawAfter, false, "uia");

        (bool passed, VerifiedSceneTool.SceneDiff diff) = VerifiedSceneTool.EvaluateAnyChange(
            SceneServices.Default, self.Id, beforeFiltered, afterRaw, filter);

        passed.Should().BeFalse();
        diff.HasChanges.Should().BeFalse();
        diff.ElementsAdded.Should().BeEmpty();
        diff.ElementsRemoved.Should().BeEmpty();
        diff.ElementsChanged.Should().BeEmpty();
    }

    [Fact]
    public void Filtered_observe_scene_sets_truncated_only_when_results_are_actually_cut()
    {
        using Process self = Process.GetCurrentProcess();
        WindowDescriptor win = new(self.Id, 100, "VMware Workstation", self.ProcessName, true, 0, 0, 1200, 800);
        WindowIdentity winId = new(self.Id, 100, new WindowBounds(0, 0, 1200, 800));

        List<SceneElement> rawElements = new();
        for (int i = 1; i <= 51; i++)
        {
            string name = i <= 5 ? $"VM Item {i}" : $"Other Item {i}";
            rawElements.Add(new SceneElement(
                $"e{i}", "uia", "Button", name, null,
                true, true, false, null, new WindowBounds(10, 10 * i, 80, 20), ["activate"], winId,
                new ElementLocator(Name: name, ControlType: "Button"), $"id-{i}"));
        }

        // Filtered capture traverses up to MaxTraversalNodes so maxResults=40 does not cut before filtering.
        SceneCaptureFilter filtered = new(MaxResults: 40, ActionableOnly: true, FilterText: "VM");
        VerifiedSceneTool.CaptureLimitFor(filtered).Should().Be(UiaDefaults.MaxTraversalNodes);
        VerifiedSceneTool.CaptureLimitFor(new SceneCaptureFilter(MaxResults: 40)).Should().Be(40);

        // 5 matching elements out of maxResults=40 => not truncated.
        SceneCapture underCap = VerifiedSceneTool.EnrichCapture(
            SceneServices.Default, self.Id, null,
            new SceneCapture([win], rawElements, false, "uia"),
            actionableOnly: true, filterText: "VM", maxResults: 40);
        underCap.Elements.Should().HaveCount(5);
        underCap.Truncated.Should().BeFalse();

        // 5 matching elements with maxResults=3 => cut to 3 and truncated=true.
        SceneCapture overCap = VerifiedSceneTool.EnrichCapture(
            SceneServices.Default, self.Id, null,
            new SceneCapture([win], rawElements, false, "uia"),
            actionableOnly: true, filterText: "VM", maxResults: 3);
        overCap.Elements.Should().HaveCount(3);
        overCap.Truncated.Should().BeTrue();

        // If the underlying UIA traversal itself was truncated, truncated stays true.
        SceneCapture sourceTruncated = VerifiedSceneTool.EnrichCapture(
            SceneServices.Default, self.Id, null,
            new SceneCapture([win], rawElements, true, "uia"),
            actionableOnly: true, filterText: "VM", maxResults: 40);
        sourceTruncated.Elements.Should().HaveCount(5);
        sourceTruncated.Truncated.Should().BeTrue();
    }

    [Fact]
    public void MenuItem_activate_any_change_ignores_unrelated_changes_and_verifies_window_popup_or_expand()
    {
        WindowDescriptor mainWin = new(1, 100, "VMware Workstation", "vmware", true, 0, 0, 1200, 800, WindowClassifier.KindMain, "VMUIFrame");
        WindowIdentity winId = new(1, 100, new WindowBounds(0, 0, 1200, 800));

        SceneElement vmMenu = new("e1", "uia", "MenuItem", "VM", null, true, true, false, null,
            new WindowBounds(100, 30, 50, 20), ["activate"], winId, new ElementLocator(Name: "VM", ControlType: "MenuItem"), "menu-vm",
            ExpandCollapseState: "Collapsed");
        SceneElement fileMenu = new("e2", "uia", "MenuItem", "File", null, true, true, false, null,
            new WindowBounds(10, 30, 40, 20), ["activate"], winId, new ElementLocator(Name: "File", ControlType: "MenuItem"), "menu-file");
        SceneElement statusBar = new("e3", "uia", "Text", "Ready", null, true, true, false, null,
            new WindowBounds(0, 780, 300, 20), [], winId, new ElementLocator(Name: "Ready", ControlType: "Text"), "status-1");

        // Filtered source observation contained only "VM", while unfiltered baseline recorded all 3 keys.
        SceneCapture beforeFiltered = new([mainWin], [vmMenu], false, "uia");
        HashSet<string> unfilteredKeys = [VerifiedSceneTool.ElementKeyFor(vmMenu), VerifiedSceneTool.ElementKeyFor(fileMenu), VerifiedSceneTool.ElementKeyFor(statusBar)];

        // Case 1: UIA Invoke moved focus to File and an unrelated status text changed, but no menu opened.
        SceneCapture afterUnrelated = new([mainWin],
            [vmMenu, fileMenu with { Focused = true }, statusBar with { Name = "Updated 21:45" }], false, "uia");
        (bool passedUnrelated, VerifiedSceneTool.SceneDiff diffUnrelated) = VerifiedSceneTool.EvaluateMenuItemAnyChange(
            beforeFiltered, afterUnrelated, vmMenu, unfilteredKeys);
        passedUnrelated.Should().BeFalse();
        diffUnrelated.HasChanges.Should().BeFalse();

        // Case 2: A popup window opened (#32768 menu).
        WindowDescriptor popupWin = new(1, 200, "", "vmware", true, 100, 50, 180, 260, WindowClassifier.KindPopup, "#32768");
        SceneCapture afterWindow = new([mainWin, popupWin], [vmMenu, fileMenu, statusBar], false, "uia");
        (bool passedWindow, VerifiedSceneTool.SceneDiff diffWindow) = VerifiedSceneTool.EvaluateMenuItemAnyChange(
            beforeFiltered, afterWindow, vmMenu, unfilteredKeys);
        passedWindow.Should().BeTrue();
        diffWindow.WindowsAdded.Should().HaveCount(1);

        // Case 3: New popup MenuItem elements appeared (even though "Power" would not match filterText="VM").
        SceneElement powerItem = new("e4", "uia", "MenuItem", "Power", null, true, true, false, null,
            new WindowBounds(105, 55, 160, 22), ["activate"], winId, new ElementLocator(Name: "Power", ControlType: "MenuItem"), "menu-power");
        SceneCapture afterPopupItems = new([mainWin], [vmMenu, fileMenu, statusBar, powerItem], false, "uia");
        (bool passedPopup, VerifiedSceneTool.SceneDiff diffPopup) = VerifiedSceneTool.EvaluateMenuItemAnyChange(
            beforeFiltered, afterPopupItems, vmMenu, unfilteredKeys);
        passedPopup.Should().BeTrue();
        diffPopup.ElementsAdded.Should().ContainSingle(x => x.Contains("Power"));

        // Case 4: Target MenuItem transitioned to Expanded.
        SceneCapture afterExpanded = new([mainWin], [vmMenu with { ExpandCollapseState = "Expanded" }, fileMenu, statusBar], false, "uia");
        (bool passedExpanded, VerifiedSceneTool.SceneDiff diffExpanded) = VerifiedSceneTool.EvaluateMenuItemAnyChange(
            beforeFiltered, afterExpanded, vmMenu, unfilteredKeys);
        passedExpanded.Should().BeTrue();
        diffExpanded.ElementsChanged.Should().ContainSingle(x => x.Contains("Expanded"));
    }

    [Fact]
    public void MenuItem_unconfirmed_hint_suggests_click_point_at_element_bounds_center()
    {
        WindowIdentity winId = new(1, 100, new WindowBounds(0, 0, 1200, 800));
        SceneElement vmMenu = new("e1", "uia", "MenuItem", "VM", null, true, true, false, null,
            new WindowBounds(100, 30, 50, 20), ["activate"], winId, new ElementLocator(Name: "VM", ControlType: "MenuItem"), "menu-vm");

        object hint = VerifiedSceneTool.BuildMenuItemUnconfirmedHint(vmMenu);
        using JsonDocument doc = JsonDocument.Parse(VerifiedSceneTool.Serialize(hint));
        JsonElement root = doc.RootElement;
        root.GetProperty("code").GetString().Should().Be("dispatched_unconfirmed");
        root.GetProperty("suggestedTool").GetString().Should().Be("click_point");
        root.GetProperty("suggestedArgs").GetProperty("x").GetInt32().Should().Be(125);
        root.GetProperty("suggestedArgs").GetProperty("y").GetInt32().Should().Be(40);
        root.GetProperty("suggestedArgs").GetProperty("coordinateSpace").GetString().Should().Be("screen");
        root.GetProperty("suggestedArgs").GetProperty("focusWindow").GetBoolean().Should().BeTrue();
        root.GetProperty("guidance").GetString().Should().Contain("click_point").And.Contain("wui.unibar");
    }
}
