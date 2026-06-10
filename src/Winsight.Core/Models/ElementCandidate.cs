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
/// Compact description of an element returned when a locator is ambiguous
/// (several matches) or fails (near misses). Gives an agent enough signal
/// to refine its locator in a single round trip.
/// </summary>
public sealed record ElementCandidate(
    int Index,
    string AutomationId,
    string Name,
    string ControlType,
    string ClassName,
    string WindowTitle);
