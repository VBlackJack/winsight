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

[Trait("Category", "E2E")]
public class CounterScenarioTests
{
    private const string CounterTypeName = "Winsight.Samples.SimpleCounter.ViewModels.CounterViewModel";
    private const string CounterFieldName = "_internalCount";
    private const string IncrementButtonId = "IncrementButton";
    private const string ResetButtonId = "ResetButton";

    [Fact]
    public void Agent_can_drive_counter_and_observe_internal_state()
    {
        var executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using var process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        try
        {
            WaitForMainWindow(process);

            var windows = new WindowDiscovery().List();
            windows.Should().Contain(w => w.ProcessId == process.Id);

            var invoker = new ActionInvoker();
            var clickResult = invoker.Click(process.Id, new ElementLocator(AutomationId: IncrementButtonId));
            clickResult.Success.Should().BeTrue(clickResult.Error);

            // Allow the UI thread to flush the binding update before we read state.
            Thread.Sleep(250);

            var reader = new ManagedStateReader();
            var state = reader.Read(process.Id, CounterTypeName, CounterFieldName);
            state.Success.Should().BeTrue(state.Error);
            state.Data.Should().Be(1);

            var resetResult = invoker.Click(process.Id, new ElementLocator(AutomationId: ResetButtonId));
            resetResult.Success.Should().BeTrue(resetResult.Error);
            Thread.Sleep(250);

            var afterReset = reader.Read(process.Id, CounterTypeName, CounterFieldName);
            afterReset.Data.Should().Be(0);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static void WaitForMainWindow(Process process)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return;
            }
            Thread.Sleep(100);
        }
        throw new TimeoutException("Sample app did not show its main window within 10 seconds.");
    }
}
