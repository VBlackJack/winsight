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

namespace Winsight.E2E;

/// <summary>
/// Launches the WPF lab for one scenario, waits until its UI is ready and
/// kills it on dispose, so each scenario starts from the same window state.
/// </summary>
internal sealed class WpfLabSession : IDisposable
{
    private const string ReadyElementId = "LastHookChord";

    private readonly Process _process;

    private WpfLabSession(Process process)
    {
        _process = process;
    }

    public int ProcessId => _process.Id;

    public static WpfLabSession Start()
    {
        string executablePath = SampleAppLocator.ResolveLabExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the WPF lab must be built before running E2E tests (looked at: {executablePath})");
        Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start the WPF lab.");
        WpfLabSession session = new(process);
        OperationResult ready = new ElementWaiter().Wait(
            process.Id, new ElementLocator(AutomationId: ReadyElementId), WaitCondition.Exists);
        ready.Success.Should().BeTrue(ready.Error);
        return session;
    }

    /// <summary>Current Name of a lab element, or the lookup error.</summary>
    public string ReadName(string automationId)
    {
        OperationResult state = new ElementStateReader().Read(ProcessId, new ElementLocator(AutomationId: automationId));
        return state.Success ? ((ElementState)state.Data!).Name : $"<{state.Error}>";
    }

    /// <summary>Wait until the element named by its AutomationId carries exactly this Name.</summary>
    public bool WaitForName(string automationId, string expectedName, int timeoutMs = 3000)
    {
        OperationResult result = new ElementWaiter().Wait(
            ProcessId,
            new ElementLocator(AutomationId: automationId, Name: expectedName),
            WaitCondition.Exists,
            timeoutMs: timeoutMs);
        return result.Success;
    }

    /// <summary>Most recent probe log lines (newest first), for failure diagnostics.</summary>
    public string ReadKeyLog(int maxLines = 24)
    {
        UiElementNode root = new UiTreeInspector().Inspect(
            ProcessId, maxDepth: UiTreeInspector.MaxAllowedDepth, controlTypeFilter: "ListItem");
        List<string> lines = new();
        Collect(root, lines, maxLines);
        return string.Join(Environment.NewLine, lines);
    }

    private static void Collect(UiElementNode node, List<string> lines, int maxLines)
    {
        if (lines.Count >= maxLines)
        {
            return;
        }
        if (string.Equals(node.ControlType, "ListItem", StringComparison.Ordinal))
        {
            lines.Add(node.Name);
        }
        foreach (UiElementNode child in node.Children)
        {
            Collect(child, lines, maxLines);
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        _process.Dispose();
    }
}
