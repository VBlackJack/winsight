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

namespace Winsight.Core.Modules.Cdp;

/// <summary>
/// Translates plain text into the keyDown/keyUp event sequence that
/// <c>Input.dispatchKeyEvent</c> expects. Sending real key events (rather
/// than <c>Input.insertText</c>) is what makes typing work against
/// keydown-driven surfaces such as xterm.js terminals embedded in WebView2.
/// </summary>
public static class CdpKeyEventFactory
{
    private const string KeyDown = "keyDown";
    private const string KeyUp = "keyUp";

    private const string EnterKey = "Enter";
    private const string EnterCode = "Enter";
    private const string EnterText = "\r";
    private const int EnterVirtualKeyCode = 13;

    private const string TabKey = "Tab";
    private const string TabCode = "Tab";
    private const string TabText = "\t";
    private const int TabVirtualKeyCode = 9;

    /// <summary>
    /// Expand <paramref name="text"/> into an ordered key event sequence.
    /// <c>\n</c> and <c>\r\n</c> become a single Enter press; <c>\t</c>
    /// becomes a Tab press; every other character becomes a keyDown carrying
    /// the character as <c>text</c> (which makes Chromium synthesize the
    /// keypress/input events) followed by a keyUp.
    /// </summary>
    public static IReadOnlyList<CdpKeyEvent> CreateSequence(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<CdpKeyEvent> events = new(text.Length * 2);
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                // Treat CRLF as one Enter press.
                index++;
                AppendSpecial(events, EnterKey, EnterCode, EnterText, EnterVirtualKeyCode);
            }
            else if (current is '\n' or '\r')
            {
                AppendSpecial(events, EnterKey, EnterCode, EnterText, EnterVirtualKeyCode);
            }
            else if (current == '\t')
            {
                AppendSpecial(events, TabKey, TabCode, TabText, TabVirtualKeyCode);
            }
            else
            {
                string character = current.ToString();
                events.Add(new CdpKeyEvent(KeyDown, character, null, character, null));
                events.Add(new CdpKeyEvent(KeyUp, character, null, null, null));
            }
        }
        return events;
    }

    private static void AppendSpecial(
        List<CdpKeyEvent> events,
        string key,
        string code,
        string text,
        int virtualKeyCode)
    {
        events.Add(new CdpKeyEvent(KeyDown, key, code, text, virtualKeyCode));
        events.Add(new CdpKeyEvent(KeyUp, key, code, null, virtualKeyCode));
    }
}
