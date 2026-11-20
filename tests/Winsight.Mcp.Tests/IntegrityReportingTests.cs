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

[Collection("ProcessIntegrity")]
public sealed class IntegrityReportingTests
{
    private static readonly IntegrityComparison ElevatedMismatch = ProcessIntegrity.Compare(
        new ProcessIntegrityInfo(ProcessIntegrity.RidMedium, "medium", false, null),
        new ProcessIntegrityInfo(ProcessIntegrity.RidHigh, "high", false, null));

    [Fact]
    public void Set_target_reports_integrity_warning_when_target_is_elevated()
    {
        ProcessIntegrity.OverrideCheck = _ => ElevatedMismatch;
        try
        {
            SessionTarget target = new();
            string json = SessionTargetTool.SetTarget(target, new WindowDiscovery(), Environment.ProcessId);

            using JsonDocument doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            JsonElement data = doc.RootElement.GetProperty("data");
            data.GetProperty("integrity").GetProperty("uipiBlocked").GetBoolean().Should().BeTrue();
            data.GetProperty("integrity").GetProperty("current").GetString().Should().Be("medium");
            data.GetProperty("integrity").GetProperty("target").GetString().Should().Be("high");
            data.GetProperty("integrityWarning").GetString()
                .Should().StartWith($"{ProcessIntegrity.UipiBlockedCode}:")
                .And.Contain("silently drop");
        }
        finally
        {
            ProcessIntegrity.OverrideCheck = null;
        }
    }

    [Fact]
    public void Observe_scene_attaches_integrity_warning_when_target_is_elevated()
    {
        ProcessIntegrity.OverrideCheck = _ => ElevatedMismatch;
        try
        {
            SceneServices services = new SceneStore().Services;
            SceneCapture raw = new([], [], false, "uia");

            SceneCapture enriched = VerifiedSceneTool.EnrichCapture(services, Environment.ProcessId, null, raw);

            enriched.IntegrityWarning.Should().StartWith($"{ProcessIntegrity.UipiBlockedCode}:")
                .And.Contain("medium")
                .And.Contain("high");
        }
        finally
        {
            ProcessIntegrity.OverrideCheck = null;
        }
    }

    [Fact]
    public void Input_tools_refuse_with_uipi_blocked_when_target_is_elevated()
    {
        ProcessIntegrity.OverrideCheck = _ => ElevatedMismatch;
        try
        {
            ActionInvoker invoker = new();
            SessionTarget target = new();
            target.Set(Environment.ProcessId);

            string clickJson = InvokeActionTool.ClickPoint(invoker, target, 100, 100, coordinateSpace: "screen");
            string scrollJson = InvokeActionTool.ScrollPoint(invoker, target, 100, 100, 3, coordinateSpace: "screen");
            string hoverJson = InvokeActionTool.HoverPoint(invoker, target, 100, 100, coordinateSpace: "screen");
            string chordJson = InvokeActionTool.SendKeyChord(invoker, target, "Escape");
            string typeKeysJson = TextInputTool.TypeKeys(invoker, target, "hello");
            string clickElemJson = InvokeActionTool.ClickElement(invoker, target, automationId: "btn");
            string typeTextJson = InvokeActionTool.TypeText(invoker, target, "hello", automationId: "edit");

            foreach (string response in new[] { clickJson, scrollJson, hoverJson, chordJson, typeKeysJson, clickElemJson, typeTextJson })
            {
                using JsonDocument doc = JsonDocument.Parse(response);
                doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
                doc.RootElement.GetProperty("error").GetString()
                    .Should().StartWith($"{ProcessIntegrity.UipiBlockedCode}:")
                    .And.Contain("same or higher integrity level");
            }
        }
        finally
        {
            ProcessIntegrity.OverrideCheck = null;
        }
    }

    [Fact]
    public void Access_denied_target_reports_unknown_and_allows_input()
    {
        IntegrityComparison unknown = ProcessIntegrity.Compare(
            new ProcessIntegrityInfo(ProcessIntegrity.RidMedium, "medium", false, null),
            new ProcessIntegrityInfo(null, "unknown", AccessDenied: true, Win32Error: 5));
        ProcessIntegrity.OverrideCheck = _ => unknown;
        try
        {
            SessionTarget target = new();
            string json = SessionTargetTool.SetTarget(target, new WindowDiscovery(), Environment.ProcessId);

            using JsonDocument doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            JsonElement data = doc.RootElement.GetProperty("data");
            data.GetProperty("integrity").GetProperty("target").GetString().Should().Be("unknown");
            data.GetProperty("integrity").GetProperty("uipiBlocked").GetBoolean().Should().BeFalse();
            data.TryGetProperty("integrityWarning", out JsonElement warn).Should().BeFalse();

            ActionInvoker.CheckInputIntegrity(Environment.ProcessId, out IntegrityComparison checkedComparison)
                .Should().BeNull();
            checkedComparison.IsUipiBlocked.Should().BeFalse();
            checkedComparison.Target.Name.Should().Be("unknown");
        }
        finally
        {
            ProcessIntegrity.OverrideCheck = null;
        }
    }

    [Fact]
    public void Diagnose_pointer_honors_dry_run_without_moving_cursor()
    {
        string? previous = Environment.GetEnvironmentVariable(ToolAccessPolicy.DryRunVariable);
        Environment.SetEnvironmentVariable(ToolAccessPolicy.DryRunVariable, "1");
        try
        {
            ActionInvoker invoker = new();
            SessionTarget target = new();
            target.Set(Environment.ProcessId);

            string json = InvokeActionTool.DiagnosePointer(invoker, target, 100, 100, coordinateSpace: "screen");

            using JsonDocument doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            JsonElement data = doc.RootElement.GetProperty("data");
            data.GetProperty("dryRun").GetBoolean().Should().BeTrue();
            data.GetProperty("action").GetString().Should().Be("diagnose_pointer");
            JsonElement methods = data.GetProperty("preview").GetProperty("methods");
            methods.GetArrayLength().Should().Be(4);
            methods[0].GetString().Should().Be(PointerMoveMethods.SendInputVirtualDesk);
            methods[3].GetString().Should().Be(PointerMoveMethods.SetCursorPos);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ToolAccessPolicy.DryRunVariable, previous);
        }
    }

    [Fact]
    public void Agent_failure_classifies_uipi_blocked_and_move_not_applied()
    {
        object uipiHint = AgentFailure.BuildHint("click_point", "blocked", "uipi_blocked: target higher");
        JsonSerializer.Serialize(uipiHint).Should().Contain("\"code\":\"uipi_blocked\"");

        object moveHint = AgentFailure.BuildHint("click_point", "blocked", "move_not_applied: remained at start");
        JsonSerializer.Serialize(moveHint).Should().Contain("\"code\":\"move_not_applied\"");
    }
}
