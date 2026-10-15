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

using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;

namespace Winsight.Eval;

/// <summary>A grader-owned exact UI predicate, never supplied by the decision source.</summary>
internal sealed record EvaluationProbe(ElementLocator Locator, string Property, string Expected, string? WindowTitle = null);

internal sealed record OracleReading(bool? Match, string? Actual, string? Error);

/// <summary>Detects changes to protected state at observation boundaries, retaining uncertainty and transient violations.</summary>
internal sealed class ProtectedStateMonitor(int probeCount)
{
    private bool _changed;
    private bool _unknown;
    private int _samples;

    internal bool? WrongTarget => _changed ? true : probeCount == 0 || _unknown || _samples == 0 ? null : false;
    internal int Samples => _samples;

    internal void Observe(IReadOnlyList<bool?> matches)
    {
        if (matches.Count != probeCount) throw new ArgumentException("Protected probe count changed.", nameof(matches));
        _samples++;
        foreach (bool? match in matches)
        {
            _changed |= match == false;
            _unknown |= match is null;
        }
    }
}

internal static class EvaluationOracles
{
    internal static bool? Read(int processId, EvaluationProbe probe)
        => ReadDetailed(processId, probe).Match;

    internal static OracleReading ReadDetailed(int processId, EvaluationProbe probe)
    {
        try
        {
            OperationResult read = new ElementStateReader().Read(processId, probe.Locator, probe.WindowTitle);
            if (!read.Success || read.Data is not ElementState state) return new(null, null, read.Error ?? "No element state returned.");
            string? value = probe.Property switch
            {
                "name" => state.Name,
                "value" => state.Value,
                "toggle" => state.ToggleState,
                "selected_item" => state.SelectedItem,
                "selected" => state.IsSelected?.ToString().ToLowerInvariant(),
                "focused" => state.HasKeyboardFocus.ToString().ToLowerInvariant(),
                "visible" => state.IsVisible.ToString().ToLowerInvariant(),
                _ => throw new InvalidDataException("Unsupported oracle property: " + probe.Property)
            };
            return new(value is null ? null : string.Equals(value, probe.Expected, StringComparison.Ordinal), value,
                value is null ? "UIA property unavailable." : null);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Console.Error.WriteLine("Oracle unavailable: " + ex.Message);
            return new(null, null, ex.Message);
        }
    }

    internal static void Validate(EvaluationProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        if (probe.Locator is null ||
            string.IsNullOrEmpty(probe.Locator.AutomationId) && string.IsNullOrEmpty(probe.Locator.Name) ||
            probe.Locator.Index is not null || probe.Locator.NameContains is not null || probe.Locator.DescendantText is not null)
            throw new InvalidDataException("Oracle locators require an exact AutomationId or Name without indexed or partial matching.");
        if (probe.Property is not ("name" or "value" or "toggle" or "selected_item" or "selected" or "focused" or "visible") || probe.Expected is null)
            throw new InvalidDataException("Oracle property and expected value are required.");
    }
}
