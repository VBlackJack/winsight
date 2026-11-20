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
/// Node of the inspected UIA tree. <see cref="Value"/> carries the current
/// Value-pattern content (text boxes, combo boxes) and is null when the
/// element does not support the pattern, so the tree doubles as a read-only
/// oracle for assertions.
/// </summary>
public sealed record UiElementNode(
    string AutomationId,
    string Name,
    string ControlType,
    string ClassName,
    bool IsEnabled,
    bool IsVisible,
    string? Value,
    int X,
    int Y,
    int Width,
    int Height,
    IReadOnlyList<UiElementNode> Children,
    bool Truncated = false,
    string? TruncationReason = null);
