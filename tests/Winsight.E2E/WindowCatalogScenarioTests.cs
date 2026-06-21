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
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// Proves the multi-window discoverability contract: once a modal dialog is up,
/// a per-process listing surfaces both the main window and the dialog — the
/// dialog classified as such and flagged modal — so an agent can find the
/// surface to act on instead of having to guess a window title.
/// </summary>
[Trait("Category", "E2E")]
public class WindowCatalogScenarioTests
{
    private const string ReadyLabelId = "CountLabel";
    private const string OpenDialogButtonId = "OpenDialogButton";
    private const string NoteInputId = "NoteInput";
    private const string DialogTitleFragment = "Note";

    [Fact]
    public void Per_process_listing_surfaces_and_classifies_a_modal_dialog()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        try
        {
            var waiter = new ElementWaiter();
            var invoker = new ActionInvoker();
            var discovery = new WindowDiscovery();

            OperationResult appeared = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: ReadyLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);

            // Before the dialog, the process exposes a single main window.
            IReadOnlyList<WindowDescriptor> beforeDialog = discovery.List(process.Id);
            beforeDialog.Should().ContainSingle(w => w.Kind == "main",
                "the sample app starts with one main window");

            // Open the modal dialog and wait for its content.
            OperationResult openDialog = invoker.Click(
                process.Id, new ElementLocator(AutomationId: OpenDialogButtonId));
            openDialog.Success.Should().BeTrue(openDialog.Error);
            OperationResult dialogShown = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: NoteInputId), WaitCondition.Exists,
                windowTitle: DialogTitleFragment);
            dialogShown.Success.Should().BeTrue(dialogShown.Error);

            // The per-process listing now surfaces the dialog, classified and modal.
            IReadOnlyList<WindowDescriptor> withDialog = discovery.List(process.Id);
            WindowDescriptor? dialog = withDialog.FirstOrDefault(
                w => w.Title.Contains(DialogTitleFragment, StringComparison.OrdinalIgnoreCase));

            dialog.Should().NotBeNull("the modal dialog must be discoverable in the process listing");
            dialog!.Kind.Should().Be("dialog");
            dialog.IsModal.Should().BeTrue("the dialog disables its owner while shown");
            dialog.OwnerHandle.Should().NotBe(0, "the dialog is owned by the main window");
            withDialog.Should().Contain(w => w.Kind == "main",
                "the main window remains in the listing alongside the dialog");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
