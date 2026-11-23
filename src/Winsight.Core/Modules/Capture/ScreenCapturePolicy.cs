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

namespace Winsight.Core.Modules.Capture;

/// <summary>
/// Snapshot of a top-level window's visibility and bounds used to decide
/// whether a desktop-composited ("screen") capture of a target window is safe.
/// </summary>
public readonly record struct OcclusionWindow(
    long Handle,
    int ProcessId,
    string Title,
    WindowBounds Bounds,
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked,
    bool IsZeroAlpha);

/// <summary>
/// Outcome of checking whether a target window can be captured from the
/// composited desktop without capturing an unintended occluding window.
/// </summary>
public sealed record OcclusionCheckResult(
    bool Allowed,
    string? Reason,
    OcclusionWindow? Occluder = null);

/// <summary>
/// Pure policy deciding whether <see cref="CaptureSource.Screen"/> may capture
/// the desktop rectangle of a target window.
/// </summary>
public static class ScreenCapturePolicy
{
    public static OcclusionCheckResult Evaluate(
        OcclusionWindow target,
        IReadOnlyList<OcclusionWindow> windowsAboveTarget,
        bool allowOccluded)
    {
        ArgumentNullException.ThrowIfNull(windowsAboveTarget);

        if (!target.IsVisible || target.IsMinimized || target.IsCloaked)
        {
            string state = target.IsMinimized
                ? "minimized"
                : target.IsCloaked
                    ? "cloaked (not on the active desktop)"
                    : "not visible";
            return new OcclusionCheckResult(
                Allowed: false,
                Reason: $"Cannot capture with captureSource=\"screen\": target window 0x{target.Handle:x} " +
                        $"(process {target.ProcessId}) is {state}. Restore or show the window, or use captureSource=\"window\".");
        }

        if (target.Bounds.Width <= 0 || target.Bounds.Height <= 0)
        {
            return new OcclusionCheckResult(
                Allowed: false,
                Reason: $"Cannot capture with captureSource=\"screen\": target window 0x{target.Handle:x} " +
                        $"has invalid dimensions ({target.Bounds.Width}x{target.Bounds.Height}).");
        }

        if (allowOccluded)
        {
            return new OcclusionCheckResult(Allowed: true, Reason: null);
        }

        foreach (OcclusionWindow candidate in windowsAboveTarget)
        {
            if (!candidate.IsVisible || candidate.IsMinimized || candidate.IsCloaked || candidate.IsZeroAlpha)
            {
                continue;
            }

            // Same-process windows above the target (popups, menus, dropdowns, tooltips)
            // are the intended content of captureSource="screen", never occluders.
            if (candidate.Handle == target.Handle || candidate.ProcessId == target.ProcessId)
            {
                continue;
            }

            if (candidate.Bounds.Width <= 0 || candidate.Bounds.Height <= 0)
            {
                continue;
            }

            if (Intersects(target.Bounds, candidate.Bounds))
            {
                return new OcclusionCheckResult(
                    Allowed: false,
                    Reason: $"Cannot capture with captureSource=\"screen\": target window 0x{target.Handle:x} " +
                            $"(process {target.ProcessId}) is occluded by window 0x{candidate.Handle:x} " +
                            $"of process {candidate.ProcessId}. Bring the target window to the front, use " +
                            "captureSource=\"window\", or pass allowOccluded=true.",
                    Occluder: candidate);
            }
        }

        return new OcclusionCheckResult(Allowed: true, Reason: null);
    }

    public static bool Intersects(WindowBounds a, WindowBounds b)
    {
        if (a.Width <= 0 || a.Height <= 0 || b.Width <= 0 || b.Height <= 0)
        {
            return false;
        }

        long aRight = (long)a.Left + a.Width;
        long aBottom = (long)a.Top + a.Height;
        long bRight = (long)b.Left + b.Width;
        long bBottom = (long)b.Top + b.Height;

        return a.Left < bRight && aRight > b.Left && a.Top < bBottom && aBottom > b.Top;
    }
}
