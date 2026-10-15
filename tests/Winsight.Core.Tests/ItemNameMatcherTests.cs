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

public class ItemNameMatcherTests
{
    private const string EarlierName = "lab-c, protocol SFTP, state Reachable (1 ms) · 13:03:33";
    private const string LaterName = "lab-c, protocol SFTP, state Reachable (1 ms) · 13:03:34";

    [Fact]
    public void An_exact_name_stops_matching_once_the_clock_in_it_moves()
    {
        ItemNameMatcher exact = ItemNameMatcher.Exact(EarlierName);

        exact.Matches(EarlierName).Should().BeTrue();
        exact.Matches(LaterName).Should().BeFalse();
    }

    [Fact]
    public void A_substring_keeps_matching_while_the_clock_moves()
    {
        Create(null, "LAB-C, protocol sftp", null).Matches(LaterName).Should().BeTrue();
    }

    [Fact]
    public void A_pattern_keeps_matching_while_the_clock_moves()
    {
        ItemNameMatcher matcher = Create(null, null, @"^lab-c, protocol SFTP, state \w+ \(\d+ ms\)");

        matcher.Matches(EarlierName).Should().BeTrue();
        matcher.Matches(LaterName).Should().BeTrue();
        matcher.Matches("lab-a, protocol SSH, state Reachable (3 ms) · 13:03:34").Should().BeFalse();
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("a", "a", null)]
    [InlineData("a", null, "a")]
    public void Exactly_one_criterion_is_required(string? exact, string? contains, string? pattern)
    {
        ItemNameMatcher.TryCreate(exact, contains, pattern, out _, out string? error).Should().BeFalse();
        error.Should().Contain("exactly one");
    }

    [Fact]
    public void An_invalid_pattern_is_reported()
    {
        ItemNameMatcher.TryCreate(null, null, "(unclosed", out _, out string? error).Should().BeFalse();
        error.Should().Contain("itemNamePattern");
    }

    [Fact]
    public void A_null_name_never_matches()
    {
        Create(null, "lab", null).Matches(null).Should().BeFalse();
    }

    private static ItemNameMatcher Create(string? exact, string? contains, string? pattern)
    {
        ItemNameMatcher.TryCreate(exact, contains, pattern, out ItemNameMatcher? matcher, out string? error)
            .Should().BeTrue(error);
        return matcher!;
    }
}
