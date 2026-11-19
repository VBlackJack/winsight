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
/// Result payload returned after a coordinate-based click. Reports the
/// absolute screen point that was clicked, so an agent can correlate the
/// action with a window-relative input.
/// </summary>
public sealed record ClickPointResult(
    int ScreenX,
    int ScreenY,
    InputDispatchInfo? Dispatch = null);
