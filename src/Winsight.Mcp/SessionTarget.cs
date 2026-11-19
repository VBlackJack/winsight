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

using System.Text.RegularExpressions;
using Winsight.Mcp.Tools;

namespace Winsight.Mcp;

/// <summary>
/// Session-scoped default target process and optional named aliases for
/// multi-app workflows. An agent driving one or more applications can bind
/// aliases via <c>set_target(processId, alias)</c> and switch between them with
/// <c>switch_target(alias)</c>. Thread-safe because MCP requests may overlap.
/// </summary>
public sealed partial class SessionTarget
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _aliases = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>Snapshot of named process aliases bound in this session.</summary>
    public IReadOnlyDictionary<string, int> Aliases
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, int>(_aliases, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>Set the session default target process.</summary>
    public void Set(int processId, string? alias = null)
    {
        if (!TrySet(processId, out string error, alias))
        {
            throw new InvalidOperationException(error);
        }
    }

    /// <summary>Set the session default target process after policy validation, optionally binding an alias.</summary>
    public bool TrySet(int processId, out string error, string? alias = null)
    {
        if (alias is not null && !AliasPattern().IsMatch(alias))
        {
            error = "alias must be 1..32 ASCII letters, digits, underscores or hyphens.";
            return false;
        }

        lock (_gate)
        {
            if (!ToolAccessPolicy.TryAuthorizeProcess(processId, out error))
            {
                return false;
            }
            _processId = processId;
            if (!string.IsNullOrEmpty(alias))
            {
                _aliases[alias] = processId;
            }
            error = string.Empty;
            return true;
        }
    }

    /// <summary>Switch the active default target to a previously bound named alias.</summary>
    public bool TrySwitch(string alias, out int processId, out string error)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            processId = 0;
            error = "alias is required.";
            return false;
        }

        lock (_gate)
        {
            if (!_aliases.TryGetValue(alias, out int boundPid))
            {
                processId = 0;
                string known = _aliases.Count == 0 ? "none" : string.Join(", ", _aliases.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
                error = $"Unknown target alias '{alias}'. Bound aliases: {known}.";
                return false;
            }

            if (!ToolAccessPolicy.TryAuthorizeProcess(boundPid, out error))
            {
                processId = 0;
                return false;
            }

            _processId = boundPid;
            processId = boundPid;
            error = string.Empty;
            return true;
        }
    }

    /// <summary>Clear a specific alias, or clear the session default target and all aliases when alias is omitted.</summary>
    public void Clear(string? alias = null)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                _aliases.Remove(alias);
                return;
            }

            _processId = null;
            _aliases.Clear();
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
            if (!ToolAccessPolicy.TryAuthorizeProcess(explicitId, out error))
            {
                processId = 0;
                return false;
            }
            processId = explicitId;
            error = string.Empty;
            return true;
        }

        lock (_gate)
        {
            if (_processId is int defaultId)
            {
                if (!ToolAccessPolicy.TryAuthorizeProcess(defaultId, out error))
                {
                    processId = 0;
                    return false;
                }
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

    [GeneratedRegex("\\A[a-zA-Z0-9_-]{1,32}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex AliasPattern();
}
