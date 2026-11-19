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
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;

namespace Winsight.Core.Modules.Lifecycle;

/// <summary>
/// Launches and tears down applications under test. Launch waits until the
/// target's window is actually present before returning, so the agent never
/// races a still-initializing UI: the returned <see cref="LaunchResult"/>
/// carries a classified window descriptor ready to drive.
/// </summary>
public sealed class AppLauncher
{
    public const int DefaultLaunchTimeoutMs = 15_000;
    internal const int MaxLaunchTimeoutMs = 120_000;
    internal const int DefaultCloseGraceMs = 5_000;
    private const int WindowPollIntervalMs = 150;

    private readonly WindowDiscovery _discovery;

    public AppLauncher(WindowDiscovery discovery)
    {
        _discovery = discovery;
    }

    /// <summary>
    /// Start an executable and wait until its window appears. When
    /// <paramref name="waitForWindowTitle"/> is set, the launcher waits for a
    /// window whose title contains it; otherwise it waits for the process's main
    /// window. Fails if the process exits early or no window appears in time.
    /// </summary>
    public OperationResult Launch(
        string path,
        string? arguments = null,
        string? workingDirectory = null,
        string? waitForWindowTitle = null,
        int timeoutMs = DefaultLaunchTimeoutMs)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return OperationResult.Failure("path is required.");
        }
        if (!File.Exists(path))
        {
            return OperationResult.Failure($"Executable not found: \"{path}\".");
        }
        if (timeoutMs <= 0 || timeoutMs > MaxLaunchTimeoutMs)
        {
            return OperationResult.Failure(
                $"timeoutMs must be between 1 and {MaxLaunchTimeoutMs}.");
        }

        ProcessStartInfo startInfo = new(path)
        {
            UseShellExecute = false,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Path.GetDirectoryName(path) ?? string.Empty
                : workingDirectory,
        };
        if (!string.IsNullOrEmpty(arguments))
        {
            startInfo.Arguments = arguments;
        }

        Process startedProcess;
        try
        {
            startedProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return OperationResult.Failure($"Failed to start \"{path}\": {ex.Message}");
        }

        using Process process = startedProcess;

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds <= timeoutMs)
        {
            process.Refresh();
            if (process.HasExited)
            {
                return OperationResult.Failure(
                    $"Process {process.Id} exited during startup with code {process.ExitCode} " +
                    "before a window appeared.");
            }

            WindowDescriptor? window = PickWindow(process.Id, waitForWindowTitle);
            if (window is not null)
            {
                return OperationResult.OkWith(new LaunchResult(process.Id, window));
            }

            Thread.Sleep(WindowPollIntervalMs);
        }

        string target = waitForWindowTitle is null
            ? "a main window"
            : $"a window whose title contains \"{waitForWindowTitle}\"";
        return OperationResult.Failure(
            $"Process {process.Id} started but {target} did not appear within {timeoutMs} ms.");
    }

    /// <summary>
    /// Close a process. By default this requests a graceful shutdown
    /// (CloseMainWindow) and reports a still-running process if it does not exit
    /// within the grace window; only <paramref name="force"/> permits killing.
    /// </summary>
    public OperationResult Close(int processId, bool force = false)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            // No such process: the desired end state (gone) already holds.
            return OperationResult.OkWith(new { processId, alreadyExited = true });
        }

        using (process)
        {
            if (force)
            {
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(DefaultCloseGraceMs))
                {
                    return OperationResult.FailureWith(
                        $"Process {processId} has not exited after the forced termination request.",
                        new { processId, stillRunning = true, forced = true });
                }
                return OperationResult.OkWith(new { processId, forced = true });
            }

            bool requested = process.CloseMainWindow();
            if (requested && process.WaitForExit(DefaultCloseGraceMs))
            {
                return OperationResult.OkWith(new { processId, forced = false });
            }

            return OperationResult.FailureWith(
                $"Process {processId} is still running. Complete any close/save confirmation, " +
                "or explicitly request force=true to terminate it.",
                new { processId, stillRunning = true, closeRequested = requested, forced = false });
        }
    }

    private WindowDescriptor? PickWindow(int processId, string? waitForWindowTitle)
    {
        IReadOnlyList<WindowDescriptor> windows = _discovery.List(processId);
        if (waitForWindowTitle is not null)
        {
            return windows.FirstOrDefault(
                w => w.Title.Contains(waitForWindowTitle, StringComparison.OrdinalIgnoreCase));
        }

        return windows.FirstOrDefault(w => w.Kind == WindowClassifier.KindMain)
            ?? windows.FirstOrDefault(w => !string.IsNullOrEmpty(w.Title));
    }
}
