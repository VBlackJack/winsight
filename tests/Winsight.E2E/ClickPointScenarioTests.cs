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
using Winsight.Core.Modules.ManagedState;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// Drives the counter through a coordinate-based click (the action partner of
/// find_element_visually) instead of UIA element location: a window-relative
/// point over the Increment button must translate to a real click that mutates
/// the managed counter, proving the perception-to-action loop closes end to end.
/// </summary>
[Trait("Category", "E2E")]
public class ClickPointScenarioTests
{
    private const string CounterTypeName = "Winsight.Samples.SimpleCounter.ViewModels.CounterViewModel";
    private const string CounterFieldName = "_internalCount";
    private const string IncrementButtonId = "IncrementButton";
    private const string ReadyLabelId = "CountLabel";
    private const string MainWindowTitleFragment = "Counter";

    [Fact]
    public void Window_relative_click_point_increments_the_counter()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        try
        {
            ElementWaiter waiter = new();
            OperationResult appeared = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: ReadyLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);

            // Resolve the Increment button's window-relative center via the strict
            // locator (robust under UIA load) plus the main window origin: element
            // geometry is screen-space, the window origin gives the offset to
            // subtract — the same mapping find_element_visually produces.
            OperationResult buttonState = new ElementStateReader().Read(
                process.Id, new ElementLocator(AutomationId: IncrementButtonId));
            buttonState.Success.Should().BeTrue(buttonState.Error);
            var button = (ElementState)buttonState.Data!;

            WindowDescriptor main = new WindowDiscovery().List(process.Id)
                .First(w => w.Kind == "main");

            int relX = button.X - main.X + button.Width / 2;
            int relY = button.Y - main.Y + button.Height / 2;

            // Scope the coordinate to the main window: a transient tooltip or
            // popup can otherwise be the topmost window of the process, which is
            // exactly why click_point takes windowTitle for a specific surface.
            var invoker = new ActionInvoker();
            OperationResult click = invoker.ClickPoint(
                process.Id, relX, relY, windowTitle: MainWindowTitleFragment);
            click.Success.Should().BeTrue(click.Error);

            // Allow the UI thread to flush the binding update before reading state.
            Thread.Sleep(250);

            var reader = new ManagedStateReader();
            OperationResult state = reader.Read(process.Id, CounterTypeName, CounterFieldName);
            state.Success.Should().BeTrue(state.Error);
            state.Data.Should().Be(1);
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
