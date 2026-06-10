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

public class WireParserTests
{
    [Theory]
    [InlineData("exists", WaitCondition.Exists)]
    [InlineData("not_exists", WaitCondition.NotExists)]
    [InlineData("NOT_EXISTS", WaitCondition.NotExists)]
    [InlineData("NotExists", WaitCondition.NotExists)]
    [InlineData("enabled", WaitCondition.Enabled)]
    [InlineData("Visible", WaitCondition.Visible)]
    public void Wait_condition_parses_wire_names(string raw, WaitCondition expected)
    {
        WaitConditionParser.TryParse(raw, out WaitCondition parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("gone")]
    [InlineData("3")]
    public void Wait_condition_rejects_unknown_names(string? raw)
    {
        WaitConditionParser.TryParse(raw, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("single", ClickStyle.Primary)]
    [InlineData("SINGLE", ClickStyle.Primary)]
    [InlineData("double", ClickStyle.DoubleClick)]
    [InlineData("right", ClickStyle.RightClick)]
    [InlineData(" right ", ClickStyle.RightClick)]
    public void Click_style_parses_wire_names(string raw, ClickStyle expected)
    {
        ClickStyleParser.TryParse(raw, out ClickStyle parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("middle")]
    [InlineData("Primary")]
    public void Click_style_rejects_unknown_names(string? raw)
    {
        ClickStyleParser.TryParse(raw, out _).Should().BeFalse();
    }
}
