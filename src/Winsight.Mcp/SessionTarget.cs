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

namespace Winsight.Mcp;

/// <summary>
/// Session-scoped default target process. An agent driving one application can
/// <c>set_target</c> once instead of repeating <c>processId</c> on every call;
/// each process-scoped tool falls back to this default when its own
/// <c>processId</c> is omitted. Thread-safe because MCP requests may overlap.
/// </summary>
public sealed class SessionTarget
{
    private readonly object _gate = new();
    private int? _processId;

    /// <summary>The current default process id, or null when none is set.</summary>
    public int? ProcessId
    {
        get
        {
            lock (_gate)
            {
                return _processId;
            }
        }
    }

    /// <summary>Set the session default target process.</summary>
    public void Set(int processId)
    {
        lock (_gate)
        {
            _processId = processId;
        }
    }

    /// <summary>Clear the session default target.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _processId = null;
        }
    }

    /// <summary>
    /// Resolve the effective process id: the explicit one if provided, otherwise
    /// the session default. Returns false with a clear error when neither exists.
    /// </summary>
    public bool TryResolve(int? explicitProcessId, out int processId, out string error)
    {
        if (explicitProcessId is int explicitId)
        {
            processId = explicitId;
            error = string.Empty;
            return true;
        }

        lock (_gate)
        {
            if (_processId is int defaultId)
            {
                processId = defaultId;
                error = string.Empty;
                return true;
            }
        }

        processId = 0;
        error = "No processId given and no session target set. " +
            "Pass processId, or call set_target first to set a default.";
        return false;
    }
}
