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

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>Parses the wire value of <c>type_keys</c> mode: <c>keys</c> (default) or <c>paste</c>.</summary>
public static class TypeKeysMode
{
    public static bool TryParse(string? raw, out bool paste, out string? error)
    {
        string mode = string.IsNullOrWhiteSpace(raw) ? ActionInvoker.TypeKeysModeKeys : raw.Trim();
        paste = string.Equals(mode, ActionInvoker.TypeKeysModePaste, StringComparison.OrdinalIgnoreCase);
        bool keys = string.Equals(mode, ActionInvoker.TypeKeysModeKeys, StringComparison.OrdinalIgnoreCase);
        error = paste || keys
            ? null
            : $"Unknown mode \"{raw}\". Accepted values: {ActionInvoker.TypeKeysModeKeys}, {ActionInvoker.TypeKeysModePaste}.";
        return error is null;
    }
}
