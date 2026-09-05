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
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Diagnostics;
using Winsight.Core.Modules.Lifecycle;
using Winsight.Core.Modules.Orchestration;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Core.Modules.Vision;
using Winsight.Mcp;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.E2E;

[Trait("Category", "E2E")]
public sealed class AuditSafetyScenarioTests : IDisposable
{
    private const string MainTitle = "Winsight Sample - Counter";
    private const uint NoActivate = 0x0010;
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private readonly List<Process> _processes = new();
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "winsight-tests", Guid.NewGuid().ToString("N"));

    public AuditSafetyScenarioTests()
    {
        Directory.CreateDirectory(_directory);
        SetEnvironment("WINSIGHT_OUTPUT_DIR", _directory);
        SetEnvironment("WINSIGHT_ALLOWED_PROCESS_IDS", null);
        SetEnvironment("WINSIGHT_ALLOWED_PROCESS_NAMES", null);
        SetEnvironment("WINSIGHT_DRY_RUN", null);
        SetEnvironment("WINSIGHT_TRACE", null);
        SetEnvironment("WINSIGHT_TRACE_DIR", null);
        SetEnvironment("WINSIGHT_VISION_MIN_CONFIDENCE", null);
    }

    [Fact]
    public void Macro_rejects_conflicting_target_without_mutating_the_denied_process()
    {
        Process denied = Launch();
        SetEnvironment("WINSIGHT_ALLOWED_PROCESS_IDS", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        AffordanceScanner scanner = new();
        string json = AgentExperienceTool.ActAndObserve(
            Runner(), new UiSnapshotter(new WindowDiscovery(), scanner), scanner, new WindowCapture(),
            new SessionTarget(), new SessionMemory(),
            new BatchStep("click", ProcessId: denied.Id, AutomationId: "IncrementButton"),
            processId: Environment.ProcessId);
        using JsonDocument result = JsonDocument.Parse(json);
        result.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        ReadCount(denied).Should().Be("Count: 0");
    }

    [Fact]
    public void Graceful_close_preserves_an_application_that_cancels_closing()
    {
        Process process = Launch("--refuse-close");
        AppLauncher launcher = new(new WindowDiscovery());
        OperationResult close = launcher.Close(process.Id);
        close.Success.Should().BeFalse();
        process.HasExited.Should().BeFalse();
        ReadCount(process).Should().Be("Count: 0");
        OperationResult forced = launcher.Close(process.Id, force: true);
        forced.Success.Should().BeTrue(forced.Error);
        process.HasExited.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Coordinate_click_refuses_a_covering_process_even_with_out_of_window_opt_in(bool allowOutOfWindow)
    {
        Process target = Launch();
        WindowIdentity identity = Identity(target);
        ElementState button = Read(target, "IncrementButton");
        Process covering = Launch();
        NativeMethods.SetWindowPos(new IntPtr(Identity(covering).Handle), new IntPtr(-1),
            identity.Bounds.Left, identity.Bounds.Top, identity.Bounds.Width, identity.Bounds.Height, NoActivate)
            .Should().BeTrue();
        OperationResult click = new ActionInvoker().ClickPoint(target.Id,
            button.X + button.Width / 2, button.Y + button.Height / 2,
            windowRelative: false, allowOutOfWindow: allowOutOfWindow);

        click.Success.Should().BeFalse();
        click.Error.Should().Contain("occluded");
        ReadCount(target).Should().Be("Count: 0");
        ReadCount(covering).Should().Be("Count: 0");
    }

    [Theory]
    [InlineData("move")]
    [InlineData("modal")]
    [InlineData("close")]
    public async Task Visual_action_refuses_a_changed_captured_window(string change)
    {
        Process process = Launch();
        WindowIdentity identity = Identity(process);
        StubGrounder grounder = new(() =>
        {
            if (change == "move")
            {
                NativeMethods.SetWindowPos(new IntPtr(identity.Handle), IntPtr.Zero,
                    identity.Bounds.Left + 20, identity.Bounds.Top, 0, 0, NoSize | NoZOrder | NoActivate)
                    .Should().BeTrue();
            }
            else if (change == "modal")
            {
                new ActionInvoker().Click(process.Id, new ElementLocator(AutomationId: "OpenDialogButton"))
                    .Success.Should().BeTrue();
                new ElementWaiter().Wait(process.Id, new ElementLocator(AutomationId: "NoteInput"), WaitCondition.Exists)
                    .Success.Should().BeTrue();
            }
            else
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000).Should().BeTrue();
            }
        });
        string json = await FindElementVisuallyTool.FindElementVisuallyAsync(
            grounder, new WindowCapture(), new ActionInvoker(), new SessionTarget(), "fixture", windowTitle: MainTitle, act: "single", processId: process.Id, cancellationToken: TestContext.Current.CancellationToken);
        using JsonDocument result = JsonDocument.Parse(json);
        result.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        result.RootElement.GetProperty("error").GetString().Should().Contain("Capture again");
        if (!process.HasExited) ReadCount(process).Should().Be("Count: 0");
    }

    [Fact]
    public async Task Refused_visual_click_is_an_outer_failure_with_grounding_preserved()
    {
        Process process = Launch();
        string json = await FindElementVisuallyTool.FindElementVisuallyAsync(
            new StubGrounder(x: 100000, y: 100000), new WindowCapture(), new ActionInvoker(), new SessionTarget(),
            "outside", windowTitle: MainTitle, act: "single", processId: process.Id, cancellationToken: TestContext.Current.CancellationToken);
        using JsonDocument result = JsonDocument.Parse(json);
        result.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        result.RootElement.GetProperty("data").GetProperty("click").GetProperty("success").GetBoolean().Should().BeFalse();
        result.RootElement.GetProperty("data").GetProperty("grounded").GetProperty("x").GetInt32().Should().Be(100000);
        ReadCount(process).Should().Be("Count: 0");
    }

    [Fact]
    public async Task Stable_visual_target_is_clicked_in_its_captured_coordinate_frame()
    {
        Process process = Launch();
        WindowIdentity identity = Identity(process);
        NativeMethods.SetWindowPos(new IntPtr(identity.Handle), new IntPtr(-1), identity.Bounds.Left, identity.Bounds.Top,
            identity.Bounds.Width, identity.Bounds.Height, 0).Should().BeTrue();
        ElementState button = Read(process, "IncrementButton");
        StubGrounder grounder = new(x: button.X + button.Width / 2 - identity.Bounds.Left,
            y: button.Y + button.Height / 2 - identity.Bounds.Top);
        string json = await FindElementVisuallyTool.FindElementVisuallyAsync(
            grounder, new WindowCapture(), new ActionInvoker(), new SessionTarget(), "increment", windowTitle: MainTitle, act: "single", processId: process.Id, cancellationToken: TestContext.Current.CancellationToken);
        using JsonDocument result = JsonDocument.Parse(json);
        result.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(json);
        new ElementWaiter().Wait(process.Id, new ElementLocator(Name: "Count: 1"), WaitCondition.Exists)
            .Success.Should().BeTrue();
    }

    [Fact]
    public void Batch_keeps_distinct_before_and_after_screenshot_evidence()
    {
        Process process = Launch();
        string json = RunStepsTool.RunSteps(Runner(), new DiagnosticsObserver(), new SessionTarget(), new[]
        {
            new BatchStep("screenshot", WindowTitle: MainTitle),
            new BatchStep("click", AutomationId: "IncrementButton"),
            new BatchStep("wait", Name: "Count: 1", Condition: "exists"),
            new BatchStep("screenshot", WindowTitle: MainTitle),
        }, processId: process.Id);
        using JsonDocument result = JsonDocument.Parse(json);
        result.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(json);
        JsonElement steps = result.RootElement.GetProperty("steps");
        string before = steps[0].GetProperty("data").GetProperty("path").GetString()!;
        string after = steps[3].GetProperty("data").GetProperty("path").GetString()!;
        before.Should().NotBe(after);
        SHA256.HashData(File.ReadAllBytes(before)).Should().NotEqual(SHA256.HashData(File.ReadAllBytes(after)));
    }

    private Process Launch(string? argument = null)
    {
        ProcessStartInfo start = new(SampleAppLocator.ResolveExecutablePath()) { UseShellExecute = false };
        if (argument is not null) start.ArgumentList.Add(argument);
        Process process = Process.Start(start) ?? throw new InvalidOperationException("Sample launch failed.");
        _processes.Add(process);
        new ElementWaiter().Wait(process.Id, new ElementLocator(AutomationId: "IncrementButton"), WaitCondition.Exists)
            .Success.Should().BeTrue();
        process.Refresh();
        return process;
    }

    private static ElementState Read(Process process, string automationId)
    {
        OperationResult state = new ElementStateReader().Read(process.Id, new ElementLocator(AutomationId: automationId));
        state.Success.Should().BeTrue(state.Error);
        return (ElementState)state.Data!;
    }
    private static string ReadCount(Process process) => Read(process, "CountLabel").Name;
    private static WindowIdentity Identity(Process process)
    {
        WindowGeometry.TryResolveWindow(process.Id, MainTitle, out WindowIdentity identity, out string? error).Should().BeTrue(error);
        return identity;
    }
    private static StepRunner Runner() => new(new ActionInvoker(), new ElementWaiter(), new ElementStateReader(), new WindowCapture());
    private void SetEnvironment(string key, string? value)
    {
        _environment.TryAdd(key, Environment.GetEnvironmentVariable(key));
        Environment.SetEnvironmentVariable(key, value);
    }
    public void Dispose()
    {
        foreach (Process process in _processes)
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
            process.Dispose();
        }
        foreach (KeyValuePair<string, string?> item in _environment) Environment.SetEnvironmentVariable(item.Key, item.Value);
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class StubGrounder(Action? afterCapture = null, int x = 20, int y = 20) : IElementGrounder
    {
        public Task<GroundedElement> FindAsync(string screenshotPath, string query, CancellationToken cancellationToken = default)
        {
            afterCapture?.Invoke();
            return Task.FromResult(new GroundedElement(x, y, 0, 0, 1, "test fixture", "stub"));
        }
    }
    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    }
}
