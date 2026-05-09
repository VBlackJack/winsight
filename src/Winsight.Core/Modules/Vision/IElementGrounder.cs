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

namespace Winsight.Core.Modules.Vision;

/// <summary>
/// Locates a UI element from a screenshot given a natural-language query.
/// Implementations may use a cloud vision API (Anthropic, OpenAI),
/// a local ONNX model (ShowUI-π, OmniParser V2), or a hybrid.
/// </summary>
public interface IElementGrounder
{
    Task<GroundedElement> FindAsync(
        string screenshotPath,
        string query,
        CancellationToken cancellationToken = default);
}
