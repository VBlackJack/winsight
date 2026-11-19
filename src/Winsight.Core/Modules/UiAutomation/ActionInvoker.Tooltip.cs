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
using System.Globalization;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Winsight.Core.Models;
using Winsight.Core.Modules.Clipboard;
using FlaUIControlType = FlaUI.Core.Definitions.ControlType;

namespace Winsight.Core.Modules.UiAutomation;

public sealed partial class ActionInvoker
{
    /// <summary>
    /// Move the mouse to the center of an element and capture the newly
    /// appearing tooltip, optionally asserting its text. The tooltip popup
    /// window is awaited for the whole timeout; the element's HelpText is only
    /// a fallback when no popup opens, and the snapshot says which one it is.
    /// </summary>
    public OperationResult Hover(
        int processId,
        ElementLocator locator,
        string? expectedTooltipText = null,
        string? windowTitle = null,
        int? timeoutMs = null)
    {
        if (CheckInputIntegrity(processId, out _) is { } blocked)
        {
            return blocked;
        }
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            int timeout = timeoutMs ?? DefaultTooltipTimeoutMs;
            if (timeout <= 0 || timeout > MaxTooltipTimeoutMs)
            {
                return OperationResult.Failure(
                    $"timeoutMs must be between 1 and {MaxTooltipTimeoutMs}.");
            }

            using UIA3Automation automation = new();
            ElementSearchResult search = ElementFinder.FindSingle(automation, processId, locator, windowTitle);
            if (!search.Found)
            {
                return OperationResult.FailureWith(search.Error!, search.Candidates);
            }

            AutomationElement element = search.Element!;
            ScrollIntoViewIfOffscreen(element);
            System.Drawing.Rectangle rect = element.BoundingRectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return OperationResult.Failure(
                    $"Located element has invalid bounds {rect.Width}x{rect.Height}.");
            }

            if (PrepareInputContext(out InputDispatchInfo dispatch) is { } contextBlocked)
            {
                return contextBlocked;
            }
            System.Drawing.Point center = new(
                rect.Left + rect.Width / 2,
                rect.Top + rect.Height / 2);
            System.Drawing.Point outsideElement = new(
                Math.Max(0, rect.Left - 24),
                Math.Max(0, rect.Top - 24));
            Mouse.MoveTo(outsideElement);
            Thread.Sleep(PreHoverSettleMs);

            HashSet<string> staleTooltips = CaptureTooltips(automation, processId)
                .Select(t => TooltipSignature(t.Snapshot))
                .ToHashSet(StringComparer.Ordinal);

            Mouse.MoveTo(center);

            string? helpText = SafeReadHelpText(element);
            Stopwatch stopwatch = Stopwatch.StartNew();
            TooltipSnapshot? lastTooltip = null;
            while (stopwatch.ElapsedMilliseconds <= timeout)
            {
                List<TooltipSnapshot> fresh = CaptureTooltips(automation, processId)
                    .Where(t => !staleTooltips.Contains(TooltipSignature(t.Snapshot)))
                    .Where(t => t.DistanceTo(center) <= TooltipProximityPx)
                    .OrderBy(t => t.DistanceTo(center))
                    .Select(t => t.Snapshot)
                    .ToList();
                TooltipSnapshot? tooltip = fresh.FirstOrDefault(t => TooltipTextMatches(t, expectedTooltipText));
                if (tooltip is not null)
                {
                    return OperationResult.OkWith(new HoverResult(center.X, center.Y, tooltip, dispatch));
                }

                lastTooltip = fresh.FirstOrDefault() ?? lastTooltip;
                Thread.Sleep(TooltipPollIntervalMs);
            }

            // No popup carried the text: fall back to the element's HelpText,
            // which assistive technology reads but which is not what a user sees.
            if (lastTooltip is null && TooltipTextMatches(helpText, expectedTooltipText))
            {
                TooltipSnapshot fromHelpText = new(
                    helpText!,
                    rect.X,
                    rect.Y,
                    rect.Width,
                    rect.Height,
                    TooltipSnapshot.SourceHelpText);
                return OperationResult.OkWith(new HoverResult(center.X, center.Y, fromHelpText, dispatch));
            }

            HoverResult result = new(center.X, center.Y, lastTooltip, dispatch);
            if (expectedTooltipText is not null)
            {
                return OperationResult.FailureWith(
                    $"Tooltip containing \"{expectedTooltipText}\" did not appear within {timeout} ms " +
                    $"near hover point ({center.X}, {center.Y}).",
                    result);
            }

            return OperationResult.OkWith(result);
        }
    }


    private sealed record TooltipCandidate(TooltipSnapshot Snapshot)
    {
        internal int DistanceTo(System.Drawing.Point point)
        {
            int dx = point.X < Snapshot.X
                ? Snapshot.X - point.X
                : point.X > Snapshot.X + Snapshot.Width
                    ? point.X - (Snapshot.X + Snapshot.Width)
                    : 0;
            int dy = point.Y < Snapshot.Y
                ? Snapshot.Y - point.Y
                : point.Y > Snapshot.Y + Snapshot.Height
                    ? point.Y - (Snapshot.Y + Snapshot.Height)
                    : 0;
            return (int)Math.Round(Math.Sqrt(dx * dx + dy * dy));
        }
    }

    // Tooltips of the target live in its own transient top-level windows (a
    // WPF Popup hosting a ToolTip, or a tooltips_class32 window); searching
    // only those keeps each poll cheap and ignores other applications.
    private static List<TooltipCandidate> CaptureTooltips(UIA3Automation automation, int processId)
    {
        List<TooltipCandidate> candidates = new();
        foreach ((IntPtr handle, _) in NativeWindowEnumerator.VisibleTopLevelWindows(processId))
        {
            if (!NativeWindowEnumerator.IsTransient(handle))
            {
                continue;
            }
            try
            {
                AutomationElement root = automation.FromHandle(handle);
                IEnumerable<AutomationElement> tooltips = root.Properties.ControlType.ValueOrDefault == FlaUIControlType.ToolTip
                    ? new[] { root }
                    : root.FindAllDescendants(cf => cf.ByControlType(FlaUIControlType.ToolTip));
                candidates.AddRange(tooltips
                    .Select(t => TryBuildTooltipCandidate(t, handle))
                    .Where(t => t is not null)
                    .Cast<TooltipCandidate>());
            }
            catch (COMException)
            {
                // The popup closed between enumeration and attachment.
            }
        }
        return candidates;
    }

    private static TooltipCandidate? TryBuildTooltipCandidate(AutomationElement tooltip, IntPtr windowHandle)
    {
        try
        {
            System.Drawing.Rectangle rect = tooltip.BoundingRectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return null;
            }

            string text = ReadTooltipText(tooltip);
            return new TooltipCandidate(new TooltipSnapshot(
                text,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                TooltipSnapshot.SourcePopup,
                windowHandle.ToInt64()));
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string ReadTooltipText(AutomationElement tooltip)
    {
        List<string> texts = new();
        AddDistinctText(texts, SafeReadName(tooltip));
        AddDistinctText(texts, SafeReadValue(tooltip));

        Queue<(AutomationElement Element, int Depth)> queue = new();
        foreach (AutomationElement child in SafeChildren(tooltip))
        {
            queue.Enqueue((child, 1));
        }
        while (queue.Count > 0)
        {
            (AutomationElement current, int depth) = queue.Dequeue();
            AddDistinctText(texts, SafeReadName(current));
            AddDistinctText(texts, SafeReadValue(current));
            if (depth >= TooltipTextDepth)
            {
                continue;
            }
            foreach (AutomationElement child in SafeChildren(current))
            {
                queue.Enqueue((child, depth + 1));
            }
        }

        return string.Join(" ", texts);
    }

    private static bool TooltipTextMatches(TooltipSnapshot tooltip, string? expectedTooltipText)
    {
        return expectedTooltipText is null ||
            tooltip.Text.Contains(expectedTooltipText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TooltipTextMatches(string? tooltipText, string? expectedTooltipText)
    {
        return !string.IsNullOrEmpty(tooltipText) &&
            (expectedTooltipText is null ||
                tooltipText.Contains(expectedTooltipText, StringComparison.OrdinalIgnoreCase));
    }

    private static string TooltipSignature(TooltipSnapshot tooltip)
    {
        return string.Join(
            "|",
            tooltip.Text,
            tooltip.X.ToString(CultureInfo.InvariantCulture),
            tooltip.Y.ToString(CultureInfo.InvariantCulture),
            tooltip.Width.ToString(CultureInfo.InvariantCulture),
            tooltip.Height.ToString(CultureInfo.InvariantCulture));
    }

    private static string? SafeReadName(AutomationElement element)
    {
        try
        {
            return element.Properties.Name.ValueOrDefault;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string? SafeReadValue(AutomationElement element)
    {
        try
        {
            return element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string? SafeReadHelpText(AutomationElement element)
    {
        try
        {
            return element.HelpText;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static AutomationElement[] SafeChildren(AutomationElement element)
    {
        try
        {
            return element.FindAllChildren();
        }
        catch (COMException)
        {
            return Array.Empty<AutomationElement>();
        }
    }

    private static void AddDistinctText(List<string> texts, string? text)
    {
        if (!string.IsNullOrEmpty(text) &&
            !texts.Contains(text, StringComparer.Ordinal))
        {
            texts.Add(text);
        }
    }

}
