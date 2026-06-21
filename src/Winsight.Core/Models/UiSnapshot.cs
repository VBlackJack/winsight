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
/// A structural snapshot of a process's UI at a point in time: its top-level
/// windows and its actionable elements. Diffing two snapshots yields what
/// changed after an action — the agent's feedback sense.
/// </summary>
public sealed record UiSnapshot(
    IReadOnlyList<WindowDescriptor> Windows,
    IReadOnlyList<UiInteractable> Interactables);
