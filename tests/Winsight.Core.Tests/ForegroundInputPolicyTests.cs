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
    public void Recognizes_default_rejecting_hosts_when_env_is_unset(string processName)
    {
        ForegroundInputPolicy.OverrideEnvReader = () => null;
        try
        {
            ForegroundInputPolicy.IsKnownRejectingHost(processName).Should().BeTrue();
        }
        finally
        {
            ForegroundInputPolicy.OverrideEnvReader = null;
        }
    }

    [Fact]
    public void Honors_custom_WINSIGHT_SYNTHETIC_INPUT_REJECTING_HOSTS_env_override()
    {
        ForegroundInputPolicy.OverrideEnvReader = () => "custom-host.exe, another_app";
        try
        {
            ForegroundInputPolicy.IsKnownRejectingHost("custom-host").Should().BeTrue();
            ForegroundInputPolicy.IsKnownRejectingHost("another_app.exe").Should().BeTrue();
            ForegroundInputPolicy.IsKnownRejectingHost("vmware").Should().BeFalse();
        }
        finally
        {
            ForegroundInputPolicy.OverrideEnvReader = null;
        }
    }

    [Fact]
    public void Explicit_empty_env_override_disables_default_rejecting_host_list()
    {
        ForegroundInputPolicy.OverrideEnvReader = () => string.Empty;
        try
        {
            ForegroundInputPolicy.ConfiguredRejectingHosts().Should().BeEmpty();
            ForegroundInputPolicy.IsKnownRejectingHost("vmware").Should().BeFalse();
        }
        finally
        {
            ForegroundInputPolicy.OverrideEnvReader = null;
        }
    }

    [Theory]
    [InlineData("send_key_chord")]
    [InlineData("type_keys")]
    [InlineData("type_text")]
    public void Keyboard_warning_is_non_blocking_and_reports_proven_workarounds(string action)
    {
        ForegroundInputPolicy.OverrideEnvReader = () => null;
        ForegroundInputPolicy.OverrideForegroundReader = () => new ForegroundProcessInfo(0x1234, 11152, "vmware");
        try
        {
            string? warning = ForegroundInputPolicy.BuildKeyboardWarning(action);

            warning.Should().NotBeNull();
            warning.Should().StartWith($"{ForegroundInputPolicy.ForegroundMayRejectSyntheticInputCode}:")
                .And.Contain("vmware")
                .And.Contain("11152")
                .And.Contain("powered-on VM")
                .And.Contain("vmsight")
                .And.Contain("act_verified")
                .And.NotContain("Preferences > Input")
                .And.NotContain("Enhanced Virtual Keyboard");
        }
        finally
        {
            ForegroundInputPolicy.OverrideForegroundReader = null;
            ForegroundInputPolicy.OverrideEnvReader = null;
        }
    }

    [Fact]
    public void Pointer_dispatch_returns_foreground_rejects_synthetic_input_with_proven_facts_only()
    {
        ForegroundInputPolicy.OverrideEnvReader = () => null;
        ForegroundInputPolicy.OverrideForegroundReader = () => new ForegroundProcessInfo(0x1234, 11152, "vmware");
        try
        {
            RejectingPointer pointer = new(new Point(817, 1332));

            OperationResult? refusal = PointerDispatch.Hover(new Point(1857, 44), holdMs: 0, settleMs: 0, driver: pointer);

            refusal.Should().NotBeNull();
            refusal!.Error.Should().StartWith($"{ForegroundInputPolicy.ForegroundRejectsSyntheticInputCode}:")
                .And.Contain("vmware")
                .And.Contain("set_cursor_pos res=0, err=0")
                .And.Contain("no known host preference fixes it")
                .And.Contain("vmsight")
                .And.NotContain("Preferences > Input")
                .And.NotContain("Enhanced Virtual Keyboard");
        }
        finally
        {
            ForegroundInputPolicy.OverrideForegroundReader = null;
            ForegroundInputPolicy.OverrideEnvReader = null;
        }
    }

    private sealed class RejectingPointer : IPointerDriver
    {
        private readonly Point _fixed;

        public RejectingPointer(Point initial) => _fixed = initial;

        public Point Position => _fixed;

        public PointerMoveResult MoveTo(Point point) =>
            new(1, 0, point.X, point.Y, new VirtualDesktopBounds(0, 0, 6000, 1440), PointerMoveMethods.SendInputVirtualDesk);

        public PointerMoveResult? ProbeSetCursorPos(Point point) =>
            new(0, 0, point.X, point.Y, new VirtualDesktopBounds(0, 0, 6000, 1440), PointerMoveMethods.SetCursorPos);

        public void Wait(int milliseconds) { }

        public void Click(MouseButton button) { }

        public void DoubleClick(MouseButton button) { }

        public void Scroll(int notches, bool horizontal) { }
    }
}
