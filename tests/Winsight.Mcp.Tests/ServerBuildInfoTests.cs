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

using System.Text.Json;
using FluentAssertions;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

/// <summary>
/// A session that started before a rebuild keeps the old server process and
/// its tool list; the guide must say which build runs and whether a newer one exists.
/// </summary>
public sealed class ServerBuildInfoTests
{
    private static readonly DateTime Built = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_same_build_copied_elsewhere_is_not_newer()
    {
        ServerBuildInfo.IsNewer(new BuildStamp("0.1.0+abc", Built), new BuildStamp("0.1.0+abc", Built.AddSeconds(1)))
            .Should().BeFalse();
    }

    [Fact]
    public void A_rebuild_from_another_commit_is_newer()
    {
        ServerBuildInfo.IsNewer(new BuildStamp("0.1.0+abc", Built), new BuildStamp("0.1.0+def", Built))
            .Should().BeTrue();
    }

    [Fact]
    public void A_later_rebuild_of_the_same_commit_is_newer()
    {
        ServerBuildInfo.IsNewer(new BuildStamp("0.1.0+abc", Built), new BuildStamp("0.1.0+abc", Built.AddMinutes(5)))
            .Should().BeTrue();
    }

    [Fact]
    public void An_unknown_source_build_says_nothing()
    {
        ServerBuildInfo.IsNewer(new BuildStamp("0.1.0+abc", Built), null).Should().BeNull();
    }

    [Fact]
    public void The_guide_reports_the_running_build_and_its_tools()
    {
        using JsonDocument guide = JsonDocument.Parse(AgentExperienceTool.GetWinsightGuide());

        JsonElement server = guide.RootElement.GetProperty("data").GetProperty("server");
        server.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        server.GetProperty("registeredTools").ValueKind.Should().Be(JsonValueKind.Array);
        server.GetProperty("location").GetString().Should().EndWith(".dll");
    }
}
