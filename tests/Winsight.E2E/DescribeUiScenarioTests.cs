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
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// describe_ui surfaces the sample app's actionable elements with usable
/// locators, so an agent can orient and act without parsing the full UIA tree.
/// </summary>
[Trait("Category", "E2E")]
public class DescribeUiScenarioTests
{
    private const string ReadyLabelId = "CountLabel";
    private const string IncrementButtonId = "IncrementButton";
    private const string OptionSelectorId = "OptionSelector";

    private readonly ITestOutputHelper _output;

    public DescribeUiScenarioTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Describe_surfaces_actionable_elements_with_usable_locators()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        try
        {
            OperationResult appeared = new ElementWaiter().Wait(
                process.Id, new ElementLocator(AutomationId: ReadyLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);

            IReadOnlyList<UiInteractable> elements = new AffordanceScanner().Describe(process.Id);

            foreach (UiInteractable e in elements)
            {
                _output.WriteLine($"{e.ControlType,-12} {e.RecommendedLocator,-34} \"{e.Name}\"");
            }

            // The Increment button is surfaced as a Button with its AutomationId
            // as the recommended locator.
            UiInteractable? increment = elements.FirstOrDefault(e => e.AutomationId == IncrementButtonId);
            increment.Should().NotBeNull("the Increment button must be an actionable element");
            increment!.ControlType.Should().Be("Button");
            increment.RecommendedLocator.Should().Be($"automationId=\"{IncrementButtonId}\"");
            increment.IsEnabled.Should().BeTrue();

            // The ComboBox is surfaced too (a select_item target).
            elements.Should().Contain(e => e.AutomationId == OptionSelectorId && e.ControlType == "ComboBox");

            // Only interactable kinds — the static Count label is not in the list.
            elements.Should().NotContain(e => e.AutomationId == ReadyLabelId);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
