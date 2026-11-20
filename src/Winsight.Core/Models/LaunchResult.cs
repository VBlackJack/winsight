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
/// Result of launching an application: the new process id and the window the
/// launcher waited for (the main window, or the one matching the requested
/// title), already classified so the agent can drive it immediately.
/// </summary>
public sealed record LaunchResult(
    int ProcessId,
    WindowDescriptor Window,
    IReadOnlyList<string>? MatchingProfiles = null,
    int? HandedOffToProcessId = null);
