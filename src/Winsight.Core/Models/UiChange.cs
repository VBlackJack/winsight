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

namespace Winsight.Core.Models;

/// <summary>One field of an element whose value changed between two snapshots.</summary>
public sealed record FieldChange(string Field, string? Before, string? After);

/// <summary>An element that persisted between two snapshots but whose state changed.</summary>
public sealed record ElementChange(
    string ControlType,
    string? AutomationId,
    string Name,
    string WindowTitle,
    string RecommendedLocator,
    IReadOnlyList<FieldChange> Changes);

/// <summary>
/// The delta between two <see cref="UiSnapshot"/>s: windows opened/closed,
/// actionable elements added/removed, and elements whose state changed.
/// <see cref="Verdict"/> is <c>no_change</c> when nothing differs, otherwise
/// <c>changed</c>.
/// </summary>
public sealed record UiChange(
    string Verdict,
    IReadOnlyList<WindowDescriptor> WindowsOpened,
    IReadOnlyList<WindowDescriptor> WindowsClosed,
    IReadOnlyList<UiInteractable> ElementsAdded,
    IReadOnlyList<UiInteractable> ElementsRemoved,
    IReadOnlyList<ElementChange> ElementsChanged);
