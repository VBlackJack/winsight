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
/// Criteria used to locate a UI Automation element. All provided criteria
/// must match (logical AND). <see cref="Index"/> alone is not a valid
/// locator: it only disambiguates when several elements match the other
/// criteria.
/// </summary>
/// <param name="AutomationId">Exact AutomationId match.</param>
/// <param name="Name">Exact Name property match.</param>
/// <param name="ClassName">Exact ClassName match.</param>
/// <param name="ControlType">Exact UIA control type match (e.g. "Button", "Edit"), case-insensitive.</param>
/// <param name="NameContains">Case-insensitive substring match on the Name property.</param>
/// <param name="Index">Zero-based index used to pick one element when several match the other criteria.</param>
public sealed record ElementLocator(
    string? AutomationId = null,
    string? Name = null,
    string? ClassName = null,
    string? ControlType = null,
    string? NameContains = null,
    int? Index = null)
{
    /// <summary>
    /// True when no discriminating criterion is set. An index alone does not
    /// make a locator usable.
    /// </summary>
    public bool IsEmpty =>
        string.IsNullOrEmpty(AutomationId) &&
        string.IsNullOrEmpty(Name) &&
        string.IsNullOrEmpty(ClassName) &&
        string.IsNullOrEmpty(ControlType) &&
        string.IsNullOrEmpty(NameContains);
}
