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

/// <summary>
/// One actionable element surfaced by <c>describe_ui</c>: a compact affordance
/// the agent can act on, with a ready-to-use <see cref="RecommendedLocator"/>
/// (the smallest criterion that targets it unambiguously) plus enough state to
/// reason about it. The point is to answer "what can I do here?" without parsing
/// a full UIA tree.
/// </summary>
public sealed record UiInteractable(
    string ControlType,
    string? AutomationId,
    string Name,
    string? Value,
    string? ToggleState,
    bool? IsSelected,
    bool IsEnabled,
    bool IsVisible,
    int X,
    int Y,
    int Width,
    int Height,
    string WindowTitle,
    string RecommendedLocator,
    string AffordanceId = "");
