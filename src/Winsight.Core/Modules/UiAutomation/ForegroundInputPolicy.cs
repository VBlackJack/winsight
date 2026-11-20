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
/// Configurable policy for foreground hosts known to reject synthetic Win32
/// input (such as VMware Workstation, where SetCursorPos returns FALSE with
/// GetLastError == 0 whenever VMware is the foreground window).
/// </summary>
public static class ForegroundInputPolicy
{
    public const string ForegroundRejectsSyntheticInputCode = "foreground_rejects_synthetic_input";
    public const string ForegroundMayRejectSyntheticInputCode = "foreground_may_reject_synthetic_input";
    public const string RejectingHostsEnvVar = "WINSIGHT_SYNTHETIC_INPUT_REJECTING_HOSTS";
    public const string DefaultRejectingHosts = "vmware,vmware-vmx,vmplayer";

    private static readonly char[] ListSeparators = { ',', ';' };

    private const string VmwareHint =
        "VMware Workstation rejects synthetic pointer moves while it is the foreground window, regardless of " +
        "how it became foreground or where the cursor is; no known VMware preference fixes it. " +
        "Recommended workarounds: bring another window to the foreground first when the target is not VMware, " +
        "use UI Automation patterns (click_element / select_item / act_verified with UIA actions) for VMware " +
        "host UI, and use vmsight for guest VM input.";

    private const string GenericForegroundHint =
        "The foreground process rejected synthetic pointer input (SetCursorPos returned FALSE). " +
        "Bring another window to the foreground first when the target is not that process, or use " +
        "UI Automation patterns (click_element / select_item / act_verified with UIA actions) instead of physical input.";

    private static readonly AsyncLocal<Func<ForegroundProcessInfo?>?> OverrideForegroundReaderSlot = new();
    private static readonly AsyncLocal<Func<string?>?> OverrideEnvReaderSlot = new();

    internal static Func<ForegroundProcessInfo?>? OverrideForegroundReader
    {
        get => OverrideForegroundReaderSlot.Value;
        set => OverrideForegroundReaderSlot.Value = value;
    }

    internal static Func<string?>? OverrideEnvReader
    {
        get => OverrideEnvReaderSlot.Value;
        set => OverrideEnvReaderSlot.Value = value;
    }

    /// <summary>Read the current foreground process (or test override).</summary>
    internal static ForegroundProcessInfo? ReadForeground() =>
        OverrideForegroundReader is { } reader ? reader() : PointerDiagnostics.ReadForegroundProcess();

    /// <summary>
    /// Resolve the configured rejecting host names from
    /// <see cref="RejectingHostsEnvVar"/>, falling back to
    /// <see cref="DefaultRejectingHosts"/> when the variable is unset.
    /// </summary>
    public static IReadOnlySet<string> ConfiguredRejectingHosts()
    {
        string? raw = OverrideEnvReader is { } envReader
            ? envReader()
            : Environment.GetEnvironmentVariable(RejectingHostsEnvVar);
        string effective = raw ?? DefaultRejectingHosts;
        HashSet<string> hosts = new(StringComparer.OrdinalIgnoreCase);
        foreach (string token in effective.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string normalized = NormalizeProcessName(token);
            if (!string.IsNullOrEmpty(normalized))
            {
                hosts.Add(normalized);
            }
        }
        return hosts;
    }

    /// <summary>True when <paramref name="processName"/> is listed in the configured rejecting hosts.</summary>
    public static bool IsKnownRejectingHost(string? processName)
    {
        string normalized = NormalizeProcessName(processName);
        return !string.IsNullOrEmpty(normalized) && ConfiguredRejectingHosts().Contains(normalized);
    }

    /// <summary>
    /// Return a non-blocking warning for keyboard tools (<c>send_key_chord</c>,
    /// <c>type_keys</c>, <c>type_text</c> fallback) when the foreground process
    /// is listed as a synthetic-input-rejecting host, or null otherwise.
    /// </summary>
    internal static string? BuildKeyboardWarning(string action)
    {
        ForegroundProcessInfo? foreground = ReadForeground();
        if (foreground is null || !IsKnownRejectingHost(foreground.ProcessName))
        {
            return null;
        }

        string hint = HintFor(foreground);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{ForegroundMayRejectSyntheticInputCode}: foreground process {foreground.ProcessName} (PID {foreground.ProcessId}) is listed as a host that may reject synthetic input during {action}. {hint}");
    }

    /// <summary>
    /// Build the hint string for a rejected pointer move or keyboard warning.
    /// </summary>
    internal static string HintFor(ForegroundProcessInfo? foreground)
    {
        string normalized = NormalizeProcessName(foreground?.ProcessName);
        return normalized is "vmware" or "vmware-vmx" or "vmplayer"
            ? VmwareHint
            : GenericForegroundHint;
    }

    private static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }
        string trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}
