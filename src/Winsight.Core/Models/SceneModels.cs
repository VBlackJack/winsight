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

using System.Text.Json.Serialization;

namespace Winsight.Core.Models;

/// <summary>A short-lived, observation-scoped target with explicit provenance.</summary>
public sealed record SceneElement(
    string Ref, string Source, string Role, string Name, string? Value,
    bool Enabled, bool Visible, bool Focused, bool? Selected,
    WindowBounds Bounds, IReadOnlyList<string> Actions,
    WindowIdentity Window,
    ElementLocator Locator,
    [property: JsonIgnore] string Identity,
    [property: JsonIgnore] string? BackendId = null,
    double? Confidence = null,
    string? ToggleState = null,
    [property: JsonIgnore] string? StateHash = null);

/// <summary>Expectation is evaluated against the same authorized process as the action.</summary>
public sealed record SceneExpectation(
    string Kind, string? Expected = null, ElementLocator? Locator = null,
    string? WindowTitle = null, string? TypeName = null, string? FieldName = null,
    string? CssSelector = null);

/// <summary>No implicit retry is permitted after dispatch.</summary>
public sealed record SceneAction(
    string Observation, string Target, string Action, SceneExpectation Expect,
    string? Text = null, string? ItemName = null, string? Destination = null,
    string[]? Targets = null, int TimeoutMs = 3000, bool DryRun = false,
    bool Recover = true, bool ObserveAfter = false);

/// <summary>Dispatched is distinct from Verified, including on exceptions or cancellation.</summary>
public sealed record VerifiedActionResult(
    string Status, bool Dispatched, bool Verified, string Message,
    string? Observation = null, object? Evidence = null, object? Recovery = null,
    object? NextObservation = null, object? Diff = null);

public sealed record SceneCapture(
    IReadOnlyList<WindowDescriptor> Windows, IReadOnlyList<SceneElement> Elements,
    bool Truncated, string Source, string? DocumentIdentity = null);
