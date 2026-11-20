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

using Winsight.Core.Models;

namespace Winsight.Core.Modules.UserActivity;

internal readonly record struct SessionLockReading(bool? Locked, string Source);

/// <summary>
/// Platform clock, input, foreground and session queries used by
/// <see cref="UserActivityMonitor"/>. Replaced with a deterministic fake in
/// unit tests so wait loops and attribution never depend on wall-clock time or
/// the live desktop.
/// </summary>
internal interface IUserActivityBackend
{
    uint TickNow();

    long ElapsedMs();

    uint LastInputTick(uint now);

    LedgerState ReadLedger();

    void RecordHuman(uint tick);

    ForegroundWindowInfo? ReadForeground(IReadOnlyCollection<int>? revealTitleFor);

    SessionLockReading ReadSessionLock();

    CursorPoint? ReadCursorPosition();

    HookInputState ReadHookState();

    void Sleep(int milliseconds);

    Task DelayAsync(int milliseconds, CancellationToken cancellationToken);
}
