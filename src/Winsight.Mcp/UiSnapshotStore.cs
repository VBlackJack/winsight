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

namespace Winsight.Mcp;

/// <summary>
/// Session-scoped store of the last UI snapshot per process, the baseline
/// <c>diff_ui</c> compares against. Keyed by process id so concurrent targets
/// keep independent baselines. Thread-safe because MCP requests may overlap.
/// </summary>
public sealed class UiSnapshotStore
{
    private readonly object _gate = new();
    private readonly Dictionary<int, UiSnapshot> _byProcess = new();

    public void Set(int processId, UiSnapshot snapshot)
    {
        lock (_gate)
        {
            _byProcess[processId] = snapshot;
        }
    }

    public bool TryGet(int processId, out UiSnapshot snapshot)
    {
        lock (_gate)
        {
            return _byProcess.TryGetValue(processId, out snapshot!);
        }
    }
}
