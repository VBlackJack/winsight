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

using System.Diagnostics;
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Core.Modules.VisualRegression;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// Two-polarity calibration for the visual-regression differ against the live sample
/// app: a same-vs-same noise floor (must PASS with default thresholds, proving
/// specificity) and a real injected change: an Increment click that updates the count
/// label (must FAIL, proving sensitivity, especially the local SSIM criterion). The
/// reported table is how thresholds are calibrated above the environment noise floor.
/// </summary>
[Trait("Category", "E2E")]
public class VisualRegressionCalibrationTests
{
    private const string ReadyLabelId = "CountLabel";
    private const string IncrementButtonId = "IncrementButton";

    private readonly ITestOutputHelper _output;

    public VisualRegressionCalibrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Noise_floor_passes_and_an_injected_change_fails_with_default_thresholds()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        ElementWaiter waiter = new();
        WindowCapture capture = new();
        ActionInvoker invoker = new();
        VisualDiffer differ = new();

        string dir = Path.Combine(Path.GetTempPath(), $"winsight-visual-cal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string baseline = Path.Combine(dir, "baseline.png");
        string recapture = Path.Combine(dir, "recapture.png");
        string changed = Path.Combine(dir, "changed.png");

        try
        {
            OperationResult appeared = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: ReadyLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);
            Thread.Sleep(500); // let first-frame rendering settle

            capture.CaptureClientArea(process.Id, baseline);
            capture.CaptureClientArea(process.Id, recapture);

            VisualDiffOptions options = VisualDiffOptions.FromEnvironment();
            VisualDiffResult noise = differ.Compare(baseline, recapture, options);

            OperationResult click = invoker.Click(process.Id, new ElementLocator(AutomationId: IncrementButtonId));
            click.Success.Should().BeTrue(click.Error);
            Thread.Sleep(400); // let the count label update
            capture.CaptureClientArea(process.Id, changed);
            VisualDiffResult change = differ.Compare(baseline, changed, options);

            _output.WriteLine($"thresholds: ssim>={VisualDiffOptions.DefaultSsimThreshold} " +
                $"localSsim>={VisualDiffOptions.DefaultLocalSsimThreshold} " +
                $"pixel<={VisualDiffOptions.DefaultPixelDiffThreshold} color={VisualDiffOptions.DefaultColorThreshold}");
            _output.WriteLine($"{"case",-16} {"ssim",-10} {"localMin",-10} {"pixelRatio",-12} verdict");
            _output.WriteLine($"{"noise(same)",-16} {noise.Ssim,-10:F6} {noise.LocalSsimMin,-10:F6} {noise.PixelDiffRatio,-12:F6} {noise.Verdict}");
            _output.WriteLine($"{"change(+1)",-16} {change.Ssim,-10:F6} {change.LocalSsimMin,-10:F6} {change.PixelDiffRatio,-12:F6} {change.Verdict}");

            noise.Passed.Should().BeTrue(
                "two identical captures must pass (specificity), else thresholds are below the noise floor");
            change.Passed.Should().BeFalse(
                "a real Increment-click change must be detected (sensitivity)");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
        }
    }
}
