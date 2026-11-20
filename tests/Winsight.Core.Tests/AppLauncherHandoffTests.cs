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
using Winsight.Core.Modules.Lifecycle;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public sealed class AppLauncherHandoffTests
{
    private sealed class FakeLaunchedProcess(int id, int exitCode, int exitAfterRefreshCount) : ILaunchedProcess
    {
        private int _refreshes;

        public int Id => id;
        public bool HasExited { get; private set; }
        public int ExitCode => exitCode;

        public void Refresh()
        {
            _refreshes++;
            if (exitAfterRefreshCount >= 0 && _refreshes >= exitAfterRefreshCount)
            {
                HasExited = true;
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeLaunchEnvironment : ILaunchEnvironment
    {
        private readonly Func<ProcessStartInfo, ILaunchedProcess> _starter;
        private readonly Func<long, IReadOnlyList<WindowDescriptor>> _windowsAtTime;
        private long _elapsedMs;

        public FakeLaunchEnvironment(
            Func<ProcessStartInfo, ILaunchedProcess> starter,
            Func<long, IReadOnlyList<WindowDescriptor>> windowsAtTime)
        {
            _starter = starter;
            _windowsAtTime = windowsAtTime;
        }

        public bool FileExists(string path) => true;

        public ILaunchedProcess Start(ProcessStartInfo startInfo) => _starter(startInfo);

        public IReadOnlyList<WindowDescriptor> ListWindows(int? processId)
        {
            IReadOnlyList<WindowDescriptor> all = _windowsAtTime(_elapsedMs);
            return processId is int pid
                ? all.Where(w => w.ProcessId == pid).ToList()
                : all;
        }

        public long ElapsedMs() => _elapsedMs;

        public void Sleep(int milliseconds) => _elapsedMs += milliseconds;
    }

    private static WindowDescriptor Window(int pid, long handle, string title, string processName) =>
        new(pid, handle, title, processName, true, 0, 0, 800, 600, WindowClassifier.KindMain, "WindowClass");

    [Fact]
    public void Stub_launcher_exiting_with_code_0_binds_to_handed_off_window_and_reports_handedOffToProcessId()
    {
        WindowDescriptor existing = Window(100, 0x10, "Terminal", "WindowsTerminal");
        WindowDescriptor packagedNotepad = Window(4242, 0x20, "Untitled - Notepad", "Notepad");

        FakeLaunchEnvironment env = new(
            _ => new FakeLaunchedProcess(id: 3001, exitCode: 0, exitAfterRefreshCount: 1),
            elapsed => elapsed >= 300 ? [existing, packagedNotepad] : [existing]);
        AppLauncher launcher = new(env);

        OperationResult result = launcher.Launch(@"C:\Windows\System32\notepad.exe", timeoutMs: 2_000);

        result.Success.Should().BeTrue();
        LaunchResult launched = result.Data.Should().BeOfType<LaunchResult>().Subject;
        launched.ProcessId.Should().Be(4242);
        launched.HandedOffToProcessId.Should().Be(4242);
        launched.Window.WindowHandle.Should().Be(0x20);
    }

    [Fact]
    public void Stub_launcher_handoff_respects_waitForWindowTitle()
    {
        WindowDescriptor existing = Window(100, 0x10, "Terminal", "WindowsTerminal");
        WindowDescriptor unrelatedNew = Window(5000, 0x15, "Update Notification", "Updater");
        WindowDescriptor targetWindow = Window(6000, 0x25, "Notes.txt - Notepad", "Notepad");

        FakeLaunchEnvironment env = new(
            _ => new FakeLaunchedProcess(id: 3002, exitCode: 0, exitAfterRefreshCount: 1),
            elapsed => elapsed switch
            {
                < 150 => [existing],
                < 450 => [existing, unrelatedNew],
                _ => [existing, unrelatedNew, targetWindow],
            });
        AppLauncher launcher = new(env);

        OperationResult result = launcher.Launch(
            @"C:\Windows\System32\notepad.exe",
            waitForWindowTitle: "Notes.txt",
            timeoutMs: 2_000);

        result.Success.Should().BeTrue();
        LaunchResult launched = result.Data.Should().BeOfType<LaunchResult>().Subject;
        launched.ProcessId.Should().Be(6000);
        launched.HandedOffToProcessId.Should().Be(6000);
        launched.Window.Title.Should().Be("Notes.txt - Notepad");
    }

    [Fact]
    public void Stub_launcher_exiting_with_nonzero_code_keeps_existing_error()
    {
        FakeLaunchEnvironment env = new(
            _ => new FakeLaunchedProcess(id: 3003, exitCode: 2, exitAfterRefreshCount: 1),
            _ => []);
        AppLauncher launcher = new(env);

        OperationResult result = launcher.Launch(@"C:\app\broken.exe", timeoutMs: 1_000);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("exited during startup with code 2");
    }

    [Fact]
    public void Stub_launcher_exiting_with_code_0_and_no_new_window_keeps_existing_error()
    {
        WindowDescriptor existing = Window(100, 0x10, "Terminal", "WindowsTerminal");
        FakeLaunchEnvironment env = new(
            _ => new FakeLaunchedProcess(id: 3004, exitCode: 0, exitAfterRefreshCount: 1),
            _ => [existing]);
        AppLauncher launcher = new(env);

        OperationResult result = launcher.Launch(@"C:\Windows\System32\notepad.exe", timeoutMs: 600);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("exited during startup with code 0 before a window appeared");
    }

    [Fact]
    public void Direct_launch_without_handoff_leaves_handedOffToProcessId_null()
    {
        WindowDescriptor ownWindow = Window(7001, 0x30, "Sample App", "SampleApp");
        FakeLaunchEnvironment env = new(
            _ => new FakeLaunchedProcess(id: 7001, exitCode: 0, exitAfterRefreshCount: -1),
            elapsed => elapsed >= 150 ? [ownWindow] : []);
        AppLauncher launcher = new(env);

        OperationResult result = launcher.Launch(@"C:\app\SampleApp.exe", timeoutMs: 1_000);

        result.Success.Should().BeTrue();
        LaunchResult launched = result.Data.Should().BeOfType<LaunchResult>().Subject;
        launched.ProcessId.Should().Be(7001);
        launched.HandedOffToProcessId.Should().BeNull();
    }
}
