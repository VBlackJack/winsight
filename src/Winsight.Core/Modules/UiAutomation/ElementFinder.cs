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

internal static class ElementFinder
{
    public static AutomationElement? Find(UIA3Automation automation, int processId, ElementLocator locator)
    {
        if (locator.IsEmpty)
        {
            return null;
        }

        var desktop = automation.GetDesktop();
        var window = desktop.FindAllChildren()
            .FirstOrDefault(e => e.Properties.ProcessId.ValueOrDefault == processId);

        return window is null ? null : BreadthFirstSearch(window, locator);
    }

    private static AutomationElement? BreadthFirstSearch(AutomationElement root, ElementLocator locator)
    {
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (Matches(current, locator))
            {
                return current;
            }
            foreach (var child in current.FindAllChildren())
            {
                queue.Enqueue(child);
            }
        }
        return null;
    }

    private static bool Matches(AutomationElement element, ElementLocator locator)
    {
        if (!string.IsNullOrEmpty(locator.AutomationId) &&
            !string.Equals(element.Properties.AutomationId.ValueOrDefault, locator.AutomationId, StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.IsNullOrEmpty(locator.Name) &&
            !string.Equals(element.Properties.Name.ValueOrDefault, locator.Name, StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.IsNullOrEmpty(locator.ClassName) &&
            !string.Equals(element.Properties.ClassName.ValueOrDefault, locator.ClassName, StringComparison.Ordinal))
        {
            return false;
        }
        return true;
    }
}
