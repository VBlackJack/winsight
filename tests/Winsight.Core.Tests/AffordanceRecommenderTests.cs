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
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public sealed class AffordanceRecommenderTests
{
    private static AffordanceRecommender.Candidate C(string controlType, string? automationId, string name) =>
        new(controlType, automationId, name);

    [Fact]
    public void A_unique_automation_id_is_the_recommended_locator()
    {
        List<string> result = AffordanceRecommender.Recommend(new[]
        {
            C("Button", "IncrementButton", "Increment"),
        });

        result.Should().ContainSingle().Which.Should().Be("automationId=\"IncrementButton\"");
    }

    [Fact]
    public void A_unique_name_is_used_when_there_is_no_automation_id()
    {
        List<string> result = AffordanceRecommender.Recommend(new[]
        {
            C("Button", null, "Save"),
        });

        result[0].Should().Be("name=\"Save\"");
    }

    [Fact]
    public void Falls_back_to_control_type_and_index_when_neither_is_unique()
    {
        List<string> result = AffordanceRecommender.Recommend(new[]
        {
            C("Button", null, "OK"),
            C("Button", null, "OK"),
        });

        result[0].Should().Be("controlType=Button index=0");
        result[1].Should().Be("controlType=Button index=1");
    }

    [Fact]
    public void Index_is_scoped_per_control_type_in_input_order()
    {
        List<string> result = AffordanceRecommender.Recommend(new[]
        {
            C("Button", null, "dup"),
            C("Edit", null, "dup"),
            C("Button", null, "dup"),
        });

        result[0].Should().Be("controlType=Button index=0");
        result[1].Should().Be("controlType=Edit index=0");
        result[2].Should().Be("controlType=Button index=1");
    }

    [Fact]
    public void A_duplicated_automation_id_no_longer_qualifies_but_a_unique_name_does()
    {
        List<string> result = AffordanceRecommender.Recommend(new[]
        {
            C("Button", "dup", "First"),
            C("Button", "dup", "Second"),
        });

        result[0].Should().Be("name=\"First\"", "the AutomationId is ambiguous but the name is unique");
        result[1].Should().Be("name=\"Second\"");
    }
}
