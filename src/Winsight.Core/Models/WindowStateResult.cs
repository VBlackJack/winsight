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
/// A top-level window after a window operation: its show state
/// ("normal", "maximized" or "minimized") and its bounds as the system
/// reports them. <see cref="MatchesRequest"/> is false when the application
/// constrained a move or resize (minimum or maximum size, snapping), so the
/// caller sees the bounds it actually got.
/// </summary>
public sealed record WindowStateResult(
    long WindowHandle,
    string Title,
    string Action,
    string State,
    int X,
    int Y,
    int Width,
    int Height,
    bool MatchesRequest);
