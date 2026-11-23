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

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Seam over live scene capture and action dispatch so tools can run against a fake driver in tests.
/// </summary>
public interface ISceneDriver
{
    SceneCapture Capture(
        int processId,
        string? title,
        int maximum,
        TimeSpan? timeBudget = null,
        TimeProvider? timeProvider = null);

    OperationResult Act(SceneElement node, SceneAction action, IReadOnlyList<SceneElement> additional, Action beforeDispatch);
}
