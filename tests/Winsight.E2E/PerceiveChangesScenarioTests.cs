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
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// The feedback sense: snapshot_ui then diff_ui report what an action changed,
/// a toggled checkbox as a field change, and an opened dialog as a new window
/// with new actionable elements.
/// </summary>
[Trait("Category", "E2E")]
public class PerceiveChangesScenarioTests
{
    private const string ReadyLabelId = "CountLabel";
    private const string FeatureToggleId = "FeatureToggle";
    private const string OpenDialogButtonId = "OpenDialogButton";
    private const string NoteInputId = "NoteInput";
    private const string DialogTitleFragment = "Note";

    [Fact]
    public void Diff_reports_a_toggled_checkbox_then_an_opened_dialog()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        try
        {
            var waiter = new ElementWaiter();
            OperationResult appeared = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: ReadyLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);

            var snapshotter = new UiSnapshotter(new WindowDiscovery(), new AffordanceScanner());
            var store = new UiSnapshotStore();
            var target = new SessionTarget();
            target.Set(process.Id);
            var invoker = new ActionInvoker();

            // Baseline, then toggle the checkbox, then diff.
            PerceiveChangesTool.SnapshotUi(snapshotter, store, target);
            invoker.Click(process.Id, new ElementLocator(AutomationId: FeatureToggleId))
                .Success.Should().BeTrue();
            Thread.Sleep(250);

            JsonElement toggleChange = Data(PerceiveChangesTool.DiffUi(snapshotter, store, target));
            toggleChange.GetProperty("verdict").GetString().Should().Be("changed");
            JsonElement changed = toggleChange.GetProperty("elementsChanged");
            changed.EnumerateArray().Should().Contain(
                e => AutomationIdOf(e) == FeatureToggleId,
                "the toggled checkbox must appear as a changed element");

            // The previous diff re-baselined; now open the dialog and diff again.
            invoker.Click(process.Id, new ElementLocator(AutomationId: OpenDialogButtonId))
                .Success.Should().BeTrue();
            waiter.Wait(process.Id, new ElementLocator(AutomationId: NoteInputId), WaitCondition.Exists,
                windowTitle: DialogTitleFragment).Success.Should().BeTrue();

            JsonElement dialogChange = Data(PerceiveChangesTool.DiffUi(snapshotter, store, target));
            dialogChange.GetProperty("verdict").GetString().Should().Be("changed");
            dialogChange.GetProperty("windowsOpened").EnumerateArray().Should().Contain(
                w => (w.GetProperty("title").GetString() ?? string.Empty).Contains(DialogTitleFragment),
                "the opened dialog must be reported as a new window");
            dialogChange.GetProperty("elementsAdded").EnumerateArray().Should().Contain(
                e => AutomationIdOf(e) == NoteInputId,
                "the dialog's input must be reported as a new actionable element");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static JsonElement Data(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        return document.RootElement.GetProperty("data").Clone();
    }

    // automationId is absent from the JSON when the element has none.
    private static string? AutomationIdOf(JsonElement element) =>
        element.TryGetProperty("automationId", out JsonElement id) ? id.GetString() : null;
}
