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

using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// A located element together with the compact candidate descriptor used
/// for ambiguity and near-miss reporting.
/// </summary>
internal sealed record ElementMatch(
    AutomationElement Element,
    ElementCandidate Candidate,
    IntPtr WindowHandle);

/// <summary>
/// UIA root element for a visible top-level Win32 window.
/// </summary>
internal sealed record WindowRoot(
    AutomationElement Element,
    IntPtr Handle,
    string Title);

/// <summary>
/// Outcome of a strict single-element lookup. Exactly one of
/// <see cref="Element"/> or <see cref="Error"/> is set; when an error is
/// caused by ambiguity or produces near-miss suggestions, the corresponding
/// candidates are attached so an agent can refine its locator in one step.
/// </summary>
internal sealed record ElementSearchResult(
    ElementMatch? Match,
    string? Error,
    IReadOnlyList<ElementCandidate>? Candidates)
{
    public bool Found => Match is not null;

    public AutomationElement? Element => Match?.Element;

    public static ElementSearchResult Success(ElementMatch match) =>
        new(match, null, null);

    public static ElementSearchResult Failure(string error, IReadOnlyList<ElementCandidate>? candidates = null) =>
        new(null, error, candidates);
}

/// <summary>
/// Locates elements across all top-level windows of a process. Lookups are
/// strict: when several elements match a locator without an explicit index,
/// the lookup fails and reports the candidates rather than silently picking
/// the first match.
/// </summary>
internal static class ElementFinder
{
    /// <summary>
    /// Top-level windows of the process, optionally narrowed to those whose
    /// title contains <paramref name="windowTitle"/> (case-insensitive).
    /// Enumeration goes through Win32 and roots each element at its handle,
    /// because UIA's desktop child listing omits owned windows (modal
    /// dialogs) and the owner's child listing omits them too.
    /// </summary>
    internal static IReadOnlyList<WindowRoot> ResolveWindows(
        UIA3Automation automation,
        int processId,
        string? windowTitle)
    {
        List<WindowRoot> windows = new();
        foreach ((IntPtr handle, string title) in NativeWindowEnumerator.VisibleTopLevelWindows(processId))
        {
            if (!string.IsNullOrEmpty(windowTitle) &&
                !title.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                windows.Add(new WindowRoot(automation.FromHandle(handle), handle, title));
            }
            catch (COMException)
            {
                // The window closed between enumeration and attachment.
            }
        }
        return windows;
    }

    /// <summary>
    /// Breadth-first search over the given windows, returning every element
    /// matching the locator (index criterion excluded).
    /// </summary>
    internal static List<ElementMatch> CollectMatches(
        IReadOnlyList<WindowRoot> windows,
        ElementLocator locator)
    {
        List<ElementMatch> matches = new();
        foreach (WindowRoot window in windows)
        {
            string title = window.Title;
            Queue<AutomationElement> queue = new();
            queue.Enqueue(window.Element);
            while (queue.Count > 0)
            {
                AutomationElement current = queue.Dequeue();
                (string automationId, string name, string className, string controlType)? snapshot =
                    TryReadSnapshot(current);
                if (snapshot is null)
                {
                    // Element vanished mid-walk (closing popup); skip its branch.
                    continue;
                }
                (string automationId, string name, string className, string controlType) = snapshot.Value;
                List<string>? descendantTexts = !string.IsNullOrEmpty(locator.DescendantText)
                    ? CollectDescendantTexts(current)
                    : null;
                if (LocatorMatcher.Matches(
                    locator,
                    automationId,
                    name,
                    className,
                    controlType,
                    descendantTexts))
                {
                    matches.Add(new ElementMatch(
                        current,
                        new ElementCandidate(matches.Count, automationId, name, controlType, className, title),
                        window.Handle));
                }
                foreach (AutomationElement child in SafeChildren(current))
                {
                    queue.Enqueue(child);
                }
            }
        }
        return matches;
    }

    /// <summary>
    /// Strict single-element lookup. Fails with candidate listings on
    /// ambiguity, and with near-miss suggestions when nothing matches.
    /// </summary>
    internal static ElementSearchResult FindSingle(
        UIA3Automation automation,
        int processId,
        ElementLocator locator,
        string? windowTitle = null)
    {
        if (locator.IsEmpty)
        {
            return ElementSearchResult.Failure(
                "Locator is empty: provide at least one of automationId, name, nameContains, descendantText, className, or controlType.");
        }

        IReadOnlyList<WindowRoot> windows = ResolveWindows(automation, processId, windowTitle);
        if (windows.Count == 0)
        {
            return ElementSearchResult.Failure(BuildNoWindowError(processId, windowTitle));
        }

        List<ElementMatch> matches = CollectMatches(windows, locator);

        if (matches.Count == 0)
        {
            List<ElementCandidate> suggestions = CollectNearMisses(windows, locator);
            string suffix = suggestions.Count > 0
                ? $" Near matches: {DescribeCandidates(suggestions)}"
                : string.Empty;
            return ElementSearchResult.Failure(
                $"Element not found for locator {locator} in process {processId}.{suffix}",
                suggestions.Count > 0 ? suggestions : null);
        }

        if (locator.Index is int index)
        {
            if (index < 0 || index >= matches.Count)
            {
                return ElementSearchResult.Failure(
                    $"Locator index {index} is out of range: {matches.Count} element(s) matched. " +
                    $"Candidates: {DescribeCandidates(Cap(matches))}",
                    Cap(matches));
            }
            return ElementSearchResult.Success(matches[index]);
        }

        if (matches.Count > 1)
        {
            return ElementSearchResult.Failure(
                $"Locator {locator} matched {matches.Count} elements in process {processId}. " +
                $"Add 'index' (0-based) or narrow the locator. Candidates: {DescribeCandidates(Cap(matches))}",
                Cap(matches));
        }

        return ElementSearchResult.Success(matches[0]);
    }

    private static string BuildNoWindowError(int processId, string? windowTitle)
    {
        List<(IntPtr Handle, string Title)> allWindows =
            NativeWindowEnumerator.VisibleTopLevelWindows(processId);
        if (string.IsNullOrEmpty(windowTitle) || allWindows.Count == 0)
        {
            return $"No top-level window found for process {processId}.";
        }

        string titles = string.Join(", ", allWindows.Select(w => $"\"{w.Title}\""));
        return $"No window of process {processId} has a title containing \"{windowTitle}\". " +
               $"Available titles: {titles}.";
    }

    private static List<ElementCandidate> CollectNearMisses(
        IReadOnlyList<WindowRoot> windows,
        ElementLocator locator)
    {
        List<ElementCandidate> nearMisses = new();
        foreach (WindowRoot window in windows)
        {
            string title = window.Title;
            Queue<AutomationElement> queue = new();
            queue.Enqueue(window.Element);
            while (queue.Count > 0 && nearMisses.Count < UiaDefaults.MaxReportedCandidates)
            {
                AutomationElement current = queue.Dequeue();
                (string automationId, string name, string className, string controlType)? snapshot =
                    TryReadSnapshot(current);
                if (snapshot is null)
                {
                    continue;
                }
                (string automationId, string name, string className, string controlType) = snapshot.Value;
                if (LocatorMatcher.IsNearMiss(locator, automationId, name))
                {
                    nearMisses.Add(new ElementCandidate(
                        nearMisses.Count, automationId, name, controlType, className, title));
                }
                foreach (AutomationElement child in SafeChildren(current))
                {
                    queue.Enqueue(child);
                }
            }
        }
        return nearMisses;
    }

    private static (string AutomationId, string Name, string ClassName, string ControlType)? TryReadSnapshot(
        AutomationElement element)
    {
        try
        {
            return (
                element.Properties.AutomationId.ValueOrDefault ?? string.Empty,
                element.Properties.Name.ValueOrDefault ?? string.Empty,
                element.Properties.ClassName.ValueOrDefault ?? string.Empty,
                element.Properties.ControlType.ValueOrDefault.ToString());
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static List<string> CollectDescendantTexts(AutomationElement element)
    {
        List<string> texts = new();
        Queue<(AutomationElement Element, int Depth)> queue = new();
        foreach (AutomationElement child in SafeChildren(element))
        {
            queue.Enqueue((child, 1));
        }

        while (queue.Count > 0)
        {
            (AutomationElement current, int depth) = queue.Dequeue();
            AddIfNotEmpty(texts, SafeReadName(current));
            AddIfNotEmpty(texts, SafeReadValue(current));

            if (depth >= UiaDefaults.MaxDescendantTextDepth)
            {
                continue;
            }
            foreach (AutomationElement child in SafeChildren(current))
            {
                queue.Enqueue((child, depth + 1));
            }
        }

        return texts;
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

    private static void AddIfNotEmpty(List<string> texts, string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            texts.Add(text);
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

    private static List<ElementCandidate> Cap(List<ElementMatch> matches)
    {
        return matches
            .Take(UiaDefaults.MaxReportedCandidates)
            .Select(m => m.Candidate)
            .ToList();
    }

    private static string DescribeCandidates(IReadOnlyList<ElementCandidate> candidates)
    {
        return string.Join("; ", candidates.Select(c =>
            $"[{c.Index}] automationId=\"{c.AutomationId}\" name=\"{c.Name}\" controlType={c.ControlType} window=\"{c.WindowTitle}\""));
    }
}
