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

using FlaUI.Core.WindowsAPI;
using FluentAssertions;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public class KeyChordDispatcherTests
{
    // Set-1 scan codes of a US/AZERTY keyboard, so the tests do not depend on
    // the layout of the machine that runs them.
    private static readonly Dictionary<VirtualKeyShort, ushort> ScanCodes = new()
    {
        [VirtualKeyShort.CONTROL] = 0x1D,
        [VirtualKeyShort.SHIFT] = 0x2A,
        [VirtualKeyShort.ALT] = 0x38,
        [VirtualKeyShort.PRIOR] = 0x49,
        [VirtualKeyShort.TAB] = 0x0F,
        [VirtualKeyShort.UP] = 0x48,
        [VirtualKeyShort.KEY_1] = 0x02,
        [VirtualKeyShort.LWIN] = 0xE05B,
    };

    [Fact]
    public void Ctrl_shift_pageup_is_sent_like_a_physical_keyboard()
    {
        IReadOnlyList<KeyTransition> transitions = KeyChordDispatcher.BuildTransitions(
            new[] { VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.PRIOR },
            key => ScanCodes[key]);

        transitions.Should().Equal(
            new KeyTransition(VirtualKeyShort.CONTROL, 0x1D, IsExtended: false, IsKeyUp: false),
            new KeyTransition(VirtualKeyShort.SHIFT, 0x2A, IsExtended: false, IsKeyUp: false),
            new KeyTransition(VirtualKeyShort.PRIOR, 0x49, IsExtended: true, IsKeyUp: false),
            new KeyTransition(VirtualKeyShort.PRIOR, 0x49, IsExtended: true, IsKeyUp: true),
            new KeyTransition(VirtualKeyShort.SHIFT, 0x2A, IsExtended: false, IsKeyUp: true),
            new KeyTransition(VirtualKeyShort.CONTROL, 0x1D, IsExtended: false, IsKeyUp: true));
    }

    [Fact]
    public void Every_transition_carries_a_scan_code()
    {
        IReadOnlyList<KeyTransition> transitions = KeyChordDispatcher.BuildTransitions(
            new[] { VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.TAB },
            key => ScanCodes[key]);

        transitions.Should().OnlyContain(t => t.ScanCode != 0);
        transitions.Should().OnlyContain(t => !t.IsExtended);
    }

    [Theory]
    [InlineData(VirtualKeyShort.PRIOR)]
    [InlineData(VirtualKeyShort.NEXT)]
    [InlineData(VirtualKeyShort.HOME)]
    [InlineData(VirtualKeyShort.END)]
    [InlineData(VirtualKeyShort.LEFT)]
    [InlineData(VirtualKeyShort.UP)]
    [InlineData(VirtualKeyShort.RIGHT)]
    [InlineData(VirtualKeyShort.DOWN)]
    [InlineData(VirtualKeyShort.INSERT)]
    [InlineData(VirtualKeyShort.DELETE)]
    public void Navigation_block_keys_are_extended_so_they_are_not_read_as_keypad_keys(VirtualKeyShort key)
    {
        KeyChordDispatcher.IsExtendedKey(key).Should().BeTrue();
    }

    [Theory]
    [InlineData(VirtualKeyShort.KEY_1)]
    [InlineData(VirtualKeyShort.TAB)]
    [InlineData(VirtualKeyShort.SHIFT)]
    [InlineData(VirtualKeyShort.CONTROL)]
    [InlineData(VirtualKeyShort.NUMPAD9)]
    public void Main_block_and_keypad_keys_are_not_extended(VirtualKeyShort key)
    {
        KeyChordDispatcher.IsExtendedKey(key).Should().BeFalse();
    }

    [Fact]
    public void A_mapped_e0_prefix_marks_the_key_extended_and_is_stripped_from_the_scan_code()
    {
        IReadOnlyList<KeyTransition> transitions = KeyChordDispatcher.BuildTransitions(
            new[] { VirtualKeyShort.LWIN, VirtualKeyShort.KEY_1 },
            key => ScanCodes[key]);

        transitions[0].Should().Be(new KeyTransition(VirtualKeyShort.LWIN, 0x5B, IsExtended: true, IsKeyUp: false));
    }

    [Fact]
    public void Modifiers_are_pressed_first_and_released_last_whatever_the_input_order()
    {
        IReadOnlyList<KeyTransition> transitions = KeyChordDispatcher.BuildTransitions(
            new[] { VirtualKeyShort.UP, VirtualKeyShort.SHIFT },
            key => ScanCodes[key]);

        transitions.Select(t => (t.VirtualKey, t.IsKeyUp)).Should().Equal(
            (VirtualKeyShort.SHIFT, false),
            (VirtualKeyShort.UP, false),
            (VirtualKeyShort.UP, true),
            (VirtualKeyShort.SHIFT, true));
    }
}
