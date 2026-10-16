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

namespace Winsight.Core.Modules.UserActivity;

/// <summary>
/// What every Winsight process in the session knows about recent input: the
/// last injection window (start and end ticks, and whether one is in progress)
/// and the most recent input already attributed to a human. Ticks are the
/// 32-bit millisecond counter that <c>GetLastInputInfo</c> reports.
/// </summary>
internal readonly record struct LedgerState(
    bool HasInjection,
    bool InjectionActive,
    uint InjectionStartTick,
    uint InjectionEndTick,
    bool HasHuman,
    uint HumanTick);

/// <summary>Whether the last input came from Winsight, and the tick of the last human input when known.</summary>
internal readonly record struct InputAttribution(bool LastInputIsWinsight, uint? HumanTick);

/// <summary>
/// Decides whether the last input Windows saw was injected by Winsight. Without
/// a low-level hook (which endpoint products flag) the only evidence is time:
/// input that falls inside a Winsight injection window, widened by a small
/// margin, is Winsight's; anything else is a human's. A human input made during
/// an injection is masked by it, and input injected by another tool counts as
/// human. All arithmetic tolerates the 49.7-day wrap of the tick counter.
/// </summary>
internal static class InputAttributor
{
    /// <summary>Slack around an injection window for input the system timestamps late.</summary>
    public const uint InjectionMarginMs = 50;

    /// <summary>An injection marked active for longer than this is a crashed holder, not live input.</summary>
    public const uint StaleInjectionMs = 60_000;

    /// <summary>Milliseconds elapsed from <paramref name="tick"/> to <paramref name="now"/>, across a wrap.</summary>
    public static uint AgeMs(uint now, uint tick) => unchecked(now - tick);

    public static InputAttribution Attribute(uint now, uint lastInputTick, LedgerState ledger)
    {
        bool winsight = false;
        if (ledger.HasInjection)
        {
            long inputAge = AgeMs(now, lastInputTick);
            long startAge = AgeMs(now, ledger.InjectionStartTick);
            bool atOrAfterStart = inputAge <= startAge + InjectionMarginMs;
            bool active = ledger.InjectionActive && startAge <= StaleInjectionMs;
            if (active)
            {
                winsight = atOrAfterStart;
            }
            else
            {
                long endAge = AgeMs(now, ledger.InjectionEndTick);
                winsight = atOrAfterStart && inputAge + InjectionMarginMs >= endAge;
            }
        }

        if (!winsight)
        {
            return new InputAttribution(false, lastInputTick);
        }
        return new InputAttribution(true, ledger.HasHuman ? ledger.HumanTick : null);
    }
}
