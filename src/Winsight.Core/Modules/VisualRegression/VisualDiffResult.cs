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

namespace Winsight.Core.Modules.VisualRegression;

public sealed record VisualDiffResult(
    bool Passed,
    double Ssim,
    double LocalSsimMin,
    double PixelDiffRatio,
    string? DiffMapPath,
    string Reasoning,
    string Verdict = VisualDiffVerdicts.Pass,
    string? BaselinePath = null,
    string? CandidatePath = null);

public static class VisualDiffVerdicts
{
    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string BaselineCreated = "baseline_created";
    public const string BaselineUpdated = "baseline_updated";
}
