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

public class TraversalBudgetTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan delta) => _ticks += delta.Ticks;
    }

    [Theory]
    [InlineData(null, UiaDefaults.DefaultSceneTraversalBudgetMs)]
    [InlineData("", UiaDefaults.DefaultSceneTraversalBudgetMs)]
    [InlineData("not-a-number", UiaDefaults.DefaultSceneTraversalBudgetMs)]
    [InlineData("0", UiaDefaults.DefaultSceneTraversalBudgetMs)]
    [InlineData("-500", UiaDefaults.DefaultSceneTraversalBudgetMs)]
    [InlineData("50", UiaDefaults.DefaultSceneTraversalBudgetMs)]
    [InlineData("999999", UiaDefaults.DefaultSceneTraversalBudgetMs)]
    [InlineData("100", 100)]
    [InlineData("2500", 2500)]
    [InlineData("120000", 120000)]
    public void ResolveSceneTraversalBudgetMs_validates_env_override(string? raw, int expected)
    {
        int resolved = UiaDefaults.ResolveSceneTraversalBudgetMs(_ => raw);
        resolved.Should().Be(expected);
    }

    [Fact]
    public void TryStep_stops_with_time_budget_when_elapsed_reaches_budget()
    {
        FakeTimeProvider clock = new();
        TraversalBudget budget = new(TimeSpan.FromMilliseconds(500), maxNodes: 1000, timeProvider: clock);

        budget.TryStep().Should().BeTrue();
        budget.TryStep().Should().BeTrue();
        budget.IsTruncated.Should().BeFalse();

        clock.Advance(TimeSpan.FromMilliseconds(500));

        budget.TryStep().Should().BeFalse();
        budget.IsTruncated.Should().BeTrue();
        budget.TruncationReason.Should().Be(TraversalTruncationReasons.TimeBudget);
    }

    [Fact]
    public void TryStep_stops_with_node_limit_when_visited_exceeds_maxNodes()
    {
        FakeTimeProvider clock = new();
        TraversalBudget budget = new(TimeSpan.FromSeconds(10), maxNodes: 2, timeProvider: clock);

        budget.TryStep().Should().BeTrue();
        budget.TryStep().Should().BeTrue();
        budget.TryStep().Should().BeFalse();
        budget.IsTruncated.Should().BeTrue();
        budget.TruncationReason.Should().Be(TraversalTruncationReasons.NodeLimit);
    }

    [Fact]
    public void MarkTruncated_preserves_initial_reason()
    {
        FakeTimeProvider clock = new();
        TraversalBudget budget = new(TimeSpan.FromSeconds(10), maxNodes: 10, timeProvider: clock);

        budget.MarkTruncated(TraversalTruncationReasons.ComError);
        budget.MarkTruncated(TraversalTruncationReasons.MaxResults);

        budget.TruncationReason.Should().Be(TraversalTruncationReasons.ComError);
    }
}
