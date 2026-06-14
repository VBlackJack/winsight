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

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Parses human-readable keyboard chords such as "Ctrl+Shift+N" into
/// virtual keys suitable for FlaUI's SendInput-backed keyboard primitives.
/// </summary>
public static class KeyboardChordParser
{
    private static readonly Dictionary<string, VirtualKeyShort> Aliases =
        new Dictionary<string, VirtualKeyShort>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl"] = VirtualKeyShort.CONTROL,
            ["control"] = VirtualKeyShort.CONTROL,
            ["shift"] = VirtualKeyShort.SHIFT,
            ["alt"] = VirtualKeyShort.ALT,
            ["menu"] = VirtualKeyShort.ALT,
            ["win"] = VirtualKeyShort.LWIN,
            ["windows"] = VirtualKeyShort.LWIN,
            ["leftwin"] = VirtualKeyShort.LWIN,
            ["rightwin"] = VirtualKeyShort.RWIN,
            ["enter"] = VirtualKeyShort.RETURN,
            ["return"] = VirtualKeyShort.RETURN,
            ["esc"] = VirtualKeyShort.ESCAPE,
            ["escape"] = VirtualKeyShort.ESCAPE,
            ["backspace"] = VirtualKeyShort.BACK,
            ["back"] = VirtualKeyShort.BACK,
            ["delete"] = VirtualKeyShort.DELETE,
            ["del"] = VirtualKeyShort.DELETE,
            ["insert"] = VirtualKeyShort.INSERT,
            ["ins"] = VirtualKeyShort.INSERT,
            ["space"] = VirtualKeyShort.SPACE,
            ["tab"] = VirtualKeyShort.TAB,
            ["home"] = VirtualKeyShort.HOME,
            ["end"] = VirtualKeyShort.END,
            ["pageup"] = VirtualKeyShort.PRIOR,
            ["pgup"] = VirtualKeyShort.PRIOR,
            ["pagedown"] = VirtualKeyShort.NEXT,
            ["pgdn"] = VirtualKeyShort.NEXT,
            ["left"] = VirtualKeyShort.LEFT,
            ["right"] = VirtualKeyShort.RIGHT,
            ["up"] = VirtualKeyShort.UP,
            ["down"] = VirtualKeyShort.DOWN,
            ["plus"] = VirtualKeyShort.OEM_PLUS,
            ["minus"] = VirtualKeyShort.OEM_MINUS,
            ["comma"] = VirtualKeyShort.OEM_COMMA,
            ["period"] = VirtualKeyShort.OEM_PERIOD,
            ["slash"] = VirtualKeyShort.OEM_2,
            ["backslash"] = VirtualKeyShort.OEM_5,
            ["semicolon"] = VirtualKeyShort.OEM_1,
            ["quote"] = VirtualKeyShort.OEM_7,
            ["leftbracket"] = VirtualKeyShort.OEM_4,
            ["rightbracket"] = VirtualKeyShort.OEM_6,
        };

    /// <summary>
    /// Try to parse a keyboard chord. Modifiers are normalized before the
    /// non-modifier key so "N+Ctrl" and "Ctrl+N" dispatch the same sequence.
    /// </summary>
    public static bool TryParse(
        string? chord,
        out VirtualKeyShort[] keys,
        out string? error)
    {
        keys = Array.Empty<VirtualKeyShort>();
        error = null;

        if (string.IsNullOrWhiteSpace(chord))
        {
            error = "Keyboard chord is empty.";
            return false;
        }

        string[] tokens = chord.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            error = "Keyboard chord is empty.";
            return false;
        }

        List<VirtualKeyShort> modifiers = new();
        List<VirtualKeyShort> nonModifiers = new();
        HashSet<VirtualKeyShort> seen = new();
        foreach (string token in tokens)
        {
            if (!TryParseToken(token, out VirtualKeyShort key))
            {
                error = $"Unknown key token \"{token}\" in chord \"{chord}\".";
                return false;
            }
            if (!seen.Add(key))
            {
                error = $"Duplicate key token \"{token}\" in chord \"{chord}\".";
                return false;
            }
            if (IsModifier(key))
            {
                modifiers.Add(key);
            }
            else
            {
                nonModifiers.Add(key);
            }
        }

        if (nonModifiers.Count == 0)
        {
            error = $"Chord \"{chord}\" must include at least one non-modifier key.";
            return false;
        }

        keys = modifiers.Concat(nonModifiers).ToArray();
        return true;
    }

    private static bool TryParseToken(string token, out VirtualKeyShort key)
    {
        if (Aliases.TryGetValue(token, out key))
        {
            return true;
        }

        if (token.Length == 1)
        {
            char character = token[0];
            if (char.IsAsciiLetter(character))
            {
                return Enum.TryParse(
                    $"KEY_{char.ToUpperInvariant(character)}",
                    ignoreCase: false,
                    out key);
            }
            if (char.IsAsciiDigit(character))
            {
                return Enum.TryParse($"KEY_{character}", ignoreCase: false, out key);
            }
        }

        string normalized = token.Replace("_", string.Empty, StringComparison.Ordinal);
        if (normalized.Length is >= 2 and <= 3 &&
            (normalized[0] == 'F' || normalized[0] == 'f') &&
            int.TryParse(normalized[1..], out int functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            return Enum.TryParse($"F{functionKey}", ignoreCase: false, out key);
        }

        // Enum.TryParse accepts any raw numeric string (e.g. "9999") and casts it
        // to an undefined enum value. Guard with Enum.IsDefined so a numeric token
        // outside the enum is rejected as an unknown key rather than dispatched as
        // an arbitrary virtual key.
        return Enum.TryParse(token, ignoreCase: true, out key)
            && Enum.IsDefined(key);
    }

    internal static bool IsModifier(VirtualKeyShort key)
    {
        return key is VirtualKeyShort.CONTROL
            or VirtualKeyShort.LCONTROL
            or VirtualKeyShort.RCONTROL
            or VirtualKeyShort.SHIFT
            or VirtualKeyShort.LSHIFT
            or VirtualKeyShort.RSHIFT
            or VirtualKeyShort.ALT
            or VirtualKeyShort.LMENU
            or VirtualKeyShort.RMENU
            or VirtualKeyShort.LWIN
            or VirtualKeyShort.RWIN;
    }
}
