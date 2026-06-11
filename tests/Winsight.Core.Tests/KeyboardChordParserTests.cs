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

public class KeyboardChordParserTests
{
    [Fact]
    public void Parses_ctrl_shift_letter_chord_with_modifiers_first()
    {
        bool parsed = KeyboardChordParser.TryParse(
            "Ctrl+Shift+N",
            out VirtualKeyShort[] keys,
            out string? error);

        parsed.Should().BeTrue(error);
        keys.Should().Equal(
            VirtualKeyShort.CONTROL,
            VirtualKeyShort.SHIFT,
            VirtualKeyShort.KEY_N);
    }

    [Fact]
    public void Normalizes_modifiers_before_non_modifier_keys()
    {
        bool parsed = KeyboardChordParser.TryParse(
            "N+Ctrl",
            out VirtualKeyShort[] keys,
            out string? error);

        parsed.Should().BeTrue(error);
        keys.Should().Equal(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_N);
    }

    [Fact]
    public void Rejects_chords_without_non_modifier_key()
    {
        bool parsed = KeyboardChordParser.TryParse(
            "Ctrl+Shift",
            out _,
            out string? error);

        parsed.Should().BeFalse();
        error.Should().Contain("non-modifier");
    }

    [Fact]
    public void Rejects_unknown_key_tokens()
    {
        bool parsed = KeyboardChordParser.TryParse(
            "Ctrl+Hyper+N",
            out _,
            out string? error);

        parsed.Should().BeFalse();
        error.Should().Contain("Hyper");
    }
}
