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

namespace Winsight.Core.Modules.Cdp;

/// <summary>
/// Parameter set of one <c>Input.dispatchKeyEvent</c> CDP command.
/// <paramref name="EventType"/> is the CDP <c>type</c> field
/// (<c>keyDown</c> / <c>keyUp</c>); the rest map one-to-one to the
/// protocol fields of the same (camelCase) name.
/// </summary>
public sealed record CdpKeyEvent(
    string EventType,
    string Key,
    string? Code,
    string? Text,
    int? WindowsVirtualKeyCode);
