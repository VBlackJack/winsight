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

using System.ComponentModel;
using System.Globalization;
using FlaUI.Core.WindowsAPI;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>One keystroke of typed text: a UTF-16 unit, or a key such as Enter or Tab.</summary>
internal readonly record struct TextStroke(char Unit, VirtualKeyShort? Key)
{
    public static TextStroke Character(char unit) => new(unit, null);

    public static TextStroke Press(VirtualKeyShort key) => new(default, key);
}

/// <summary>
/// Types text into whatever window owns the keyboard, without a UI
/// Automation element: characters go as KEYEVENTF_UNICODE units (WM_CHAR in
/// the target), line breaks as Enter and tabs as Tab, which text fields
/// handle as keys rather than characters. Before every stroke the foreground
/// must still be the window the text was meant for; if a person clicks
/// elsewhere mid-way, typing stops instead of landing in their window.
/// </summary>
internal static class TextTyper
{
    /// <summary>Longest text one call types.</summary>
    public const int MaxLength = 4096;

    /// <summary>Pause between strokes, so an event loop that drains one key per frame keeps up.</summary>
    public const int StrokeDelayMs = 4;

    internal const string ForegroundChangedCode = "foreground_changed";

    public static string? Validate(string? text) =>
        string.IsNullOrEmpty(text)
            ? "text is required and must not be empty."
            : text.Length > MaxLength
                ? string.Create(CultureInfo.InvariantCulture, $"text is limited to {MaxLength} characters (got {text.Length}).")
                : null;

    /// <summary>Strokes for <paramref name="text"/>; CRLF, CR and LF each become one Enter.</summary>
    public static IReadOnlyList<TextStroke> Plan(string text)
    {
        List<TextStroke> strokes = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char unit = text[i];
            switch (unit)
            {
                case '\r':
                    strokes.Add(TextStroke.Press(VirtualKeyShort.RETURN));
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    break;
                case '\n':
                    strokes.Add(TextStroke.Press(VirtualKeyShort.RETURN));
                    break;
                case '\t':
                    strokes.Add(TextStroke.Press(VirtualKeyShort.TAB));
                    break;
                default:
                    strokes.Add(TextStroke.Character(unit));
                    break;
            }
        }
        return strokes;
    }

    /// <summary>
    /// Send the strokes while <paramref name="stillTargeted"/> holds. Returns
    /// null on success, or why typing stopped and how many strokes were sent.
    /// </summary>
    public static string? Send(IReadOnlyList<TextStroke> strokes, Func<bool> stillTargeted, Action<TextStroke> send)
    {
        for (int i = 0; i < strokes.Count; i++)
        {
            if (!stillTargeted())
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{ForegroundChangedCode}: the target window lost the foreground after {i} of {strokes.Count} " +
                    $"keystrokes; typing stopped so the rest did not land in another window.");
            }
            send(strokes[i]);
            Thread.Sleep(StrokeDelayMs);
        }
        return null;
    }

    public static void SendStroke(TextStroke stroke)
    {
        if (stroke.Key is VirtualKeyShort key)
        {
            KeyChordDispatcher.Dispatch([key]);
            return;
        }
        KeySendResult result = KeyChordDispatcher.SendUnicodeUnit(stroke.Unit);
        if (!result.Succeeded)
        {
            throw new Win32Exception(
                result.LastWin32Error,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"SendInput rejected a character (SendInput={result.SentCount}/{result.ExpectedCount}, win32Error={result.LastWin32Error}). Input may be blocked by a higher-integrity foreground window or a secure desktop."));
        }
    }
}
