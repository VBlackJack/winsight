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

using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public sealed class InputContextTests
{
    [Fact]
    public void Capture_reads_live_process_input_context()
    {
        InputContextSnapshot context = InputContext.Capture();

        context.SessionId.Should().NotBeNull();
        context.WindowStation.Should().NotBeNullOrWhiteSpace();
        context.ThreadDesktop.Should().NotBeNullOrWhiteSpace();
        context.IntegrityLevel.Should().NotBeNullOrWhiteSpace();
        context.ParentChain.Should().NotBeEmpty();
    }

    [Fact]
    public void Decodes_job_ui_restriction_flags()
    {
        uint mask = InputContext.JobUiLimitHandles | InputContext.JobUiLimitDesktop | InputContext.JobUiLimitReadClipboard;

        IReadOnlyList<string> flags = InputContext.DecodeJobUiRestrictions(mask);

        flags.Should().Equal(
            "JOB_OBJECT_UILIMIT_HANDLES",
            "JOB_OBJECT_UILIMIT_READCLIPBOARD",
            "JOB_OBJECT_UILIMIT_DESKTOP");
    }

    [Fact]
    public void Evaluate_block_returns_null_for_interactive_unrestricted_context()
    {
        InputContextSnapshot healthy = MakeContext();

        InputContext.EvaluateBlock(healthy).Should().BeNull();
    }

    [Fact]
    public void Evaluate_block_detects_job_ui_restrictions()
    {
        InputContextSnapshot restricted = MakeContext(
            inJob: true,
            jobUiMask: InputContext.JobUiLimitHandles | InputContext.JobUiLimitDesktop);

        InputContextBlock? block = InputContext.EvaluateBlock(restricted);

        block.Should().NotBeNull();
        block!.Code.Should().Be(InputContext.InputContextBlockedCode);
        block.Restrictions.Should().Contain("JOB_OBJECT_UILIMIT_HANDLES")
            .And.Contain("JOB_OBJECT_UILIMIT_DESKTOP");
        block.Message.Should().StartWith($"{InputContext.InputContextBlockedCode}:")
            .And.Contain("JOB_OBJECT_UILIMIT_HANDLES")
            .And.Contain("JOB_OBJECT_UILIMIT_DESKTOP");
    }

    [Fact]
    public void Evaluate_block_detects_non_interactive_window_station()
    {
        InputContextSnapshot serviceStation = MakeContext(windowStation: "Service-0x0-3e7$", windowStationVisible: false);

        InputContextBlock? block = InputContext.EvaluateBlock(serviceStation);

        block.Should().NotBeNull();
        block!.Code.Should().Be(InputContext.InputContextBlockedCode);
        block.Message.Should().Contain("non_interactive_window_station:Service-0x0-3e7$");
    }

    private static InputContextSnapshot MakeContext(
        string windowStation = "WinSta0",
        bool windowStationVisible = true,
        string threadDesktop = "Default",
        string inputDesktop = "Default",
        bool inJob = false,
        uint jobUiMask = 0) =>
        new(
            SessionId: 1,
            ActiveConsoleSessionId: 1,
            SessionMatchesConsole: true,
            WindowStation: windowStation,
            WindowStationVisible: windowStationVisible,
            ThreadDesktop: threadDesktop,
            InputDesktop: inputDesktop,
            InputDesktopWin32Error: null,
            DesktopMatchesInput: string.Equals(threadDesktop, inputDesktop, StringComparison.OrdinalIgnoreCase),
            InJob: inJob,
            JobUiRestrictionsMask: jobUiMask,
            JobUiRestrictions: InputContext.DecodeJobUiRestrictions(jobUiMask),
            JobQueryWin32Error: null,
            IsRestrictedToken: false,
            IsAppContainer: false,
            IntegrityLevel: "medium (0x2000)",
            ParentChain: [new ProcessChainEntry(100, "explorer.exe")]);
}
