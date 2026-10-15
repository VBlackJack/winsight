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

public class WindowActionParserTests
{
    [Theory]
    [InlineData("move", WindowAction.Move)]
    [InlineData("RESIZE", WindowAction.Resize)]
    [InlineData("set_bounds", WindowAction.SetBounds)]
    [InlineData("maximize", WindowAction.Maximize)]
    [InlineData("minimize", WindowAction.Minimize)]
    [InlineData(" restore ", WindowAction.Restore)]
    public void Parses_every_action(string raw, WindowAction expected)
    {
        WindowActionParser.TryParse(raw, out WindowAction action).Should().BeTrue();
        action.Should().Be(expected);
    }

    [Fact]
    public void Rejects_unknown_actions()
    {
        WindowActionParser.TryParse("close", out _).Should().BeFalse();
    }

    [Fact]
    public void Each_action_requires_its_coordinates()
    {
        WindowActionParser.ValidateArguments(WindowAction.Move, 10, null, null, null).Should().Contain("x and y");
        WindowActionParser.ValidateArguments(WindowAction.Resize, null, null, 800, null).Should().Contain("width and height");
        WindowActionParser.ValidateArguments(WindowAction.SetBounds, 0, 0, 0, 600).Should().Contain("positive");
        WindowActionParser.ValidateArguments(WindowAction.SetBounds, 0, 0, 800, 600).Should().BeNull();
        WindowActionParser.ValidateArguments(WindowAction.Maximize, null, null, null, null).Should().BeNull();
    }
}
