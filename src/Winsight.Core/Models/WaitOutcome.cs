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
/// Result payload of a wait operation: whether the condition was met, how
/// long it took, how many elements matched on the last poll, and the final
/// state of the element when one was uniquely matched. For child_count,
/// <see cref="ChildCount"/> is the number of counted children on the last poll.
/// </summary>
public sealed record WaitOutcome(
    string Condition,
    bool ConditionMet,
    long ElapsedMs,
    int MatchCount,
    ElementState? State,
    int? ChildCount = null);
