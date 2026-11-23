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
using Winsight.Mcp.Serialization;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public class ActionableOnlyTests
{
    private const long WindowHandleValue = 42;
    private const long DomWindowHandle = 0;
    private static readonly string[] PointerTrio = ["drag_to", "right_click", "double_click"];
    private static readonly string[] ActivateWithPointer = ["activate", "drag_to", "right_click", "double_click"];
    private static readonly string[] ScrollWithPointer = ["scroll_into_view", "drag_to", "right_click", "double_click"];
    private static readonly string[] OcrActions = ["activate", "right_click", "double_click"];
    private static readonly WindowBounds SomeBounds = new(0, 0, 100, 20);
    private static readonly WindowBounds EmptyBounds = new(0, 0, 0, 0);

    private static SceneElement Make(string reference, string role, string name, IReadOnlyList<string> actions,
        bool visible = true, WindowBounds? bounds = null, string source = "uia", long handle = WindowHandleValue) =>
        new(reference, source, role, name, null, true, visible, false, null, bounds ?? SomeBounds, actions,
            new WindowIdentity(1, handle, new WindowBounds(0, 0, 800, 600)),
            new ElementLocator(Name: name, ControlType: role), "id-" + reference);

    [Fact]
    public void Predicate_excludes_elements_with_only_the_pointer_trio()
    {
        SceneActionability.IsActionable(Make("e1", "Separator", "", PointerTrio)).Should().BeFalse();
        SceneActionability.IsActionable(Make("e2", "Text", "Status", PointerTrio)).Should().BeFalse();
    }

    [Fact]
    public void Predicate_excludes_invisible_zero_bounds_menu_item()
    {
        SceneElement hidden = Make("e1", "MenuItem", "Hidden", ["activate"], visible: false, bounds: EmptyBounds);
        SceneActionability.IsActionable(hidden).Should().BeFalse();
    }

    [Fact]
    public void Predicate_keeps_offscreen_element_that_can_scroll_into_view()
    {
        SceneElement offscreen = Make("e1", "ListItem", "Row 99", ScrollWithPointer, visible: false);
        SceneActionability.IsActionable(offscreen).Should().BeTrue();
    }

    [Fact]
    public void Predicate_keeps_visible_button_and_ocr_element()
    {
        SceneActionability.IsActionable(Make("e1", "Button", "Save", ActivateWithPointer)).Should().BeTrue();
        SceneActionability.IsActionable(Make("t1", "ocr_text", "Hello", OcrActions, source: "ocr")).Should().BeTrue();
    }

    private static (WindowDescriptor Win, List<SceneElement> Elements) Fixture()
    {
        WindowDescriptor win = new(1, WindowHandleValue, "App", "app", true, 0, 0, 800, 600);
        List<SceneElement> elements =
        [
            Make("e1", "Separator", "", PointerTrio),
            Make("e2", "Text", "Status", PointerTrio),
            Make("e3", "MenuItem", "Hidden", ["activate"], visible: false, bounds: EmptyBounds),
            Make("e4", "ListItem", "Row 99", ScrollWithPointer, visible: false),
            Make("e5", "Button", "Save", ActivateWithPointer),
        ];
        return (win, elements);
    }

    [Fact]
    public void Enrich_capture_with_actionable_only_drops_non_actionable_and_keeps_the_rest()
    {
        using Process self = Process.GetCurrentProcess();
        (WindowDescriptor win, List<SceneElement> elements) = Fixture();

        SceneCapture enriched = VerifiedSceneTool.EnrichCapture(
            SceneServices.Default, self.Id, null, new SceneCapture([win], elements, false, "uia"), actionableOnly: true);

        enriched.Elements.Select(e => e.Ref).Should().BeEquivalentTo(["e4", "e5"]);
    }

    [Fact]
    public void Surface_hint_count_ignores_separators_and_text()
    {
        using Process self = Process.GetCurrentProcess();
        WindowDescriptor win = new(self.Id, WindowHandleValue, "App", "app", true, 0, 0, 800, 600);
        List<SceneElement> elements = [];
        for (int i = 0; i < 5; i++) elements.Add(Make("s" + i, "Separator", "", PointerTrio));
        elements.Add(Make("e1", "Button", "Only", ActivateWithPointer));

        SceneCapture enriched = VerifiedSceneTool.EnrichCapture(
            SceneServices.Default, self.Id, null, new SceneCapture([win], elements, false, "uia"));

        enriched.SurfaceHint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Any_change_baseline_keeps_counting_status_text_changes_under_actionable_only()
    {
        using Process self = Process.GetCurrentProcess();
        WindowDescriptor win = new(self.Id, WindowHandleValue, "App", "app", true, 0, 0, 800, 600);
        SceneElement button = Make("e1", "Button", "Save", ActivateWithPointer);
        SceneElement statusBefore = Make("e2", "Text", "Idle", ["drag_to", "right_click", "double_click", "focus"]);
        SceneElement statusAfter = statusBefore with { Name = "Saved" };

        (SceneCapture strictBefore, SceneCapture looseBefore) = VerifiedSceneTool.EnrichCaptureWithBaseline(
            SceneServices.Default, self.Id, null, new SceneCapture([win], [button, statusBefore], false, "uia"), actionableOnly: true);
        (_, SceneCapture looseAfter) = VerifiedSceneTool.EnrichCaptureWithBaseline(
            SceneServices.Default, self.Id, null, new SceneCapture([win], [button, statusAfter], false, "uia"), actionableOnly: true);

        VerifiedSceneTool.ComputeDiff(looseBefore, looseAfter).HasChanges.Should().BeTrue();
        strictBefore.Elements.Select(e => e.Ref).Should().BeEquivalentTo(["e1", "e2"]);
    }

    [Fact]
    public void Any_change_baseline_ignores_pure_text_with_no_actions_like_before()
    {
        using Process self = Process.GetCurrentProcess();
        WindowDescriptor win = new(self.Id, WindowHandleValue, "App", "app", true, 0, 0, 800, 600);
        SceneElement button = Make("e1", "Button", "Save", ActivateWithPointer);
        SceneElement pure = Make("e2", "Text", "Idle", []);

        (_, SceneCapture loose) = VerifiedSceneTool.EnrichCaptureWithBaseline(
            SceneServices.Default, self.Id, null, new SceneCapture([win], [button, pure], false, "uia"), actionableOnly: true);

        loose.Elements.Select(e => e.Ref).Should().BeEquivalentTo(["e1"]);
    }

    [Fact]
    public void Element_serializes_window_handle_and_no_window_object()
    {
        string json = JsonSerializer.Serialize(Make("e1", "Button", "Save", ActivateWithPointer), JsonOptions.Default);
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("window", out _).Should().BeFalse();
        doc.RootElement.GetProperty("windowHandle").GetInt64().Should().Be(WindowHandleValue);
    }

    [Fact]
    public void Handle_zero_element_omits_window_handle()
    {
        string json = JsonSerializer.Serialize(
            Make("e1", "Button", "Save", ActivateWithPointer, handle: DomWindowHandle), JsonOptions.Default);
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("windowHandle", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("window", out _).Should().BeFalse();
    }
}
