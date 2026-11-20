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
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public class AnyChangeHintTests
{
    private static readonly WindowDescriptor MainWin =
        new(1, 100, "Main", "app", true, 0, 0, 1000, 700, WindowClassifier.KindMain, "MainWindow");
    private static readonly WindowIdentity WinId =
        new(1, 100, new WindowBounds(0, 0, 1000, 700));

    private static SceneElement Element(
        string refId,
        string id,
        string role,
        string name,
        string? value = null,
        string? parentId = null) =>
        new(refId, "uia", role, name, value, true, true, false, null,
            new WindowBounds(10, 10, 80, 24), ["activate"], WinId,
            new ElementLocator(Name: name, ControlType: role), id,
            ParentIdentity: parentId);

    [Fact]
    public void BuildAnyChangeUnrelatedHint_returns_guidance_when_only_unrelated_element_changed()
    {
        SceneElement target = Element("e1", "btn-1", "Button", "Save");
        SceneElement clockBefore = Element("e2", "lbl-clock", "Text", "10:00:00");
        SceneElement clockAfter = Element("e2", "lbl-clock", "Text", "10:00:01");

        SceneCapture before = new([MainWin], [target, clockBefore], false, "uia");
        SceneCapture after = new([MainWin], [target, clockAfter], false, "uia");
        VerifiedSceneTool.SceneDiff diff = VerifiedSceneTool.ComputeDiff(before, after);

        object? hint = VerifiedSceneTool.BuildAnyChangeUnrelatedHint(before, after, target, diff);

        hint.Should().NotBeNull();
        string json = JsonSerializer.Serialize(hint);
        json.Should().Contain("any_change_unrelated");
        json.Should().Contain("window_open").And.Contain("selected").And.Contain("value").And.Contain("toggle");
    }

    [Fact]
    public void BuildAnyChangeUnrelatedHint_returns_null_when_target_itself_changed()
    {
        SceneElement targetBefore = Element("e1", "chk-1", "CheckBox", "Enable", value: "Off");
        SceneElement targetAfter = Element("e1", "chk-1", "CheckBox", "Enable", value: "On");

        SceneCapture before = new([MainWin], [targetBefore], false, "uia");
        SceneCapture after = new([MainWin], [targetAfter], false, "uia");
        VerifiedSceneTool.SceneDiff diff = VerifiedSceneTool.ComputeDiff(before, after);

        object? hint = VerifiedSceneTool.BuildAnyChangeUnrelatedHint(before, after, targetBefore, diff);

        hint.Should().BeNull();
    }

    [Fact]
    public void BuildAnyChangeUnrelatedHint_returns_null_when_descendant_changed()
    {
        SceneElement target = Element("e1", "panel-1", "Group", "Container");
        SceneElement child = Element("e2", "row-1", "ListItem", "Row", parentId: "panel-1");
        SceneElement grandchildBefore = Element("e3", "cell-1", "Text", "Old", parentId: "row-1");
        SceneElement grandchildAfter = Element("e3", "cell-1", "Text", "New", parentId: "row-1");

        SceneCapture before = new([MainWin], [target, child, grandchildBefore], false, "uia");
        SceneCapture after = new([MainWin], [target, child, grandchildAfter], false, "uia");
        VerifiedSceneTool.SceneDiff diff = VerifiedSceneTool.ComputeDiff(before, after);

        object? hint = VerifiedSceneTool.BuildAnyChangeUnrelatedHint(before, after, target, diff);

        hint.Should().BeNull();
    }

    [Fact]
    public void BuildAnyChangeUnrelatedHint_returns_null_when_new_window_opened()
    {
        SceneElement target = Element("e1", "btn-open", "Button", "Open Dialog");
        WindowDescriptor dialog = new(1, 200, "Settings", "app", true, 50, 50, 400, 300, WindowClassifier.KindDialog, "#32770");

        SceneCapture before = new([MainWin], [target], false, "uia");
        SceneCapture after = new([MainWin, dialog], [target], false, "uia");
        VerifiedSceneTool.SceneDiff diff = VerifiedSceneTool.ComputeDiff(before, after);

        object? hint = VerifiedSceneTool.BuildAnyChangeUnrelatedHint(before, after, target, diff);

        hint.Should().BeNull();
    }

    [Fact]
    public void GetWinsightGuide_documents_any_change_unrelated_guidance()
    {
        string json = AgentExperienceTool.GetWinsightGuide();

        json.Should().Contain("any_change_unrelated");
        json.Should().Contain("window_open");
    }
}
