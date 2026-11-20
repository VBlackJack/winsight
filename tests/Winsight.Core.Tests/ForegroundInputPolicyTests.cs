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

using System.Drawing;
using FlaUI.Core.Input;
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public sealed class ForegroundInputPolicyTests
{
    [Theory]
    [InlineData("vmware")]
    [InlineData("vmware.exe")]
    [InlineData("VMWARE")]
    [InlineData("vmware-vmx")]
    [InlineData("vmplayer")]
    public void Recognizes_vmware_processes_as_known_rejecting_hosts(string processName)
    {
        ForegroundInputPolicy.IsKnownRejectingHost(processName).Should().BeTrue();
        ForegroundInputPolicy.TryGetKnownHostHint(processName)
            .Should().Contain("Edit > Preferences > Input")
            .And.Contain("vmsight");
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("brave")]
    [InlineData("")]
    [InlineData(null)]
    public void Does_not_flag_ordinary_hosts(string? processName)
    {
        ForegroundInputPolicy.IsKnownRejectingHost(processName).Should().BeFalse();
    }

    [Theory]
    [InlineData("send_key_chord")]
    [InlineData("type_keys")]
    public void Keyboard_check_rejects_vmware_foreground_instead_of_silently_succeeding(string action)
    {
        ForegroundInputPolicy.OverrideForegroundReader = () => new ForegroundProcessInfo(0x1234, 11152, "vmware");
        try
        {
            OperationResult? result = ForegroundInputPolicy.CheckForegroundForKeyboardInput(action);

            result.Should().NotBeNull();
            result!.Success.Should().BeFalse();
            result.Error.Should().StartWith($"{ForegroundInputPolicy.ForegroundRejectsSyntheticInputCode}:")
                .And.Contain("vmware")
                .And.Contain("11152")
                .And.Contain("Edit > Preferences > Input")
                .And.Contain("vmsight");
        }
        finally
        {
            ForegroundInputPolicy.OverrideForegroundReader = null;
        }
    }

    [Fact]
    public void Pointer_dispatch_returns_foreground_rejects_synthetic_input_when_set_cursor_pos_returns_false()
    {
        ForegroundInputPolicy.OverrideForegroundReader = () => new ForegroundProcessInfo(0x1234, 11152, "vmware");
        try
        {
            RejectingPointer pointer = new(new Point(817, 1332));

            OperationResult? refusal = PointerDispatch.Hover(new Point(1857, 44), holdMs: 0, settleMs: 0, driver: pointer);

            refusal.Should().NotBeNull();
            refusal!.Error.Should().StartWith($"{ForegroundInputPolicy.ForegroundRejectsSyntheticInputCode}:")
                .And.Contain("vmware")
                .And.Contain("set_cursor_pos res=0, err=0")
                .And.Contain("Edit > Preferences > Input")
                .And.Contain("vmsight");
        }
        finally
        {
            ForegroundInputPolicy.OverrideForegroundReader = null;
        }
    }

    private sealed class RejectingPointer : IPointerDriver
    {
        private readonly Point _fixed;

        public RejectingPointer(Point initial) => _fixed = initial;

        public Point Position => _fixed;

        public PointerMoveResult MoveTo(Point point) =>
            new(1, 0, point.X, point.Y, new VirtualDesktopBounds(0, 0, 6000, 1440), PointerMoveMethods.SendInputVirtualDesk);

        public PointerMoveResult MoveToWithMethod(Point point, string method) =>
            method == PointerMoveMethods.SetCursorPos
                ? new(0, 0, point.X, point.Y, new VirtualDesktopBounds(0, 0, 6000, 1440), method)
                : new(1, 0, point.X, point.Y, new VirtualDesktopBounds(0, 0, 6000, 1440), method);

        public void Wait(int milliseconds) { }

        public void Click(MouseButton button) { }

        public void DoubleClick(MouseButton button) { }

        public void Scroll(int notches, bool horizontal) { }
    }
}
