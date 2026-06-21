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
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public sealed class UiDifferTests
{
    private const string Win = "Main";

    [Fact]
    public void Identical_snapshots_report_no_change()
    {
        UiSnapshot snapshot = Snap(windows: new[] { Window(1) }, elements: new[] { Button("Go", "GoButton") });

        UiChange change = UiDiffer.Diff(snapshot, snapshot);

        change.Verdict.Should().Be(UiDiffer.VerdictNoChange);
        change.WindowsOpened.Should().BeEmpty();
        change.ElementsAdded.Should().BeEmpty();
        change.ElementsChanged.Should().BeEmpty();
    }

    [Fact]
    public void A_new_window_is_reported_as_opened()
    {
        UiSnapshot before = Snap(windows: new[] { Window(1) });
        UiSnapshot after = Snap(windows: new[] { Window(1), Window(2) });

        UiChange change = UiDiffer.Diff(before, after);

        change.Verdict.Should().Be(UiDiffer.VerdictChanged);
        change.WindowsOpened.Should().ContainSingle().Which.WindowHandle.Should().Be(2);
        change.WindowsClosed.Should().BeEmpty();
    }

    [Fact]
    public void A_disappeared_window_is_reported_as_closed()
    {
        UiSnapshot before = Snap(windows: new[] { Window(1), Window(2) });
        UiSnapshot after = Snap(windows: new[] { Window(1) });

        UiChange change = UiDiffer.Diff(before, after);

        change.WindowsClosed.Should().ContainSingle().Which.WindowHandle.Should().Be(2);
    }

    [Fact]
    public void New_and_removed_elements_are_reported()
    {
        UiSnapshot before = Snap(elements: new[] { Button("Open", "OpenButton") });
        UiSnapshot after = Snap(elements: new[] { Button("Confirm", "ConfirmButton") });

        UiChange change = UiDiffer.Diff(before, after);

        change.ElementsAdded.Should().ContainSingle().Which.AutomationId.Should().Be("ConfirmButton");
        change.ElementsRemoved.Should().ContainSingle().Which.AutomationId.Should().Be("OpenButton");
    }

    [Fact]
    public void A_toggled_checkbox_is_reported_as_a_field_change()
    {
        UiSnapshot before = Snap(elements: new[] { Checkbox("Feature", "Toggle", "Off") });
        UiSnapshot after = Snap(elements: new[] { Checkbox("Feature", "Toggle", "On") });

        UiChange change = UiDiffer.Diff(before, after);

        change.ElementsChanged.Should().ContainSingle();
        ElementChange ec = change.ElementsChanged[0];
        ec.AutomationId.Should().Be("Toggle");
        ec.Changes.Should().ContainSingle(f => f.Field == "toggleState"
            && f.Before == "Off" && f.After == "On");
    }

    [Fact]
    public void Duplicate_keyed_elements_are_matched_by_occurrence_order()
    {
        // Two id-less buttons sharing name "OK"; the second is removed.
        UiSnapshot before = Snap(elements: new[] { Button("OK", null), Button("OK", null) });
        UiSnapshot after = Snap(elements: new[] { Button("OK", null) });

        UiChange change = UiDiffer.Diff(before, after);

        change.ElementsRemoved.Should().ContainSingle("one of the two duplicates disappeared");
        change.ElementsChanged.Should().BeEmpty("the surviving duplicate did not change");
    }

    private static UiSnapshot Snap(
        IReadOnlyList<WindowDescriptor>? windows = null,
        IReadOnlyList<UiInteractable>? elements = null) =>
        new(windows ?? Array.Empty<WindowDescriptor>(), elements ?? Array.Empty<UiInteractable>());

    private static WindowDescriptor Window(long handle) =>
        new(ProcessId: 10, WindowHandle: handle, Title: Win, ProcessName: "app", IsForeground: handle == 1,
            X: 0, Y: 0, Width: 100, Height: 100);

    private static UiInteractable Button(string name, string? automationId) =>
        new("Button", automationId, name, Value: null, ToggleState: null, IsSelected: null,
            IsEnabled: true, IsVisible: true, X: 0, Y: 0, Width: 10, Height: 10, WindowTitle: Win,
            RecommendedLocator: automationId is null ? $"name=\"{name}\"" : $"automationId=\"{automationId}\"");

    private static UiInteractable Checkbox(string name, string automationId, string toggleState) =>
        new("CheckBox", automationId, name, Value: null, ToggleState: toggleState, IsSelected: null,
            IsEnabled: true, IsVisible: true, X: 0, Y: 0, Width: 10, Height: 10, WindowTitle: Win,
            RecommendedLocator: $"automationId=\"{automationId}\"");
}
