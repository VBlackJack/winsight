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
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Diagnostics;
using Winsight.Core.Modules.WebView2;
using Xunit;

namespace Winsight.E2E;

[Trait("Category", "E2E")]
public class DiagnosticsScenarioTests
{
    private const int WindowReadyDelayMs = 500;
    private const int CounterObservationMs = 2500;
    private const int EventCaptureMs = 1500;
    private const string RuntimeProvider = "System.Runtime";
    private static readonly string[] TplProviders = ["System.Threading.Tasks.TplEventSource"];

    [Fact]
    public void Agent_can_capture_screenshot_observe_counters_and_query_webview2()
    {
        var executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built first (looked at: {executablePath})");

        using var process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        try
        {
            WaitForMainWindow(process);
            Thread.Sleep(WindowReadyDelayMs);

            // capture_screenshot
            var capture = new WindowCapture();
            var screenshotPath = Path.Combine(
                Path.GetTempPath(),
                "winsight-e2e",
                $"screenshot-{Guid.NewGuid():N}.png");
            var screenshot = capture.Capture(process.Id, screenshotPath);
            try
            {
                File.Exists(screenshot.Path).Should().BeTrue();
                new FileInfo(screenshot.Path).Length.Should().BeGreaterThan(0);
                screenshot.Width.Should().BeGreaterThan(0);
                screenshot.Height.Should().BeGreaterThan(0);
                screenshot.CapturedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
            }
            finally
            {
                File.Delete(screenshot.Path);
            }

            // observe_counters
            var observer = new DiagnosticsObserver();
            var snapshot = observer.ObserveCounters(process.Id, RuntimeProvider, CounterObservationMs);
            snapshot.ProcessId.Should().Be(process.Id);
            snapshot.ProviderName.Should().Be(RuntimeProvider);
            snapshot.SamplesObserved.Should().BeGreaterThan(0,
                "the runtime emits EventCounters once per second when subscribed");
            snapshot.Counters.Should().NotBeEmpty();
            snapshot.Counters.Should().ContainKey("cpu-usage");

            // capture_events (low-rate slice — TplEventSource always emits something during a 1.5s window)
            var capture2 = observer.CaptureEvents(
                process.Id,
                providerNames: TplProviders,
                durationMs: EventCaptureMs,
                maxEvents: 200);
            capture2.ProcessId.Should().Be(process.Id);
            capture2.DurationMs.Should().Be(EventCaptureMs);
            capture2.Events.Count.Should().Be(capture2.TotalCaptured);
            capture2.Truncated.Should().BeFalse(
                "200 events is plenty of headroom for a 1.5s TPL slice on a quiet WPF app");

            // detect_webview2_endpoints — sample app has none, exercises the empty path
            var detector = new WebView2Detector();
            var endpoints = detector.Detect(process.Id);
            endpoints.Should().BeEmpty(
                "SampleWpfApp does not embed WebView2; the detector must return an empty list, not throw");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static void WaitForMainWindow(Process process)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return;
            }
            Thread.Sleep(100);
        }
        throw new TimeoutException("Sample app did not show its main window within 10 seconds.");
    }
}
