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
/// A debuggable target exposed by a Chrome DevTools Protocol endpoint
/// (one entry of the <c>/json/list</c> response). For WebView2 hosts the
/// interesting targets are usually of type <c>page</c>.
/// </summary>
public sealed record CdpTarget(
    string Id,
    string Title,
    string Url,
    string Type,
    string? WebSocketDebuggerUrl);
