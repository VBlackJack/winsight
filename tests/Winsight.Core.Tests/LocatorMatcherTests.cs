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

using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public class LocatorMatcherTests
{
    private static readonly string[] SemanticLabelTexts = { "Semantic Label" };

    private static readonly string[] CancelTexts = { "Cancel" };

    [Fact]
    public void Exact_automation_id_match_is_case_sensitive()
    {
        var locator = new ElementLocator(AutomationId: "IncrementButton");

        LocatorMatcher.Matches(locator, "IncrementButton", null, null, null).Should().BeTrue();
        LocatorMatcher.Matches(locator, "incrementbutton", null, null, null).Should().BeFalse();
    }

    [Fact]
    public void All_set_criteria_must_match()
    {
        var locator = new ElementLocator(AutomationId: "IncrementButton", ControlType: "Button");

        LocatorMatcher.Matches(locator, "IncrementButton", "Increment", "Button", "Button").Should().BeTrue();
        LocatorMatcher.Matches(locator, "IncrementButton", "Increment", "Text", "Text").Should().BeFalse();
    }

    [Fact]
    public void Control_type_match_is_case_insensitive()
    {
        var locator = new ElementLocator(ControlType: "button");

        LocatorMatcher.Matches(locator, null, null, null, "Button").Should().BeTrue();
    }

    [Fact]
    public void Name_contains_matches_substring_case_insensitively()
    {
        var locator = new ElementLocator(NameContains: "count:");

        LocatorMatcher.Matches(locator, null, "Count: 5", null, "Text").Should().BeTrue();
        LocatorMatcher.Matches(locator, null, "Total: 5", null, "Text").Should().BeFalse();
    }

    [Fact]
    public void Name_contains_does_not_match_null_name()
    {
        var locator = new ElementLocator(NameContains: "Count");

        LocatorMatcher.Matches(locator, "CountLabel", null, null, "Text").Should().BeFalse();
    }

    [Fact]
    public void Descendant_text_matches_descendant_name_case_insensitively()
    {
        var locator = new ElementLocator(DescendantText: "semantic label");

        LocatorMatcher.Matches(
                locator,
                "Container",
                "Winsight.ViewModels.SemanticItem",
                null,
                "Button",
                SemanticLabelTexts)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Descendant_text_must_match_when_set()
    {
        var locator = new ElementLocator(DescendantText: "Save");

        LocatorMatcher.Matches(
                locator,
                "Container",
                "Winsight.ViewModels.SemanticItem",
                null,
                "Button",
                CancelTexts)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Index_is_ignored_by_single_element_matching()
    {
        var locator = new ElementLocator(ControlType: "Button", Index: 2);

        LocatorMatcher.Matches(locator, null, null, null, "Button").Should().BeTrue();
    }

    [Theory]
    [InlineData("IncrementButton", null, "IncrementButton", "anything")]
    [InlineData(null, "Increment", "SomeId", "The Increment action")]
    [InlineData(null, "increment", "IncrementButton", null)]
    public void Near_miss_detects_partial_overlap(
        string? locatorId, string? locatorName, string? automationId, string? name)
    {
        var locator = new ElementLocator(locatorId, locatorName);

        LocatorMatcher.IsNearMiss(locator, automationId, name).Should().BeTrue();
    }

    [Fact]
    public void Near_miss_is_false_without_textual_overlap()
    {
        var locator = new ElementLocator(AutomationId: "SubmitButton");

        LocatorMatcher.IsNearMiss(locator, "CancelLink", "Cancel").Should().BeFalse();
    }

    [Fact]
    public void Score_explains_exact_and_semantic_locator_matches()
    {
        var locator = new ElementLocator(
            AutomationId: "IncrementButton",
            ControlType: "Button",
            DescendantText: "Semantic Label");

        LocatorScore score = LocatorMatcher.Score(
            locator,
            "IncrementButton",
            "Increment",
            "Button",
            "Button",
            SemanticLabelTexts);

        score.Score.Should().BeGreaterThan(0);
        score.Reason.Should().Contain("automationId");
        score.Reason.Should().Contain("controlType");
        score.Reason.Should().Contain("descendantText");
    }

    [Fact]
    public void Score_reports_partial_text_for_near_misses()
    {
        var locator = new ElementLocator(AutomationId: "IncrementButton");

        LocatorScore score = LocatorMatcher.Score(
            locator,
            "Other",
            "The IncrementButton action",
            null,
            "Button");

        score.Score.Should().BeGreaterThan(0);
        score.Reason.Should().Contain("partialText");
    }
}
