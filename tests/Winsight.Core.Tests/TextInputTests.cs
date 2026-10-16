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
using Winsight.Core.Modules.Clipboard;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

/// <summary>
/// type_keys planning and pacing, its mode parser, and the clipboard gate.
/// Nothing here touches the real keyboard or clipboard.
/// </summary>
public sealed class TextInputTests
{
    [Fact]
    public void Characters_become_unicode_strokes_and_line_breaks_become_enter()
    {
        IReadOnlyList<TextStroke> strokes = TextTyper.Plan("a\r\nb\nc\rd\te");

        strokes.Should().Equal(
            TextStroke.Character('a'),
            TextStroke.Press(VirtualKeyShort.RETURN),
            TextStroke.Character('b'),
            TextStroke.Press(VirtualKeyShort.RETURN),
            TextStroke.Character('c'),
            TextStroke.Press(VirtualKeyShort.RETURN),
            TextStroke.Character('d'),
            TextStroke.Press(VirtualKeyShort.TAB),
            TextStroke.Character('e'));
    }

    [Fact]
    public void Characters_outside_the_bmp_are_sent_as_their_two_surrogates()
    {
        IReadOnlyList<TextStroke> strokes = TextTyper.Plan("\U0001F600");

        strokes.Should().HaveCount(2);
        char.IsHighSurrogate(strokes[0].Unit).Should().BeTrue();
        char.IsLowSurrogate(strokes[1].Unit).Should().BeTrue();
    }

    [Fact]
    public void Typing_stops_when_the_target_loses_the_foreground()
    {
        IReadOnlyList<TextStroke> strokes = TextTyper.Plan("abcd");
        List<TextStroke> sent = new();
        int checks = 0;

        string? stopped = TextTyper.Send(strokes, () => ++checks <= 2, sent.Add);

        sent.Should().HaveCount(2, "the third stroke would have landed in another window");
        stopped.Should().StartWith("foreground_changed:").And.Contain("after 2 of 4");
    }

    [Fact]
    public void All_strokes_are_sent_while_the_target_keeps_the_foreground()
    {
        List<TextStroke> sent = new();

        TextTyper.Send(TextTyper.Plan("ok"), () => true, sent.Add).Should().BeNull();
        sent.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_text_is_refused(string? text)
    {
        TextTyper.Validate(text).Should().Contain("required");
    }

    [Fact]
    public void Text_longer_than_the_limit_is_refused()
    {
        TextTyper.Validate(new string('x', TextTyper.MaxLength + 1)).Should().Contain(TextTyper.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        TextTyper.Validate(new string('x', TextTyper.MaxLength)).Should().BeNull();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("keys", false)]
    [InlineData(" Paste ", true)]
    public void Known_modes_parse(string? raw, bool expectedPaste)
    {
        TypeKeysMode.TryParse(raw, out bool paste, out string? error).Should().BeTrue();
        paste.Should().Be(expectedPaste);
        error.Should().BeNull();
    }

    [Fact]
    public void Unknown_mode_lists_the_accepted_values()
    {
        TypeKeysMode.TryParse("shout", out _, out string? error).Should().BeFalse();
        error.Should().Contain("keys").And.Contain("paste");
    }

    [Fact]
    public void The_clipboard_stays_off_until_allowed()
    {
        ClipboardPolicy.Refusal("read_clipboard_text", allowed: false).Should()
            .StartWith("clipboard_disabled:").And.Contain(ClipboardPolicy.AllowVariable);
        ClipboardPolicy.Refusal("read_clipboard_text", allowed: true).Should().BeNull();
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData(" TRUE ", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    [InlineData("maybe", false)]
    public void Allow_values_are_the_usual_truthy_words(string? raw, bool expected)
    {
        ClipboardPolicy.IsTruthy(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(13u, true)]     // CF_UNICODETEXT
    [InlineData(8u, true)]      // CF_DIB
    [InlineData(15u, true)]     // CF_HDROP
    [InlineData(0xC123u, true)] // a registered format
    [InlineData(2u, false)]     // CF_BITMAP
    [InlineData(14u, false)]    // CF_ENHMETAFILE
    [InlineData(0x0250u, false)] // private range
    [InlineData(0x0350u, false)] // GDI object range
    public void Only_memory_block_formats_are_saved(uint format, bool saved)
    {
        ClipboardService.IsMemoryFormat(format).Should().Be(saved);
    }
}
