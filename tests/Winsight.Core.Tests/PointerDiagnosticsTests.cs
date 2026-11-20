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
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public sealed class PointerDiagnosticsTests
{
    [Fact]
    public void Decodes_cursor_showing_and_suppressed_flags()
    {
        CursorStateInfo showing = PointerDiagnostics.DecodeCursorInfo(0x00000001, 100, 200);
        showing.Showing.Should().BeTrue();
        showing.Suppressed.Should().BeFalse();

        CursorStateInfo suppressed = PointerDiagnostics.DecodeCursorInfo(0x00000002, 100, 200);
        suppressed.Showing.Should().BeFalse();
        suppressed.Suppressed.Should().BeTrue();

        CursorStateInfo hidden = PointerDiagnostics.DecodeCursorInfo(0x00000000, 100, 200);
        hidden.Showing.Should().BeFalse();
        hidden.Suppressed.Should().BeFalse();
    }

    [Fact]
    public void Reads_live_environment_snapshots_without_moving_cursor()
    {
        PointerDiagnostics.ReadClipCursor().Should().NotBeNull();
        PointerDiagnostics.ReadMouseAcceleration().Should().NotBeNull();
        PointerDiagnostics.ReadMouseButtonsDown().Should().NotBeNull();
        PointerDiagnostics.QueryPrimaryMonitorBounds().Width.Should().BeGreaterThan(0);

        if (InputContext.Capture().DesktopMatchesInput)
        {
            PointerDiagnostics.ReadCursorInfo().Should().NotBeNull();
        }
    }

    [Fact]
    public void Fallback_order_covers_all_secondary_move_methods_in_priority_order()
    {
        PointerMoveMethods.AllOrdered.Should().Equal(
            PointerMoveMethods.SendInputVirtualDesk,
            PointerMoveMethods.SendInputPrimaryAbsolute,
            PointerMoveMethods.SendInputRelative,
            PointerMoveMethods.SetCursorPos);

        PointerMoveMethods.FallbackOrder.Should().Equal(
            PointerMoveMethods.SendInputPrimaryAbsolute,
            PointerMoveMethods.SendInputRelative,
            PointerMoveMethods.SetCursorPos);
    }

    [Fact]
    public void Run_diagnostic_tries_all_four_methods_in_order_and_restores_cursor_each_time()
    {
        FakeDiagnosticDriver driver = new(new Point(817, 1332), workingMethod: PointerMoveMethods.SetCursorPos);

        PointerDiagnosticReport report = PointerDiagnostics.RunDiagnostic(new Point(1857, 44), driver);

        report.TargetX.Should().Be(1857);
        report.TargetY.Should().Be(44);
        report.FirstWorkingMethod.Should().Be(PointerMoveMethods.SetCursorPos);
        report.Attempts.Should().HaveCount(4);
        report.Attempts.Select(a => a.Method).Should().Equal(PointerMoveMethods.AllOrdered);
        report.Attempts.Take(3).Should().OnlyContain(a => !a.ReachedTarget && a.Restored);
        report.Attempts[3].ReachedTarget.Should().BeTrue();
        report.Attempts[3].Restored.Should().BeTrue();
        driver.Cursor.Should().Be(new Point(817, 1332), "original cursor position must be restored after every attempt");
        driver.Waits.Should().Equal(
            PointerDiagnostics.FirstSampleDelayMs, PointerDiagnostics.SecondSampleDelayMs,
            PointerDiagnostics.FirstSampleDelayMs, PointerDiagnostics.SecondSampleDelayMs,
            PointerDiagnostics.FirstSampleDelayMs, PointerDiagnostics.SecondSampleDelayMs,
            PointerDiagnostics.FirstSampleDelayMs, PointerDiagnostics.SecondSampleDelayMs);
    }

    private sealed class FakeDiagnosticDriver : IPointerDiagnosticDriver
    {
        private readonly string _workingMethod;

        public FakeDiagnosticDriver(Point start, string workingMethod)
        {
            Cursor = start;
            _workingMethod = workingMethod;
        }

        public Point Cursor { get; private set; }
        public List<int> Waits { get; } = new();

        public InputContextSnapshot CaptureContext() =>
            new(1, 1, true, InputContext.InteractiveWindowStationName, true, "Default", "Default", null, true, false, 0, Array.Empty<string>(), null, false, false, "medium (0x2000)", Array.Empty<ProcessChainEntry>(), 42);
        public CursorPoint? ReadCursorPos() => new(Cursor.X, Cursor.Y);
        public CursorPoint? ReadPhysicalCursorPos() => new(Cursor.X, Cursor.Y);
        public PointerClipRect? ReadClipCursor() => new(0, 0, 6000, 1440, 6000, 1440);
        public CursorStateInfo? ReadCursorInfo() => new(1, true, false, Cursor.X, Cursor.Y);
        public IReadOnlyList<string> ReadMouseButtonsDown() => Array.Empty<string>();
        public MouseAccelerationInfo? ReadMouseAcceleration() => new(6, 10, 1, 10);
        public ForegroundProcessInfo? ReadForegroundProcess() => new(0x100, 1234, "brave");
        public VirtualDesktopBounds QueryVirtualDesktop() => new(0, 0, 6000, 1440);
        public VirtualDesktopBounds QueryPrimaryMonitor() => new(0, 0, 3440, 1440);
        public string DescribeDpiAwareness() => "per_monitor_aware_v2";

        public PointerMoveResult MoveWithMethod(Point target, Point current, string method)
        {
            if (method == _workingMethod || method == PointerMoveMethods.SetCursorPos)
            {
                Cursor = target;
                return new PointerMoveResult(1, 0, target.X, target.Y, QueryVirtualDesktop(), method);
            }
            return new PointerMoveResult(1, 0, target.X, target.Y, QueryVirtualDesktop(), method);
        }

        public void Wait(int milliseconds) => Waits.Add(milliseconds);
    }
}
