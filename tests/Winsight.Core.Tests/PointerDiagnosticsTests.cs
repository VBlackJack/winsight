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
}
