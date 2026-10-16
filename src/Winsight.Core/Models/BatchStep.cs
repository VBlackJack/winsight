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
/// One step in a <c>run_steps</c> batch. A flat, discriminated shape: the
/// <see cref="Action"/> selects which underlying primitive runs, and the rest
/// of the fields carry that primitive's parameters (only the relevant ones are
/// read). Keeping it flat (rather than a polymorphic union) is what lets an
/// LLM fill a step reliably.
/// </summary>
public sealed record BatchStep(
    string Action,
    string? Label = null,
    int? ProcessId = null,
    // Shared locator (UIA actions).
    string? AutomationId = null,
    string? Name = null,
    string? ClassName = null,
    string? ControlType = null,
    string? NameContains = null,
    int? Index = null,
    string? DescendantText = null,
    string? WindowTitle = null,
    // click / click_point.
    string? ClickStyle = null,
    int? X = null,
    int? Y = null,
    string? CoordinateSpace = null,
    bool? AllowOutOfWindow = null,
    int? SettleMs = null,
    // scroll_point (wheel notches, positive up) and hover_point.
    int? Delta = null,
    bool? Horizontal = null,
    int? HoldMs = null,
    // type and click_point.
    string? Text = null,
    bool? FocusWindow = null,
    // type_keys (text, windowTitle, plus mode keys|paste and the paste restore delay).
    string? Mode = null,
    int? RestoreDelayMs = null,
    // key_chord.
    string? Chord = null,
    // select.
    string? ItemName = null,
    // hover.
    string? ExpectedTooltipText = null,
    // wait.
    string? Condition = null,
    string? ExpectedValue = null,
    int? TimeoutMs = null,
    int? PollIntervalMs = null,
    int? StableForMs = null,
    // assert (reuses ExpectedValue as the expected operand; text_visible reads it with OCR).
    string? Operator = null,
    // wait_visual_change / wait_visual_stable / assert text_visible: area in window coordinates.
    WindowRegion? Region = null,
    double? Threshold = null,
    double? LocalThreshold = null,
    int? StableComparisons = null,
    // screenshot.
    string? OutputPath = null);
