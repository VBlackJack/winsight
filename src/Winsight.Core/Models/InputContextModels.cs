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

/// <summary>One process in the parent chain of Winsight.</summary>
public sealed record ProcessChainEntry(int ProcessId, string Name);

/// <summary>
/// Read-only execution-context diagnostics for synthetic desktop input:
/// session, window station, thread vs input desktop, job object UI
/// restrictions, token restriction/AppContainer flags, and parent process chain.
/// </summary>
public sealed record InputContextSnapshot(
    uint? SessionId,
    uint ActiveConsoleSessionId,
    bool SessionMatchesConsole,
    string? WindowStation,
    bool? WindowStationVisible,
    string? ThreadDesktop,
    string? InputDesktop,
    int? InputDesktopWin32Error,
    bool DesktopMatchesInput,
    bool InJob,
    uint JobUiRestrictionsMask,
    IReadOnlyList<string> JobUiRestrictions,
    int? JobQueryWin32Error,
    bool? IsRestrictedToken,
    bool? IsAppContainer,
    string IntegrityLevel,
    IReadOnlyList<ProcessChainEntry> ParentChain,
    uint ThreadId = 0);
