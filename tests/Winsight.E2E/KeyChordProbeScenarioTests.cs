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

namespace Winsight.E2E;

/// <summary>
/// send_key_chord against the three places a WPF shell reads keys: a
/// WH_KEYBOARD_LL hook that reads modifiers with GetAsyncKeyState, the
/// WM_KEYDOWN window message with GetKeyState, and the WPF KeyDown event.
/// A chord only counts as delivered when every path sees it complete, and an
/// extended key (PageUp, arrows, Home/End) must arrive flagged as extended,
/// not as its numeric-keypad twin. WPF consumes Tab navigation while it
/// preprocesses the message, so Tab chords never reach the window procedure,
/// exactly as with a physical keyboard: the message path is not asserted there.
/// </summary>
[Trait("Category", "E2E")]
public class KeyChordProbeScenarioTests
{
    private const string HookChordId = "LastHookChord";
    private const string MessageChordId = "LastMessageChord";
    private const string MessageFlagsId = "LastMessageFlags";
    private const string HookFlagsId = "LastHookFlags";
    private const string WpfChordId = "LastWpfChord";
    private const string ExtendedMarker = "extended=True";

    public static TheoryData<string, bool, bool> Chords() => new()
    {
        { "Ctrl+Shift+PageUp", true, true },
        { "Ctrl+Shift+PageDown", true, true },
        { "Ctrl+PageUp", true, true },
        { "Ctrl+Shift+Home", true, true },
        { "Ctrl+Shift+End", true, true },
        { "Shift+Up", true, true },
        { "Ctrl+Shift+Tab", false, false },
        { "Ctrl+1", false, true },
        { "Ctrl+9", false, true },
    };

    [Theory]
    [MemberData(nameof(Chords))]
    public void Chord_reaches_hook_message_and_wpf_complete(string chord, bool extended, bool reachesWindowProc)
    {
        using WpfLabSession lab = WpfLabSession.Start();

        OperationResult sent = new ActionInvoker().SendKeyChord(lab.ProcessId, chord);
        sent.Success.Should().BeTrue(sent.Error);

        bool hook = lab.WaitForName(HookChordId, chord);
        bool message = !reachesWindowProc || lab.WaitForName(MessageChordId, chord, timeoutMs: 500);
        bool wpf = lab.WaitForName(WpfChordId, chord, timeoutMs: 500);
        string diagnostics =
            $"hook=\"{lab.ReadName(HookChordId)}\" ({lab.ReadName(HookFlagsId)}), " +
            $"message=\"{lab.ReadName(MessageChordId)}\" ({lab.ReadName(MessageFlagsId)}), " +
            $"wpf=\"{lab.ReadName(WpfChordId)}\"{Environment.NewLine}{lab.ReadKeyLog()}";

        (hook && message && wpf).Should().BeTrue($"{chord} must arrive complete everywhere: {diagnostics}");
        if (extended)
        {
            lab.ReadName(HookFlagsId).Should().Contain(ExtendedMarker, diagnostics);
            lab.ReadName(MessageFlagsId).Should().Contain(ExtendedMarker, diagnostics);
        }
    }
}
