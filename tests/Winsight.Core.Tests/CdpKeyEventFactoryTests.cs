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
using Winsight.Core.Modules.Cdp;
using Xunit;

namespace Winsight.Core.Tests;

public sealed class CdpKeyEventFactoryTests
{
    [Fact]
    public void CreateSequence_SingleCharacter_EmitsDownWithTextThenUp()
    {
        IReadOnlyList<CdpKeyEvent> events = CdpKeyEventFactory.CreateSequence("a");

        events.Should().HaveCount(2);
        events[0].EventType.Should().Be("keyDown");
        events[0].Text.Should().Be("a");
        events[0].Key.Should().Be("a");
        events[1].EventType.Should().Be("keyUp");
        events[1].Text.Should().BeNull();
    }

    [Fact]
    public void CreateSequence_Newline_BecomesEnterWithVirtualKeyCode()
    {
        IReadOnlyList<CdpKeyEvent> events = CdpKeyEventFactory.CreateSequence("\n");

        events.Should().HaveCount(2);
        events[0].Key.Should().Be("Enter");
        events[0].Code.Should().Be("Enter");
        events[0].Text.Should().Be("\r");
        events[0].WindowsVirtualKeyCode.Should().Be(13);
        events[1].EventType.Should().Be("keyUp");
        events[1].WindowsVirtualKeyCode.Should().Be(13);
    }

    [Fact]
    public void CreateSequence_CrLf_CollapsesToSingleEnterPress()
    {
        IReadOnlyList<CdpKeyEvent> events = CdpKeyEventFactory.CreateSequence("\r\n");

        events.Should().HaveCount(2);
        events[0].Key.Should().Be("Enter");
    }

    [Fact]
    public void CreateSequence_Tab_BecomesTabKey()
    {
        IReadOnlyList<CdpKeyEvent> events = CdpKeyEventFactory.CreateSequence("\t");

        events[0].Key.Should().Be("Tab");
        events[0].WindowsVirtualKeyCode.Should().Be(9);
    }

    [Fact]
    public void CreateSequence_CommandWithNewline_OrdersEventsCorrectly()
    {
        IReadOnlyList<CdpKeyEvent> events = CdpKeyEventFactory.CreateSequence("ls\n");

        events.Should().HaveCount(6);
        events.Select(e => e.Key).Should().ContainInOrder("l", "l", "s", "s", "Enter", "Enter");
    }

    [Fact]
    public void CreateSequence_EmptyString_ReturnsEmptySequence()
    {
        CdpKeyEventFactory.CreateSequence(string.Empty).Should().BeEmpty();
    }
}
