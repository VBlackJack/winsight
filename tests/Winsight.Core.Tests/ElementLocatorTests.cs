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
using Xunit;

namespace Winsight.Core.Tests;

public class ElementLocatorTests
{
    [Fact]
    public void Default_locator_is_empty()
    {
        var locator = new ElementLocator();
        locator.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Locator_with_only_whitespace_automation_id_is_treated_as_empty()
    {
        var locator = new ElementLocator(AutomationId: "   ");
        locator.IsEmpty.Should().BeFalse(
            "IsEmpty checks for null/empty, whitespace is left to the matching layer to reject");
    }

    [Theory]
    [InlineData("CountLabel", null, null)]
    [InlineData(null, "Increment", null)]
    [InlineData(null, null, "Button")]
    public void Locator_is_not_empty_when_any_field_is_set(string? id, string? name, string? className)
    {
        var locator = new ElementLocator(id, name, className);
        locator.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Locator_with_only_control_type_is_not_empty()
    {
        var locator = new ElementLocator(ControlType: "Button");
        locator.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Locator_with_only_name_contains_is_not_empty()
    {
        var locator = new ElementLocator(NameContains: "Count");
        locator.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Locator_with_only_index_is_empty()
    {
        var locator = new ElementLocator(Index: 2);
        locator.IsEmpty.Should().BeTrue(
            "an index alone matches everything and must not be accepted as a locator");
    }

    [Fact]
    public void Locator_with_only_descendant_text_is_not_empty()
    {
        var locator = new ElementLocator(DescendantText: "Semantic Label");
        locator.IsEmpty.Should().BeFalse();
    }
}
