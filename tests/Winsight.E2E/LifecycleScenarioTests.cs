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

namespace Winsight.E2E;

/// <summary>
/// Exercises the launch/close lifecycle: launch_app must return only once the
/// app's window is present (no manual poll), with a classified descriptor, and
/// close_app must tear the process down.
/// </summary>
[Trait("Category", "E2E")]
public class LifecycleScenarioTests
{
    private const string MainWindowTitleFragment = "Counter";

    [Fact]
    public void Launch_waits_for_the_window_then_close_tears_it_down()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        var launcher = new AppLauncher(new WindowDiscovery());

        OperationResult launched = launcher.Launch(
            executablePath, waitForWindowTitle: MainWindowTitleFragment);
        launched.Success.Should().BeTrue(launched.Error);

        var result = (LaunchResult)launched.Data!;
        int processId = result.ProcessId;

        try
        {
            // The window is already present and classified when Launch returns.
            result.Window.Title.Should().Contain(MainWindowTitleFragment);
            result.Window.Kind.Should().Be("main");
            result.Window.Width.Should().BeGreaterThan(0);
            Process.GetProcessById(processId).HasExited.Should().BeFalse();
        }
        finally
        {
            OperationResult closed = launcher.Close(processId, force: true);
            closed.Success.Should().BeTrue(closed.Error);
        }

        // After close_app the process is gone.
        WaitUntilGone(processId, TimeSpan.FromSeconds(5))
            .Should().BeTrue("close_app should have terminated the process");
    }

    [Fact]
    public void Launch_fails_clearly_when_the_executable_is_missing()
    {
        var launcher = new AppLauncher(new WindowDiscovery());

        OperationResult launched = launcher.Launch(
            Path.Combine(Path.GetTempPath(), "winsight-does-not-exist.exe"));

        launched.Success.Should().BeFalse();
        launched.Error.Should().Contain("not found");
    }

    private static bool WaitUntilGone(int processId, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (Process.GetProcessById(processId).HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }
            Thread.Sleep(100);
        }
        return false;
    }
}
