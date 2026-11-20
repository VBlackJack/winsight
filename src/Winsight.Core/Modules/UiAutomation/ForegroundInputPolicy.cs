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

using System.Globalization;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Detects foreground processes whose input drivers or grabs reject synthetic
/// Win32 input (SendInput / SetCursorPos), such as VMware Workstation's
/// enhanced virtual keyboard driver and input grab.
/// </summary>
public static class ForegroundInputPolicy
{
    public const string ForegroundRejectsSyntheticInputCode = "foreground_rejects_synthetic_input";

    private const string VmwareHint =
        "VMware Workstation rejects synthetic mouse and keyboard input while its window is in the foreground " +
        "(enhanced virtual keyboard driver or input grab). Check VMware Edit > Preferences > Input " +
        "(or disable Enhanced Virtual Keyboard), or use UI Automation patterns (click_element / select_item) " +
        "for VMware host UI and vmsight for guest VM input instead of physical input.";

    private const string GenericForegroundHint =
        "The foreground process is rejecting synthetic Win32 input (SetCursorPos returned FALSE). " +
        "Check whether the foreground application installs a low-level input driver or grab, or use " +
        "UI Automation patterns instead of physical input.";

    private static readonly Dictionary<string, string> KnownHostHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vmware"] = VmwareHint,
        ["vmware-vmx"] = VmwareHint,
        ["vmplayer"] = VmwareHint,
    };

    private static readonly AsyncLocal<Func<ForegroundProcessInfo?>?> OverrideForegroundReaderSlot = new();

    internal static Func<ForegroundProcessInfo?>? OverrideForegroundReader
    {
        get => OverrideForegroundReaderSlot.Value;
        set => OverrideForegroundReaderSlot.Value = value;
    }

    /// <summary>Read the current foreground process (or test override).</summary>
    internal static ForegroundProcessInfo? ReadForeground() =>
        OverrideForegroundReader is { } reader ? reader() : PointerDiagnostics.ReadForegroundProcess();

    /// <summary>Return the actionable hint if <paramref name="processName"/> is a known synthetic-input-rejecting host.</summary>
    public static string? TryGetKnownHostHint(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }
        string normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
        return KnownHostHints.TryGetValue(normalized, out string? hint) ? hint : null;
    }

    /// <summary>True when <paramref name="processName"/> is known to drop synthetic input when foreground.</summary>
    public static bool IsKnownRejectingHost(string? processName) => TryGetKnownHostHint(processName) is not null;

    /// <summary>
    /// Check the current foreground process before keyboard dispatch
    /// (send_key_chord / type_keys). Returns a failure result when the
    /// foreground host is known to silently drop synthetic input, else null.
    /// </summary>
    internal static OperationResult? CheckForegroundForKeyboardInput(string action)
    {
        ForegroundProcessInfo? foreground = ReadForeground();
        if (foreground is null || TryGetKnownHostHint(foreground.ProcessName) is not { } hint)
        {
            return null;
        }

        string message = string.Create(
            CultureInfo.InvariantCulture,
            $"{ForegroundRejectsSyntheticInputCode}: foreground process {foreground.ProcessName} (PID {foreground.ProcessId}) drops synthetic keyboard input for {action}. {hint}");

        return OperationResult.FailureWith(
            message,
            new
            {
                code = ForegroundRejectsSyntheticInputCode,
                action,
                foregroundWindow = foreground,
                hint,
            });
    }

    /// <summary>
    /// Build the hint string for a rejected pointer move when SetCursorPos
    /// returned FALSE or the foreground process is a known rejecting host.
    /// </summary>
    internal static string HintFor(ForegroundProcessInfo? foreground) =>
        TryGetKnownHostHint(foreground?.ProcessName) ?? GenericForegroundHint;
}
