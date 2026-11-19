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

public sealed record UiAccessibilityFinding(
    string Severity,
    string Rule,
    string ElementRef,
    string Role,
    string Name,
    WindowBounds Bounds,
    string Message);

public sealed record UiAccessibilityAuditReport(
    int TotalElements,
    int InteractiveElements,
    int Errors,
    int Warnings,
    int Score,
    IReadOnlyList<UiAccessibilityFinding> Findings);

/// <summary>
/// Deterministic UI Automation QA and accessibility auditor. Inspects a
/// captured scene for missing accessible names, missing AutomationIds,
/// zero-size visible controls, keyboard-unreachable controls, and overlapping
/// interactive bounding boxes.
/// </summary>
public static class UiAccessibilityAuditor
{
    private static readonly HashSet<string> InteractiveRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Button",
        "SplitButton",
        "CheckBox",
        "RadioButton",
        "Edit",
        "ComboBox",
        "ListItem",
        "MenuItem",
        "Hyperlink",
        "TabItem",
        "Slider",
        "Spinner",
    };

    public static UiAccessibilityAuditReport Audit(SceneCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        List<UiAccessibilityFinding> findings = new();
        List<SceneElement> interactive = capture.Elements
            .Where(e => InteractiveRoles.Contains(e.Role))
            .ToList();

        foreach (SceneElement element in interactive)
        {
            if (string.IsNullOrWhiteSpace(element.Name))
            {
                findings.Add(new UiAccessibilityFinding(
                    "error",
                    "missing_accessible_name",
                    element.Ref,
                    element.Role,
                    element.Name,
                    element.Bounds,
                    $"{element.Role} ({element.Ref}) has an empty accessible Name."));
            }

            if (string.IsNullOrWhiteSpace(element.Locator.AutomationId))
            {
                findings.Add(new UiAccessibilityFinding(
                    "warning",
                    "missing_automation_id",
                    element.Ref,
                    element.Role,
                    element.Name,
                    element.Bounds,
                    $"{element.Role} \"{element.Name}\" ({element.Ref}) has no AutomationId."));
            }

            if (element.Visible && (element.Bounds.Width <= 0 || element.Bounds.Height <= 0))
            {
                findings.Add(new UiAccessibilityFinding(
                    "error",
                    "zero_size_visible",
                    element.Ref,
                    element.Role,
                    element.Name,
                    element.Bounds,
                    $"{element.Role} \"{element.Name}\" ({element.Ref}) is marked visible but has zero-area bounds."));
            }

            if (element.Visible && element.Enabled && !element.Actions.Contains("focus", StringComparer.Ordinal))
            {
                findings.Add(new UiAccessibilityFinding(
                    "warning",
                    "not_keyboard_focusable",
                    element.Ref,
                    element.Role,
                    element.Name,
                    element.Bounds,
                    $"{element.Role} \"{element.Name}\" ({element.Ref}) is enabled and visible but not keyboard focusable."));
            }
        }

        List<SceneElement> visibleBoxes = interactive
            .Where(e => e.Visible && e.Bounds.Width > 0 && e.Bounds.Height > 0)
            .ToList();
        for (int i = 0; i < visibleBoxes.Count; i++)
        {
            for (int j = i + 1; j < visibleBoxes.Count; j++)
            {
                SceneElement a = visibleBoxes[i];
                SceneElement b = visibleBoxes[j];
                if (a.Window.Handle != b.Window.Handle) continue;
                double ratio = OverlapRatio(a.Bounds, b.Bounds);
                if (ratio > 0.5)
                {
                    findings.Add(new UiAccessibilityFinding(
                        "warning",
                        "overlapping_interactables",
                        $"{a.Ref}+{b.Ref}",
                        $"{a.Role}/{b.Role}",
                        $"{a.Name} / {b.Name}",
                        a.Bounds,
                        $"Interactive controls {a.Ref} (\"{a.Name}\") and {b.Ref} (\"{b.Name}\") overlap by {Math.Round(ratio * 100)}%. "));
                }
            }
        }

        int errors = findings.Count(f => f.Severity == "error");
        int warnings = findings.Count(f => f.Severity == "warning");
        int score = Math.Max(0, 100 - (errors * 10) - (warnings * 3));

        return new UiAccessibilityAuditReport(
            capture.Elements.Count,
            interactive.Count,
            errors,
            warnings,
            score,
            findings);
    }

    private static double OverlapRatio(WindowBounds a, WindowBounds b)
    {
        int x1 = Math.Max(a.Left, b.Left);
        int y1 = Math.Max(a.Top, b.Top);
        int x2 = Math.Min(a.Left + a.Width, b.Left + b.Width);
        int y2 = Math.Min(a.Top + a.Height, b.Top + b.Height);
        if (x2 <= x1 || y2 <= y1) return 0.0;

        double intersection = (double)(x2 - x1) * (y2 - y1);
        double minArea = Math.Min((double)a.Width * a.Height, (double)b.Width * b.Height);
        return minArea <= 0 ? 0.0 : intersection / minArea;
    }
}
