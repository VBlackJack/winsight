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
using Winsight.Core.Modules.ManagedState;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// Proves the set_target ergonomic: after set_target, a process-scoped tool
/// drives the app without repeating processId — the session default is used.
/// </summary>
[Trait("Category", "E2E")]
public class SessionTargetScenarioTests
{
    private const string CounterTypeName = "Winsight.Samples.SimpleCounter.ViewModels.CounterViewModel";
    private const string CounterFieldName = "_internalCount";
    private const string IncrementButtonId = "IncrementButton";
    private const string ReadyLabelId = "CountLabel";

    [Fact]
    public void After_set_target_a_click_omits_process_id_and_still_drives_the_app()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        try
        {
            OperationResult appeared = new ElementWaiter().Wait(
                process.Id, new ElementLocator(AutomationId: ReadyLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);

            SessionTarget target = new();

            // set_target confirms the process and stores the default.
            string setJson = SessionTargetTool.SetTarget(target, new WindowDiscovery(), process.Id);
            using (JsonDocument setDoc = JsonDocument.Parse(setJson))
            {
                setDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            }
            target.ProcessId.Should().Be(process.Id);

            // click_element with no processId — resolved from the session target.
            string clickJson = InvokeActionTool.ClickElement(
                new ActionInvoker(), target, automationId: IncrementButtonId);
            using (JsonDocument clickDoc = JsonDocument.Parse(clickJson))
            {
                clickDoc.RootElement.GetProperty("success").GetBoolean()
                    .Should().BeTrue("the click resolves the process from set_target");
            }

            Thread.Sleep(250);
            OperationResult state = new ManagedStateReader().Read(
                process.Id, CounterTypeName, CounterFieldName);
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

    [Fact]
    public void Without_a_target_or_process_id_the_call_fails_with_a_clear_message()
    {
        string json = InvokeActionTool.ClickElement(
            new ActionInvoker(), new SessionTarget(), automationId: IncrementButtonId);

        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetString().Should().Contain("set_target");
    }
}
