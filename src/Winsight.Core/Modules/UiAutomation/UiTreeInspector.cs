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

using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

public sealed class UiTreeInspector
{
    public const int DefaultMaxDepth = 5;
    public const int MaxAllowedDepth = 20;

    public UiElementNode Inspect(int processId, int maxDepth = DefaultMaxDepth)
    {
        if (maxDepth < 0 || maxDepth > MaxAllowedDepth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDepth),
                $"maxDepth must be between 0 and {MaxAllowedDepth}.");
        }

        using var automation = new UIA3Automation();
        var desktop = automation.GetDesktop();
        var window = desktop.FindAllChildren()
            .FirstOrDefault(e => e.Properties.ProcessId.ValueOrDefault == processId);

        if (window is null)
        {
            throw new InvalidOperationException(
                $"No top-level window found for process {processId}.");
        }

        return BuildNode(window, depth: 0, maxDepth);
    }

    private static UiElementNode BuildNode(AutomationElement element, int depth, int maxDepth)
    {
        IReadOnlyList<UiElementNode> children = depth >= maxDepth
            ? Array.Empty<UiElementNode>()
            : element.FindAllChildren()
                .Select(child => BuildNode(child, depth + 1, maxDepth))
                .ToArray();

        var rect = element.BoundingRectangle;
        return new UiElementNode(
            AutomationId: element.Properties.AutomationId.ValueOrDefault ?? string.Empty,
            Name: element.Properties.Name.ValueOrDefault ?? string.Empty,
            ControlType: element.Properties.ControlType.ValueOrDefault.ToString(),
            ClassName: element.Properties.ClassName.ValueOrDefault ?? string.Empty,
            IsEnabled: element.Properties.IsEnabled.ValueOrDefault,
            IsVisible: !element.Properties.IsOffscreen.ValueOrDefault,
            X: rect.X,
            Y: rect.Y,
            Width: rect.Width,
            Height: rect.Height,
            Children: children);
    }
}
