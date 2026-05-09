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

using FlaUI.Core.Input;
using FlaUI.UIA3;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

public sealed class ActionInvoker
{
    public OperationResult Click(int processId, ElementLocator locator)
    {
        if (locator.IsEmpty)
        {
            return OperationResult.Failure("Locator is empty: at least one of automationId, name, or className must be provided.");
        }

        using var automation = new UIA3Automation();
        var element = ElementFinder.Find(automation, processId, locator);
        if (element is null)
        {
            return OperationResult.Failure($"Element not found for locator {locator} in process {processId}.");
        }

        var invokePattern = element.Patterns.Invoke.PatternOrDefault;
        if (invokePattern is not null)
        {
            invokePattern.Invoke();
            return OperationResult.Ok();
        }

        var togglePattern = element.Patterns.Toggle.PatternOrDefault;
        if (togglePattern is not null)
        {
            togglePattern.Toggle();
            return OperationResult.Ok();
        }

        element.Click();
        return OperationResult.Ok();
    }

    public OperationResult Type(int processId, ElementLocator locator, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (locator.IsEmpty)
        {
            return OperationResult.Failure("Locator is empty: at least one of automationId, name, or className must be provided.");
        }

        using var automation = new UIA3Automation();
        var element = ElementFinder.Find(automation, processId, locator);
        if (element is null)
        {
            return OperationResult.Failure($"Element not found for locator {locator} in process {processId}.");
        }

        var valuePattern = element.Patterns.Value.PatternOrDefault;
        if (valuePattern is not null)
        {
            valuePattern.SetValue(text);
            return OperationResult.Ok();
        }

        element.Focus();
        Keyboard.Type(text);
        return OperationResult.Ok();
    }
}
