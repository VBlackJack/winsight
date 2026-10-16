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
using System.Drawing;
using System.Globalization;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;

namespace Winsight.Core.Modules.VisualRegression;

/// <summary>
/// Bounds of a visual wait. <see cref="StableComparisons"/> is the number of
/// consecutive frame-to-frame comparisons that must show no change for the
/// window to count as settled. Null thresholds fall back to the
/// <c>WINSIGHT_VISUAL_*</c> settings of <c>compare_visual_baseline</c>.
/// </summary>
public sealed record VisualWaitOptions(
    int TimeoutMs = VisualWaitOptions.DefaultTimeoutMs,
    int PollMs = VisualWaitOptions.DefaultPollMs,
    int StableComparisons = VisualWaitOptions.DefaultStableComparisons,
    double? SsimThreshold = null,
    double? LocalSsimThreshold = null)
{
    public const int DefaultTimeoutMs = 5000;
    public const int MaxTimeoutMs = 60_000;
    public const int DefaultPollMs = 100;
    public const int MinPollMs = 20;
    public const int MaxPollMs = 5000;
    public const int DefaultStableComparisons = 3;
    public const int MaxStableComparisons = 50;

    public string? Validate() =>
        TimeoutMs is < 0 or > MaxTimeoutMs
            ? Bound(nameof(TimeoutMs), 0, MaxTimeoutMs, TimeoutMs)
            : PollMs is < MinPollMs or > MaxPollMs
                ? Bound(nameof(PollMs), MinPollMs, MaxPollMs, PollMs)
                : StableComparisons is < 1 or > MaxStableComparisons
                    ? Bound(nameof(StableComparisons), 1, MaxStableComparisons, StableComparisons)
                    : SsimThreshold is < 0 or > 1 || LocalSsimThreshold is < 0 or > 1
                        ? "Thresholds must be between 0 and 1."
                        : null;

    private static string Bound(string name, int min, int max, int value) =>
        string.Create(CultureInfo.InvariantCulture, $"{name} must be between {min} and {max} (got {value}).");
}

/// <summary>Outcome of a visual wait; the scores are those of the last comparison.</summary>
public sealed record VisualWaitResult(
    string Condition,
    bool Met,
    long ElapsedMs,
    int Frames,
    double Ssim,
    double LocalSsimMin,
    double PixelDiffRatio,
    WindowRegion Region);

/// <summary>Elapsed time and sleeping, replaced by a scripted clock in tests.</summary>
internal interface IWaitClock
{
    long ElapsedMs { get; }

    void Sleep(int milliseconds);
}

internal sealed class StopwatchClock : IWaitClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public long ElapsedMs => _stopwatch.ElapsedMilliseconds;

    public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);
}

/// <summary>
/// Waits on what a window shows instead of sleeping: until it differs from a
/// reference image, or until successive captures stop changing. Change is
/// judged by the same criteria as <c>compare_visual_baseline</c> (global SSIM,
/// worst-block local SSIM, pixel ratio), so a small status text change
/// counts. Captures stay in memory.
/// </summary>
public sealed class VisualWaiter
{
    public const string ConditionChange = "change";
    public const string ConditionStable = "stable";
    private const string TimedOutCode = "timed_out";

    private readonly WindowCapture _capture;
    private readonly VisualDiffer _differ;

    public VisualWaiter(WindowCapture capture, VisualDiffer differ)
    {
        _capture = capture;
        _differ = differ;
    }

    /// <summary>
    /// Wait until the window (or region) differs from <paramref name="reference"/>,
    /// or from a capture taken now when no reference is given.
    /// </summary>
    public OperationResult WaitForChange(
        int processId,
        string? windowTitle,
        WindowRegion? region,
        VisualWaitOptions options,
        Bitmap? reference = null)
    {
        if (options.Validate() is { } invalid)
        {
            return OperationResult.Failure(invalid);
        }
        VisualDiffOptions diff = DiffOptions(options);
        using CapturedFrame? initial = reference is null ? _capture.CaptureFrame(processId, windowTitle, region) : null;
        WindowRegion used = initial?.Region ?? region ?? new WindowRegion(0, 0, reference!.Width, reference.Height);
        return RunChange(
            reference ?? initial!.Bitmap,
            () => _capture.CaptureFrame(processId, windowTitle, region).Detach(),
            (a, b) => _differ.Compare(a, b, diff),
            options,
            used,
            new StopwatchClock());
    }

    /// <summary>Wait until successive captures of the window (or region) stop changing.</summary>
    public OperationResult WaitForStable(
        int processId,
        string? windowTitle,
        WindowRegion? region,
        VisualWaitOptions options)
    {
        if (options.Validate() is { } invalid)
        {
            return OperationResult.Failure(invalid);
        }
        VisualDiffOptions diff = DiffOptions(options);
        using CapturedFrame first = _capture.CaptureFrame(processId, windowTitle, region);
        return RunStable(
            first.Bitmap,
            () => _capture.CaptureFrame(processId, windowTitle, region).Detach(),
            (a, b) => _differ.Compare(a, b, diff),
            options,
            first.Region,
            new StopwatchClock());
    }

    internal static OperationResult RunChange(
        Bitmap reference,
        Func<Bitmap> grab,
        Func<Bitmap, Bitmap, VisualDiffResult> compare,
        VisualWaitOptions options,
        WindowRegion region,
        IWaitClock clock)
    {
        int frames = 0;
        while (true)
        {
            VisualDiffResult last;
            using (Bitmap current = grab())
            {
                frames++;
                last = compare(reference, current);
            }
            if (!last.Passed)
            {
                return OperationResult.OkWith(Result(ConditionChange, true, clock, frames, last, region));
            }
            if (clock.ElapsedMs >= options.TimeoutMs)
            {
                return OperationResult.FailureWith(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{TimedOutCode}: no visible change within {options.TimeoutMs} ms ({frames} captures; " +
                        $"last SSIM {last.Ssim:F4}, local SSIM {last.LocalSsimMin:F4})."),
                    Result(ConditionChange, false, clock, frames, last, region));
            }
            clock.Sleep(options.PollMs);
        }
    }

    internal static OperationResult RunStable(
        Bitmap first,
        Func<Bitmap> grab,
        Func<Bitmap, Bitmap, VisualDiffResult> compare,
        VisualWaitOptions options,
        WindowRegion region,
        IWaitClock clock)
    {
        Bitmap previous = (Bitmap)first.Clone();
        int frames = 1;
        int unchanged = 0;
        try
        {
            while (true)
            {
                clock.Sleep(options.PollMs);
                Bitmap current = grab();
                frames++;
                VisualDiffResult last = compare(previous, current);
                previous.Dispose();
                previous = current;
                unchanged = last.Passed ? unchanged + 1 : 0;
                if (unchanged >= options.StableComparisons)
                {
                    return OperationResult.OkWith(Result(ConditionStable, true, clock, frames, last, region));
                }
                if (clock.ElapsedMs >= options.TimeoutMs)
                {
                    return OperationResult.FailureWith(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{TimedOutCode}: the window was still changing after {options.TimeoutMs} ms " +
                            $"({frames} captures; needed {options.StableComparisons} unchanged comparisons in a row)."),
                        Result(ConditionStable, false, clock, frames, last, region));
                }
            }
        }
        finally
        {
            previous.Dispose();
        }
    }

    private static VisualDiffOptions DiffOptions(VisualWaitOptions options) =>
        VisualDiffOptions.FromEnvironment(
            ssimThreshold: options.SsimThreshold,
            localSsimThreshold: options.LocalSsimThreshold);

    private static VisualWaitResult Result(
        string condition, bool met, IWaitClock clock, int frames, VisualDiffResult last, WindowRegion region) =>
        new(condition, met, clock.ElapsedMs, frames, last.Ssim, last.LocalSsimMin, last.PixelDiffRatio, region);
}
