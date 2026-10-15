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
using System.Runtime.InteropServices;
using FlaUI.Core.WindowsAPI;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>One synthetic key transition, as a physical keyboard would report it.</summary>
internal readonly record struct KeyTransition(
    VirtualKeyShort VirtualKey,
    ushort ScanCode,
    bool IsExtended,
    bool IsKeyUp);

/// <summary>
/// Sends keyboard chords through SendInput the way a physical keyboard does:
/// every transition carries its hardware scan code, and keys that live on the
/// extended block (PageUp/PageDown, Home/End, Insert/Delete, arrows, right
/// Ctrl/Alt, Windows keys) carry KEYEVENTF_EXTENDEDKEY.
/// </summary>
/// <remarks>
/// A virtual key sent with no scan code and no extended flag is read by the
/// system as its numeric-keypad twin (PageUp becomes keypad 9). With NumLock on
/// and Shift held, Windows then injects a fake Shift release around the keypad
/// key, so a low-level hook, GetAsyncKeyState, WM_KEYDOWN and WPF all observe
/// Ctrl+PageUp instead of Ctrl+Shift+PageUp. Measured with the WpfLab probe.
/// </remarks>
internal static class KeyChordDispatcher
{
    /// <summary>
    /// Pause after each transition, so a WH_KEYBOARD_LL hook that reads
    /// GetAsyncKeyState sees every modifier settled before the next key.
    /// </summary>
    internal const int TransitionDelayMs = 15;

    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MapVkToVscEx = 4;
    private const ushort ScanCodeMask = 0x00FF;
    private const ushort ExtendedPrefixE0 = 0xE000;
    private const ushort ExtendedPrefixE1 = 0xE100;
    private const ushort PrefixMask = 0xFF00;

    private static readonly HashSet<VirtualKeyShort> ExtendedKeys = new()
    {
        VirtualKeyShort.PRIOR,
        VirtualKeyShort.NEXT,
        VirtualKeyShort.END,
        VirtualKeyShort.HOME,
        VirtualKeyShort.LEFT,
        VirtualKeyShort.UP,
        VirtualKeyShort.RIGHT,
        VirtualKeyShort.DOWN,
        VirtualKeyShort.INSERT,
        VirtualKeyShort.DELETE,
        VirtualKeyShort.RCONTROL,
        VirtualKeyShort.RMENU,
        VirtualKeyShort.LWIN,
        VirtualKeyShort.RWIN,
        VirtualKeyShort.APPS,
        VirtualKeyShort.DIVIDE,
        VirtualKeyShort.NUMLOCK,
        VirtualKeyShort.SNAPSHOT,
        VirtualKeyShort.CANCEL,
    };

    /// <summary>True for keys a physical keyboard reports with the extended flag.</summary>
    internal static bool IsExtendedKey(VirtualKeyShort key) => ExtendedKeys.Contains(key);

    /// <summary>
    /// Order a chord as a person types it: modifiers down in order, then the
    /// other keys down, then everything released in reverse order.
    /// </summary>
    internal static IReadOnlyList<KeyTransition> BuildTransitions(
        IReadOnlyList<VirtualKeyShort> keys,
        Func<VirtualKeyShort, ushort> scanCodeOf)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(scanCodeOf);

        List<VirtualKeyShort> ordered = keys.Where(KeyboardChordParser.IsModifier)
            .Concat(keys.Where(k => !KeyboardChordParser.IsModifier(k)))
            .ToList();
        List<KeyTransition> transitions = new(ordered.Count * 2);
        foreach (VirtualKeyShort key in ordered)
        {
            transitions.Add(Describe(key, scanCodeOf, isKeyUp: false));
        }
        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            transitions.Add(Describe(ordered[i], scanCodeOf, isKeyUp: true));
        }
        return transitions;
    }

    /// <summary>
    /// Press and release the chord. Keys already pressed are always released,
    /// even when a later SendInput call fails, so no modifier stays stuck.
    /// </summary>
    internal static void Dispatch(IReadOnlyList<VirtualKeyShort> keys)
    {
        IReadOnlyList<KeyTransition> transitions = BuildTransitions(keys, MapScanCode);
        Stack<KeyTransition> pressed = new();
        try
        {
            foreach (KeyTransition transition in transitions)
            {
                Send(transition);
                if (transition.IsKeyUp)
                {
                    pressed.Pop();
                }
                else
                {
                    pressed.Push(transition);
                }
                Thread.Sleep(TransitionDelayMs);
            }
        }
        finally
        {
            while (pressed.Count > 0)
            {
                KeyTransition down = pressed.Pop();
                TrySend(down with { IsKeyUp = true });
            }
        }
    }

    private static KeyTransition Describe(
        VirtualKeyShort key,
        Func<VirtualKeyShort, ushort> scanCodeOf,
        bool isKeyUp)
    {
        ushort mapped = scanCodeOf(key);
        ushort prefix = (ushort)(mapped & PrefixMask);
        bool extended = IsExtendedKey(key) || prefix is ExtendedPrefixE0 or ExtendedPrefixE1;
        return new KeyTransition(key, (ushort)(mapped & ScanCodeMask), extended, isKeyUp);
    }

    private static ushort MapScanCode(VirtualKeyShort key) =>
        (ushort)MapVirtualKeyW((uint)key, MapVkToVscEx);

    private static void Send(KeyTransition transition)
    {
        if (!TrySend(transition))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"SendInput rejected the {(transition.IsKeyUp ? "release" : "press")} of {transition.VirtualKey}. " +
                "Input may be blocked by a higher-integrity foreground window or a secure desktop.");
        }
    }

    private static bool TrySend(KeyTransition transition)
    {
        uint flags = 0;
        if (transition.IsExtended)
        {
            flags |= KeyEventExtendedKey;
        }
        if (transition.IsKeyUp)
        {
            flags |= KeyEventKeyUp;
        }
        NativeInput[] inputs =
        {
            new()
            {
                Type = InputKeyboard,
                Data = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput
                    {
                        VirtualKey = (ushort)transition.VirtualKey,
                        ScanCode = transition.ScanCode,
                        Flags = flags,
                    },
                },
            },
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>()) == inputs.Length;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, NativeInput[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Data;
    }

    // The union must be as large as MOUSEINPUT, the largest member, or
    // SendInput rejects the cbSize.
    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        public NativeMouseInput Mouse;

        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
