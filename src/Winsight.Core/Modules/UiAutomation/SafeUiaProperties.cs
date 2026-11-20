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

using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Patterns;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Safe accessors for optional UI Automation properties and patterns.
/// WinUI/XAML, Chromium, and custom providers frequently omit properties such
/// as <c>IsOffscreen</c>, <c>BoundingRectangle</c>, or <c>ExpandCollapseState</c>
/// and throw <see cref="PropertyNotSupportedException"/> or <see cref="COMException"/>
/// on direct reads. Every walker reads through these helpers with a sensible default.
/// </summary>
internal static class SafeUiaProperties
{
    public static T Read<T>(Func<T> getter, T fallback = default!)
    {
        try
        {
            return getter();
        }
        catch (Exception ex) when (IsIgnorableUiaException(ex))
        {
            return fallback;
        }
    }

    public static T ReadProperty<T>(IAutomationProperty<T> property, T fallback = default!)
    {
        try
        {
            return property.TryGetValue(out T? value) && value is not null ? value : fallback;
        }
        catch (Exception ex) when (IsIgnorableUiaException(ex))
        {
            return fallback;
        }
    }

    public static bool IsIgnorableUiaException(Exception ex) =>
        ex is PropertyNotSupportedException
            or PatternNotSupportedException
            or FlaUI.Core.Exceptions.NotSupportedException
            or System.NotSupportedException
            or COMException
            or InvalidOperationException;

    public static ControlType GetControlType(AutomationElement element) =>
        Read(() => ReadProperty(element.Properties.ControlType, ControlType.Unknown), ControlType.Unknown);

    public static string GetName(AutomationElement element) =>
        Read(() => ReadProperty(element.Properties.Name, string.Empty), string.Empty);

    public static string GetAutomationId(AutomationElement element) =>
        Read(() => ReadProperty(element.Properties.AutomationId, string.Empty), string.Empty);

    public static string GetClassName(AutomationElement element) =>
        Read(() => ReadProperty(element.Properties.ClassName, string.Empty), string.Empty);

    public static IntPtr GetNativeWindowHandle(AutomationElement element) =>
        Read(() => ReadProperty(element.Properties.NativeWindowHandle, IntPtr.Zero), IntPtr.Zero);

    public static bool GetIsEnabled(AutomationElement element, bool fallback = false) =>
        Read(() => ReadProperty(element.Properties.IsEnabled, fallback), fallback);

    public static bool GetIsOffscreen(AutomationElement element, bool fallback = false) =>
        Read(() => ReadProperty(element.Properties.IsOffscreen, fallback), fallback);

    public static bool GetHasKeyboardFocus(AutomationElement element) =>
        Read(() => ReadProperty(element.Properties.HasKeyboardFocus, false), false);

    public static bool GetIsPassword(AutomationElement element) =>
        Read(() => ReadProperty(element.Properties.IsPassword, false), false);

    public static bool GetIsKeyboardFocusable(AutomationElement element) =>
        Read(() => ReadProperty(element.Properties.IsKeyboardFocusable, false), false);

    public static Rectangle GetBoundingRectangle(AutomationElement element) =>
        Read(() => ReadProperty(element.Properties.BoundingRectangle, Rectangle.Empty), Rectangle.Empty);

    public static int[] GetRuntimeId(AutomationElement element) =>
        Read(() => ReadProperty(element.Properties.RuntimeId, Array.Empty<int>()), Array.Empty<int>());

    public static string? GetValue(AutomationElement element) =>
        Read<string?>(() =>
        {
            IValuePattern? pattern = element.Patterns.Value.PatternOrDefault;
            if (pattern is null) return null;
            return pattern.Value.TryGetValue(out string? val) ? val : null;
        }, null);

    public static bool GetIsValueReadOnly(AutomationElement element) =>
        Read(() =>
        {
            IValuePattern? pattern = element.Patterns.Value.PatternOrDefault;
            return pattern is null || ReadProperty(pattern.IsReadOnly, true);
        }, true);

    public static bool? GetIsSelected(AutomationElement element) =>
        Read<bool?>(() =>
        {
            ISelectionItemPattern? pattern = element.Patterns.SelectionItem.PatternOrDefault;
            if (pattern is null) return null;
            return pattern.IsSelected.TryGetValue(out bool selected) ? selected : null;
        }, null);

    public static string? GetToggleState(AutomationElement element) =>
        Read<string?>(() =>
        {
            ITogglePattern? pattern = element.Patterns.Toggle.PatternOrDefault;
            if (pattern is null) return null;
            return pattern.ToggleState.TryGetValue(out ToggleState state) ? state.ToString() : null;
        }, null);

    public static string? GetExpandCollapseState(AutomationElement element) =>
        Read<string?>(() =>
        {
            IExpandCollapsePattern? pattern = element.Patterns.ExpandCollapse.PatternOrDefault;
            if (pattern is null) return null;
            return pattern.ExpandCollapseState.TryGetValue(out ExpandCollapseState state) ? state.ToString() : null;
        }, null);

    public static AutomationElement[] GetChildren(AutomationElement element) =>
        Read(() => element.FindAllChildren(), Array.Empty<AutomationElement>());
}
