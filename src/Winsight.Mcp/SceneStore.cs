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

/// <summary>Bounded, expiring observations. Reservation prevents concurrent or repeated dispatch.</summary>
public sealed class SceneStore(TimeProvider? clock = null)
{
    private const int Capacity = 16;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public sealed record Entry(string Id, int ProcessId, long ProcessStart, string? Title,
        DateTimeOffset Expires, SceneCapture Capture, int? CdpPort = null, string? CdpTarget = null);

    public Entry Add(int pid, long processStart, string? title, SceneCapture capture, int? cdpPort = null, string? cdpTarget = null)
    {
        lock (_gate)
        {
            foreach (string expired in _entries.Where(p => p.Value.Expires <= _clock.GetUtcNow()).Select(p => p.Key).ToArray()) _entries.Remove(expired);
            while (_entries.Count >= Capacity) _entries.Remove(_entries.MinBy(p => p.Value.Expires).Key);
            Entry entry = new(Guid.NewGuid().ToString("N"), pid, processStart, title, _clock.GetUtcNow().AddMinutes(2), capture, cdpPort, cdpTarget);
            _entries.Add(entry.Id, entry);
            return entry;
        }
    }

    public Entry? Get(string id)
    {
        lock (_gate) return _entries.TryGetValue(id, out Entry? entry) && entry.Expires > _clock.GetUtcNow() ? entry : null;
    }

    public bool Consume(string id)
    {
        lock (_gate)
        {
            bool valid = _entries.TryGetValue(id, out Entry? entry) && entry.Expires > _clock.GetUtcNow();
            _entries.Remove(id);
            return valid;
        }
    }
}
