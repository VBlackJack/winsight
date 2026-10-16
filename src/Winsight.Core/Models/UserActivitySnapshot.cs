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
/// The window that owns the foreground. <see cref="Title"/> is withheld
/// (null) unless the window belongs to a process the caller targets: the
/// title of someone else's browser or mail does not belong in an agent's log.
/// </summary>
public sealed record ForegroundWindowInfo(
    long Handle,
    int ProcessId,
    string? ProcessName,
    string? Title,
    bool TitleWithheld);

/// <summary>
/// Who is using the shared desktop. <see cref="HumanIdleMs"/> is the time since
/// the last input not attributed to Winsight (null when none was ever seen);
/// <see cref="LastInputSource"/> says whether the most recent input of any kind
/// was <c>human</c> or <c>winsight</c>. <see cref="SessionLocked"/> is null when
/// Windows does not report it.
/// </summary>
public sealed record UserActivitySnapshot(
    long LastInputAgeMs,
    string LastInputSource,
    long? HumanIdleMs,
    ForegroundWindowInfo? Foreground,
    bool? SessionLocked,
    int? YieldToUserMs,
    bool UserActive);

/// <summary>Outcome of waiting for the human to stop using the desktop.</summary>
public sealed record UserIdleWaitResult(
    bool Idle,
    long WaitedMs,
    int IdleMs,
    UserActivitySnapshot Activity);
