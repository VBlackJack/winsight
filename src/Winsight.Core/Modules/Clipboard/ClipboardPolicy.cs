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

namespace Winsight.Core.Modules.Clipboard;

/// <summary>
/// The clipboard often holds passwords and tokens, so every tool that reads or
/// replaces it stays off until <see cref="AllowVariable"/> is set to a truthy
/// value (1, true, yes, on).
/// </summary>
public static class ClipboardPolicy
{
    public const string AllowVariable = "WINSIGHT_ALLOW_CLIPBOARD";
    public const string DisabledCode = "clipboard_disabled";

    public static bool IsAllowed => IsTruthy(Environment.GetEnvironmentVariable(AllowVariable));

    /// <summary>The refusal when the clipboard is off, else null.</summary>
    public static string? Refusal(string action) => Refusal(action, IsAllowed);

    internal static string? Refusal(string action, bool allowed) =>
        allowed
            ? null
            : $"{DisabledCode}: {action} needs the clipboard, which may hold secrets. Set {AllowVariable}=1 " +
              "in the server environment to allow it.";

    internal static bool IsTruthy(string? raw) =>
        raw?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
}
