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

using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Reads the observable state of a located element so tests can assert
/// against the UI without attaching a debugger or reading managed memory.
/// </summary>
public sealed class ElementStateReader
{
    /// <summary>
    /// Locate a single element (strict) and return its
    /// <see cref="ElementState"/> snapshot wrapped in an OperationResult.
    /// </summary>
    public OperationResult Read(int processId, ElementLocator locator, string? windowTitle = null)
    {
        using UIA3Automation automation = new();
        ElementSearchResult search = ElementFinder.FindSingle(automation, processId, locator, windowTitle);
        if (!search.Found)
        {
            return OperationResult.FailureWith(search.Error!, search.Candidates);
        }
        return OperationResult.OkWith(BuildState(search.Match!));
    }

    /// <summary>
    /// Build a state snapshot from a live match. Pattern-backed fields are
    /// null when the element does not support the pattern or the provider
    /// fails to serve it.
    /// </summary>
    internal static ElementState BuildState(ElementMatch match)
    {
        AutomationElement element = match.Element;
        ElementCandidate candidate = match.Candidate;
        System.Drawing.Rectangle rect = element.BoundingRectangle;

        return new ElementState(
            AutomationId: candidate.AutomationId,
            Name: element.Properties.Name.ValueOrDefault ?? string.Empty,
            ControlType: candidate.ControlType,
            ClassName: candidate.ClassName,
            WindowTitle: candidate.WindowTitle,
            IsEnabled: element.Properties.IsEnabled.ValueOrDefault,
            IsVisible: !element.Properties.IsOffscreen.ValueOrDefault,
            HasKeyboardFocus: element.Properties.HasKeyboardFocus.ValueOrDefault,
            Value: SafeRead(() => element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault),
            ToggleState: SafeRead(() => element.Patterns.Toggle.PatternOrDefault?.ToggleState.ValueOrDefault.ToString()),
            IsSelected: SafeRead(() => element.Patterns.SelectionItem.PatternOrDefault?.IsSelected.ValueOrDefault),
            SelectedItem: SafeRead(ReadSelectedItemName),
            X: rect.X,
            Y: rect.Y,
            Width: rect.Width,
            Height: rect.Height);

        string? ReadSelectedItemName()
        {
            AutomationElement[]? selection =
                element.Patterns.Selection.PatternOrDefault?.Selection.ValueOrDefault;
            return selection is { Length: > 0 }
                ? selection[0].Properties.Name.ValueOrDefault
                : null;
        }
    }

    private static T? SafeRead<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (COMException)
        {
            return default;
        }
    }
}
