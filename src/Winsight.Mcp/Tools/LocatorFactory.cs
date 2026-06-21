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

namespace Winsight.Mcp.Tools;

/// <summary>
/// Builds an <see cref="ElementLocator"/> from the flat locator parameters every
/// UIA tool exposes. The parameters must stay flat on each tool method for the
/// MCP schema, but the assembly into a locator is single-sourced here so it
/// cannot drift between tools as the locator shape evolves.
/// </summary>
internal static class LocatorFactory
{
    public static ElementLocator From(
        string? automationId,
        string? name,
        string? className,
        string? controlType,
        string? nameContains,
        int? index,
        string? descendantText) =>
        new(automationId, name, className, controlType, nameContains, index, descendantText);
}
