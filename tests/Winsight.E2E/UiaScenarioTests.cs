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
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.E2E;

[Trait("Category", "E2E")]
public class UiaScenarioTests
{
    private const string CountLabelId = "CountLabel";
    private const string IncrementButtonId = "IncrementButton";
    private const string OptionSelectorId = "OptionSelector";
    private const string FeatureToggleId = "FeatureToggle";
    private const string OpenDialogButtonId = "OpenDialogButton";
    private const string SemanticContainerId = "SemanticContainer";
    private const string LastNoteLabelId = "LastNoteLabel";
    private const string NoteInputId = "NoteInput";
    private const string ConfirmNoteButtonId = "ConfirmNoteButton";
    private const string DialogTitleFragment = "Note";
    private const string NoteText = "hello from UIA";
    private const string ComboTargetItem = "Beta";
    private const string ShortcutText = "Shortcut: Ctrl+Shift+N";
    private const string IncrementTooltip = "Increment counter by one";

    [Fact]
    public void Agent_can_synchronize_assert_and_drive_rich_controls()
    {
        var executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        var waiter = new ElementWaiter();
        var invoker = new ActionInvoker();
        var stateReader = new ElementStateReader();
        var capture = new WindowCapture();
        string? dialogScreenshotPath = null;

        try
        {
            // wait_for_element replaces the fixed startup sleep.
            OperationResult appeared = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: CountLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);

            OperationResult shortcut = invoker.SendKeyChord(process.Id, "Ctrl+Shift+N");
            shortcut.Success.Should().BeTrue(shortcut.Error);
            OperationResult shortcutHandled = waiter.Wait(
                process.Id,
                new ElementLocator(AutomationId: LastNoteLabelId, NameContains: ShortcutText),
                WaitCondition.Exists);
            shortcutHandled.Success.Should().BeTrue(shortcutHandled.Error);

            OperationResult tooltip = invoker.Hover(
                process.Id,
                new ElementLocator(AutomationId: IncrementButtonId),
                expectedTooltipText: IncrementTooltip,
                timeoutMs: 3000);
            tooltip.Success.Should().BeTrue(tooltip.Error);

            // Strict mode: an ambiguous locator must fail and report candidates.
            OperationResult ambiguous = invoker.Click(process.Id, new ElementLocator(ControlType: "Button"));
            ambiguous.Success.Should().BeFalse("several buttons match the locator");
            ambiguous.Error.Should().Contain("matched");

            // Disambiguation via partial name match.
            OperationResult increment = invoker.Click(
                process.Id, new ElementLocator(NameContains: "increment", ControlType: "Button"));
            increment.Success.Should().BeTrue(increment.Error);

            // get_element_state is the UI-level oracle: no CLRMD attach needed.
            ElementState countState = ReadState(stateReader, process.Id, CountLabelId);
            countState.Name.Should().Be("Count: 1");

            OperationResult stableCount = waiter.Wait(
                process.Id,
                new ElementLocator(AutomationId: CountLabelId),
                WaitCondition.StableFor,
                pollIntervalMs: 50,
                stableForMs: 350);
            stableCount.Success.Should().BeTrue(stableCount.Error);
            ((WaitOutcome)stableCount.Data!).ElapsedMs.Should().BeGreaterThanOrEqualTo(350);

            OperationResult semantic = stateReader.Read(
                process.Id,
                new ElementLocator(ControlType: "Button", DescendantText: "semantic child label"));
            semantic.Success.Should().BeTrue(semantic.Error);
            ((ElementState)semantic.Data!).AutomationId.Should().Be(SemanticContainerId);

            // select_item drives the ComboBox by item text.
            OperationResult selection = invoker.SelectItem(
                process.Id, new ElementLocator(AutomationId: OptionSelectorId), ComboTargetItem);
            selection.Success.Should().BeTrue(selection.Error);
            ElementState comboState = ReadState(stateReader, process.Id, OptionSelectorId);
            comboState.SelectedItem.Should().Be(ComboTargetItem);

            // A primary click on a CheckBox goes through the Toggle pattern.
            OperationResult toggle = invoker.Click(process.Id, new ElementLocator(AutomationId: FeatureToggleId));
            toggle.Success.Should().BeTrue(toggle.Error);
            ElementState toggleState = ReadState(stateReader, process.Id, FeatureToggleId);
            toggleState.ToggleState.Should().Be("On");

            // Modal dialog: a second top-level window of the same process.
            OperationResult openDialog = invoker.Click(process.Id, new ElementLocator(AutomationId: OpenDialogButtonId));
            openDialog.Success.Should().BeTrue(openDialog.Error);

            OperationResult dialogShown = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: NoteInputId), WaitCondition.Exists,
                windowTitle: DialogTitleFragment);
            dialogShown.Success.Should().BeTrue(dialogShown.Error);

            dialogScreenshotPath = Path.Combine(
                Path.GetTempPath(),
                $"winsight-dialog-{Guid.NewGuid():N}.png");
            ScreenshotResult dialogScreenshot = capture.Capture(
                process.Id,
                dialogScreenshotPath,
                windowTitle: DialogTitleFragment);
            File.Exists(dialogScreenshot.Path).Should().BeTrue();
            dialogScreenshot.Width.Should().BeGreaterThan(0);
            dialogScreenshot.Height.Should().BeGreaterThan(0);

            OperationResult typed = invoker.Type(
                process.Id, new ElementLocator(AutomationId: NoteInputId), NoteText,
                windowTitle: DialogTitleFragment);
            typed.Success.Should().BeTrue(typed.Error);

            OperationResult typedValue = waiter.Wait(
                process.Id,
                new ElementLocator(AutomationId: NoteInputId),
                WaitCondition.ValueEquals,
                windowTitle: DialogTitleFragment,
                expectedValue: NoteText);
            typedValue.Success.Should().BeTrue(typedValue.Error);

            OperationResult confirm = invoker.Click(
                process.Id, new ElementLocator(AutomationId: ConfirmNoteButtonId),
                windowTitle: DialogTitleFragment);
            confirm.Success.Should().BeTrue(confirm.Error);

            OperationResult dialogClosed = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: NoteInputId), WaitCondition.NotExists);
            dialogClosed.Success.Should().BeTrue(dialogClosed.Error);

            // The note travelled dialog -> view model -> main window label.
            OperationResult noteAppeared = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: LastNoteLabelId, NameContains: NoteText),
                WaitCondition.Exists);
            noteAppeared.Success.Should().BeTrue(noteAppeared.Error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            if (dialogScreenshotPath is not null && File.Exists(dialogScreenshotPath))
            {
                File.Delete(dialogScreenshotPath);
            }
        }
    }

    private static ElementState ReadState(ElementStateReader stateReader, int processId, string automationId)
    {
        OperationResult result = stateReader.Read(processId, new ElementLocator(AutomationId: automationId));
        result.Success.Should().BeTrue(result.Error);
        return (ElementState)result.Data!;
    }
}
