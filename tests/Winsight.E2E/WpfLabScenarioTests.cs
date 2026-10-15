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
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// WPF shell scenarios on the lab: tooltip popup versus HelpText, window
/// selection while a tooltip is open, selection of tree rows whose names carry
/// a clock, keyboard focus reporting, UIA wait conditions and window geometry.
/// </summary>
[Trait("Category", "E2E")]
public class WpfLabScenarioTests
{
    private const string MainWindowId = "LabMainWindow";
    private const string HelpButtonId = "HelpButton";
    private const string HelpOnlyButtonId = "AddTabLaterButton";
    private const string FocusInputId = "FocusInput";
    private const string SizeLabelId = "SizeLabel";
    private const string TooltipText = "Tooltip popup text";
    private const string HelpText = "Help text exposed to automation";
    private const string ButtonHelpText = "Adds a tab after a short delay";
    private const int TooltipTimeoutMs = 3000;
    private const int ClockTickMs = 1100;
    private static readonly ElementLocator Tree = new(AutomationId: "ServerTree");
    private static readonly ElementLocator Sessions = new(Name: "Sessions", ControlType: "Tab");

    [Fact]
    public void Hover_reads_the_tooltip_popup_rather_than_the_help_text()
    {
        using WpfLabSession lab = WpfLabSession.Start();

        OperationResult hover = new ActionInvoker().Hover(
            lab.ProcessId, new ElementLocator(AutomationId: HelpButtonId), timeoutMs: TooltipTimeoutMs);

        hover.Success.Should().BeTrue(hover.Error);
        TooltipSnapshot tooltip = ((HoverResult)hover.Data!).Tooltip!;
        tooltip.Text.Should().Be(TooltipText, "the popup the user sees wins over the HelpText ({0})", HelpText);
        tooltip.Source.Should().Be(TooltipSnapshot.SourcePopup);
        tooltip.WindowHandle.Should().NotBe(0);
    }

    [Fact]
    public void Hover_falls_back_to_the_help_text_when_no_popup_opens()
    {
        using WpfLabSession lab = WpfLabSession.Start();

        OperationResult hover = new ActionInvoker().Hover(
            lab.ProcessId, new ElementLocator(AutomationId: HelpOnlyButtonId), timeoutMs: 800);

        hover.Success.Should().BeTrue(hover.Error);
        TooltipSnapshot tooltip = ((HoverResult)hover.Data!).Tooltip!;
        tooltip.Text.Should().Be(ButtonHelpText);
        tooltip.Source.Should().Be(TooltipSnapshot.SourceHelpText);
    }

    [Fact]
    public void Inspect_capture_and_focus_skip_an_open_tooltip()
    {
        using WpfLabSession lab = WpfLabSession.Start();
        OperationResult hover = new ActionInvoker().Hover(
            lab.ProcessId,
            new ElementLocator(AutomationId: HelpButtonId),
            expectedTooltipText: TooltipText,
            timeoutMs: TooltipTimeoutMs);
        hover.Success.Should().BeTrue(hover.Error);
        new WindowDiscovery().List(lab.ProcessId)[0].Kind.Should().Be(
            WindowClassifier.KindPopup, "the tooltip popup must be the topmost window for this test to mean anything");

        UiElementNode inspected = new UiTreeInspector().Inspect(lab.ProcessId, maxDepth: 0);
        inspected.AutomationId.Should().Be(MainWindowId);

        string path = Path.Combine(Path.GetTempPath(), $"winsight-lab-{Guid.NewGuid():N}.png");
        try
        {
            ScreenshotResult shot = new WindowCapture().Capture(lab.ProcessId, path);
            shot.Width.Should().Be(inspected.Width);
            shot.Height.Should().Be(inspected.Height);
        }
        finally
        {
            File.Delete(path);
        }

        new UiTreeInspector().Inspect(lab.ProcessId, maxDepth: 0, mainWindow: true)
            .AutomationId.Should().Be(MainWindowId);
        OperationResult chord = new ActionInvoker().SendKeyChord(lab.ProcessId, "Ctrl+Shift+PageDown");
        chord.Success.Should().BeTrue(chord.Error);
        ((KeyChordResult)chord.Data!).WindowTitle.Should().Be("Winsight WPF Lab");
    }

    [Fact]
    public void Tree_rows_with_a_live_clock_are_selected_by_substring_or_pattern()
    {
        using WpfLabSession lab = WpfLabSession.Start();
        ActionInvoker invoker = new();
        OperationResult before = new ElementStateReader().Read(
            lab.ProcessId, new ElementLocator(NameContains: "lab-c,", ControlType: "TreeItem"));
        before.Success.Should().BeTrue(before.Error);
        string staleName = ((ElementState)before.Data!).Name;
        Thread.Sleep(ClockTickMs);

        invoker.SelectItem(lab.ProcessId, Tree, staleName).Success.Should().BeFalse(
            "the exact name read a second ago no longer exists once the clock in it moved");

        OperationResult byContains = invoker.SelectItem(lab.ProcessId, Tree, Matcher(contains: "lab-c,"));
        byContains.Success.Should().BeTrue(byContains.Error);
        ((SelectItemResult)byContains.Data!).SelectedName.Should().StartWith("lab-c, protocol SFTP");
        IsSelected(lab, "lab-c,").Should().BeTrue();

        OperationResult byPattern = invoker.SelectItem(lab.ProcessId, Tree, Matcher(pattern: @"^lab-b, protocol RDP, state \w+"));
        byPattern.Success.Should().BeTrue(byPattern.Error);
        IsSelected(lab, "lab-b,").Should().BeTrue();

        OperationResult ambiguous = invoker.SelectItem(lab.ProcessId, Tree, Matcher(contains: "state Reachable"));
        ambiguous.Success.Should().BeFalse();
        ambiguous.Error.Should().Contain("Several items");
    }

    [Fact]
    public void Focused_element_reports_the_wpf_text_box_and_the_chord_reports_its_recipient()
    {
        using WpfLabSession lab = WpfLabSession.Start();
        ActionInvoker invoker = new();
        OperationResult typed = invoker.Type(lab.ProcessId, new ElementLocator(AutomationId: FocusInputId), "x", focusWindow: true);
        typed.Success.Should().BeTrue(typed.Error);
        OperationResult click = invoker.Click(lab.ProcessId, new ElementLocator(AutomationId: FocusInputId), ClickStyle.DoubleClick);
        click.Success.Should().BeTrue(click.Error);

        OperationResult read = new FocusedElementReader().Read(lab.ProcessId);

        read.Success.Should().BeTrue(read.Error);
        FocusedElementInfo focus = (FocusedElementInfo)read.Data!;
        focus.AutomationId.Should().Be(FocusInputId);
        focus.ControlType.Should().Be("Edit");
        focus.FrameworkId.Should().Be("WPF");
        focus.BelongsToTarget.Should().BeTrue();
        focus.IsWebView2.Should().BeFalse();
        focus.WebView2Evidence.Should().BeEmpty();

        OperationResult chord = invoker.SendKeyChord(lab.ProcessId, "End");
        chord.Success.Should().BeTrue(chord.Error);
        ((KeyChordResult)chord.Data!).Focus!.AutomationId.Should().Be(FocusInputId);
    }

    [Fact]
    public void Wait_conditions_on_child_count_and_name_replace_fixed_sleeps()
    {
        using WpfLabSession lab = WpfLabSession.Start();
        ElementWaiter waiter = new();

        OperationResult three = waiter.Wait(
            lab.ProcessId, Sessions, WaitCondition.ChildCount, timeoutMs: 2000, expectedCount: 3, childControlType: "TabItem");
        three.Success.Should().BeTrue(three.Error);

        OperationResult click = new ActionInvoker().Click(lab.ProcessId, new ElementLocator(AutomationId: HelpOnlyButtonId));
        click.Success.Should().BeTrue(click.Error);
        OperationResult four = waiter.Wait(
            lab.ProcessId, Sessions, WaitCondition.ChildCount, timeoutMs: 5000, expectedCount: 4, childControlType: "TabItem");
        four.Success.Should().BeTrue(four.Error);
        WaitOutcome outcome = (WaitOutcome)four.Data!;
        outcome.ChildCount.Should().Be(4);
        outcome.ElapsedMs.Should().BeGreaterThan(0, "the tab is added after a delay, so the wait must actually wait");

        waiter.Wait(lab.ProcessId, new ElementLocator(AutomationId: "TabOrder"), WaitCondition.NameContains,
                timeoutMs: 2000, expectedValue: "EXTRA-1")
            .Success.Should().BeTrue();
        waiter.Wait(lab.ProcessId, new ElementLocator(NameContains: "lab-a,", ControlType: "TreeItem"), WaitCondition.NameMatches,
                timeoutMs: 2000, expectedValue: @"· \d{2}:\d{2}:\d{2}$")
            .Success.Should().BeTrue();

        OperationResult never = waiter.Wait(
            lab.ProcessId, Sessions, WaitCondition.ChildCount, timeoutMs: 400, expectedCount: 9, childControlType: "TabItem");
        never.Success.Should().BeFalse();
        ((WaitOutcome)never.Data!).ChildCount.Should().Be(4);
    }

    [Fact]
    public void Window_can_be_resized_moved_maximized_minimized_and_restored()
    {
        using WpfLabSession lab = WpfLabSession.Start();
        WindowManager manager = new();
        string initialSize = lab.ReadName(SizeLabelId);

        WindowStateResult resized = Apply(manager, lab, WindowAction.Resize, width: 700, height: 500);
        resized.State.Should().Be(WindowManager.StateNormal);
        (resized.Width, resized.Height).Should().Be((700, 500));
        resized.MatchesRequest.Should().BeTrue();
        lab.WaitForName(SizeLabelId, "Size: 700x500").Should().BeTrue(
            $"the layout must observe the new size (was \"{initialSize}\", now \"{lab.ReadName(SizeLabelId)}\")");

        WindowStateResult moved = Apply(manager, lab, WindowAction.Move, x: 40, y: 60);
        (moved.X, moved.Y, moved.Width).Should().Be((40, 60, 700));

        WindowStateResult tooSmall = Apply(manager, lab, WindowAction.Resize, width: 200, height: 150);
        tooSmall.MatchesRequest.Should().BeFalse("the lab declares MinWidth=480 and MinHeight=360");
        tooSmall.Width.Should().BeGreaterThan(200);

        WindowStateResult maximized = Apply(manager, lab, WindowAction.Maximize);
        maximized.State.Should().Be(WindowManager.StateMaximized);
        maximized.Width.Should().BeGreaterThan(tooSmall.Width);
        // The show state flips before the frame is laid out; the reported bounds
        // must be the restored ones, not the maximized ones still on screen.
        WindowStateResult unmaximized = Apply(manager, lab, WindowAction.Restore);
        (unmaximized.State, unmaximized.Width, unmaximized.Height).Should().Be(
            (WindowManager.StateNormal, tooSmall.Width, tooSmall.Height));

        Apply(manager, lab, WindowAction.Maximize).State.Should().Be(WindowManager.StateMaximized);
        Apply(manager, lab, WindowAction.Minimize).State.Should().Be(WindowManager.StateMinimized);
        WindowStateResult restored = Apply(manager, lab, WindowAction.Restore);
        restored.State.Should().Be(WindowManager.StateNormal);
        restored.MatchesRequest.Should().BeTrue();
    }

    private static WindowStateResult Apply(
        WindowManager manager,
        WpfLabSession lab,
        WindowAction action,
        int? x = null,
        int? y = null,
        int? width = null,
        int? height = null)
    {
        OperationResult result = manager.Apply(lab.ProcessId, action, x, y, width, height);
        result.Success.Should().BeTrue(result.Error);
        return (WindowStateResult)result.Data!;
    }

    private static ItemNameMatcher Matcher(string? contains = null, string? pattern = null)
    {
        ItemNameMatcher.TryCreate(null, contains, pattern, out ItemNameMatcher? matcher, out string? error)
            .Should().BeTrue(error);
        return matcher!;
    }

    private static bool IsSelected(WpfLabSession lab, string hostPrefix)
    {
        OperationResult state = new ElementStateReader().Read(
            lab.ProcessId, new ElementLocator(NameContains: hostPrefix, ControlType: "TreeItem"));
        return state.Success && ((ElementState)state.Data!).IsSelected == true;
    }
}
