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

using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class ListWindowsTitlePolicyTests
{
    private const int TargetPid = 100;
    private const int OtherPid = 200;
    private const string TargetTitle = "Target Document";
    private const string OtherTitle = "Private Chat Channel";
    private const string TitleWithheldProperty = "titleWithheld";
    private const string TitleProperty = "title";
    private const string ToolName = "list_windows";
    private const string IncludeTitlesParameter = "includeTitles";

    private static readonly string[] InjectedServiceParameters = { "discovery", "target" };

    private static readonly WindowDescriptor[] Windows =
    {
        new(TargetPid, 11, TargetTitle, "target", true, 1, 2, 300, 400, "main", "TargetClass", 0, false),
        new(OtherPid, 22, OtherTitle, "other", false, 5, 6, 700, 800, "dialog", "OtherClass", 11, true),
    };

    [Fact]
    public void No_target_and_no_opt_in_withholds_every_title()
    {
        IReadOnlyList<ListedWindow> result = WindowListProjection.Project(Windows, null, includeTitles: false);

        result.Should().OnlyContain(w => w.Title == string.Empty && w.TitleWithheld);
    }

    [Fact]
    public void Target_set_keeps_only_the_target_titles()
    {
        IReadOnlyList<ListedWindow> result = WindowListProjection.Project(Windows, TargetPid, includeTitles: false);

        result[0].Title.Should().Be(TargetTitle);
        result[0].TitleWithheld.Should().BeFalse();
        result[1].Title.Should().BeEmpty();
        result[1].TitleWithheld.Should().BeTrue();
    }

    [Fact]
    public void Opt_in_keeps_every_title()
    {
        IReadOnlyList<ListedWindow> result = WindowListProjection.Project(Windows, null, includeTitles: true);

        result.Select(w => w.Title).Should().Equal(TargetTitle, OtherTitle);
        result.Should().OnlyContain(w => !w.TitleWithheld);
    }

    [Fact]
    public void Projection_never_alters_the_non_title_fields()
    {
        IReadOnlyList<ListedWindow> result = WindowListProjection.Project(Windows, null, includeTitles: false);

        for (int i = 0; i < Windows.Length; i++)
        {
            WindowDescriptor source = Windows[i];
            ListedWindow projected = result[i];
            projected.ProcessId.Should().Be(source.ProcessId);
            projected.WindowHandle.Should().Be(source.WindowHandle);
            projected.ProcessName.Should().Be(source.ProcessName);
            projected.IsForeground.Should().Be(source.IsForeground);
            projected.X.Should().Be(source.X);
            projected.Y.Should().Be(source.Y);
            projected.Width.Should().Be(source.Width);
            projected.Height.Should().Be(source.Height);
            projected.Kind.Should().Be(source.Kind);
            projected.ClassName.Should().Be(source.ClassName);
            projected.OwnerHandle.Should().Be(source.OwnerHandle);
            projected.IsModal.Should().Be(source.IsModal);
        }
    }

    [Fact]
    public void Desktop_wide_json_carries_title_withheld()
    {
        string json = ListWindowsTool.Render(Windows, TargetPid, processId: null, includeTitles: false);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement[] items = document.RootElement.EnumerateArray().ToArray();
        items[0].GetProperty(TitleWithheldProperty).GetBoolean().Should().BeFalse();
        items[0].GetProperty(TitleProperty).GetString().Should().Be(TargetTitle);
        items[1].GetProperty(TitleWithheldProperty).GetBoolean().Should().BeTrue();
        items[1].GetProperty(TitleProperty).GetString().Should().BeEmpty();
    }

    [Fact]
    public void Per_process_json_keeps_real_titles()
    {
        string json = ListWindowsTool.Render(Windows, null, processId: OtherPid, includeTitles: false);

        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.EnumerateArray()
            .Select(item => item.GetProperty(TitleProperty).GetString())
            .Should().Equal(TargetTitle, OtherTitle);
        document.RootElement.EnumerateArray()
            .Should().OnlyContain(item => !item.GetProperty(TitleWithheldProperty).GetBoolean());
    }

    [Fact]
    public void Tool_schema_exposes_include_titles_and_hides_injected_services()
    {
        MethodInfo method = typeof(ListWindowsTool).GetMethods()
            .Single(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);
        ServiceCollection services = new();
        services.AddSingleton<WindowDiscovery>();
        services.AddSingleton<SessionTarget>();
        using ServiceProvider provider = services.BuildServiceProvider();

        McpServerTool tool = McpServerTool.Create(
            method,
            target: null,
            new McpServerToolCreateOptions { Services = provider });

        tool.ProtocolTool.Name.Should().Be(ToolName);
        JsonElement properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
        properties.TryGetProperty(IncludeTitlesParameter, out _).Should().BeTrue();
        properties.TryGetProperty("processId", out _).Should().BeTrue();
        properties.EnumerateObject().Select(p => p.Name)
            .Should().NotContain(InjectedServiceParameters);
    }
}
