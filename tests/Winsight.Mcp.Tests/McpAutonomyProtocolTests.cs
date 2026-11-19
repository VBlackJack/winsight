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

using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Winsight.Mcp.Prompts;
using Winsight.Mcp.Resources;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class McpAutonomyProtocolTests
{
    [Fact]
    public void Every_mcp_tool_declares_name_and_description()
    {
        Assembly assembly = typeof(Program).Assembly;
        List<MethodInfo> toolMethods = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToList();

        toolMethods.Should().NotBeEmpty();
        foreach (MethodInfo method in toolMethods)
        {
            McpServerToolAttribute attr = method.GetCustomAttribute<McpServerToolAttribute>()!;
            attr.Name.Should().NotBeNullOrWhiteSpace(because: $"{method.DeclaringType?.Name}.{method.Name} must declare an explicit tool name");
            method.GetCustomAttribute<DescriptionAttribute>()?.Description.Should().NotBeNullOrWhiteSpace(
                because: $"tool '{attr.Name}' must declare a non-empty description");
        }
    }

    [Fact]
    public void Read_only_perception_tools_declare_read_only_annotation()
    {
        string[] expectedReadOnly =
        [
            "observe_scene",
            "capture_scene",
            "capture_screenshot",
            "list_windows",
            "get_agent_state",
            "get_winsight_guide",
            "read_text_visually",
            "read_managed_state",
            "get_user_activity",
        ];

        Dictionary<string, McpServerToolAttribute> byName = typeof(Program).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null && !string.IsNullOrEmpty(a.Name))
            .ToDictionary(a => a!.Name!, a => a!, StringComparer.Ordinal);

        foreach (string name in expectedReadOnly)
        {
            byName.Should().ContainKey(name);
            byName[name].ReadOnly.Should().BeTrue(because: $"'{name}' is a perception tool and should be auto-approvable");
            byName[name].Destructive.Should().BeFalse();
        }
    }

    [Fact]
    public void Server_configuration_sets_instructions_and_applies_compact_profile()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithToolsFromAssembly(typeof(Program).Assembly);
        using ServiceProvider provider = services.BuildServiceProvider();
        McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        int fullCount = options.ToolCollection!.Count;
        fullCount.Should().BeGreaterThanOrEqualTo(61);

        McpServerConfiguration config = new();
        config.Configure(options);
        options.ServerInstructions.Should().Contain("observe_scene").And.Contain("act_verified");

        McpServerConfiguration.ApplyToolProfile(options, "compact");
        options.ToolCollection!.Count.Should().BeLessThan(fullCount);
        options.ToolCollection.Select(t => t.ProtocolTool.Name).Should().Contain("observe_scene");
        options.ToolCollection.Select(t => t.ProtocolTool.Name).Should().Contain("act_verified");
        options.ToolCollection.Select(t => t.ProtocolTool.Name).Should().NotContain("describe_ui");
    }

    [Fact]
    public void Resources_return_valid_json()
    {
        string guide = WinsightResources.GetGuide();
        using (JsonDocument doc = JsonDocument.Parse(guide))
        {
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        }

        string state = WinsightResources.GetState(new SessionTarget(), new SessionMemory());
        using (JsonDocument doc = JsonDocument.Parse(state))
        {
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        }

        string latest = WinsightResources.GetLatestScene(new SceneStore());
        using (JsonDocument doc = JsonDocument.Parse(latest))
        {
            doc.RootElement.GetProperty("available").GetBoolean().Should().BeFalse();
        }
    }

    [Fact]
    public void Prompts_return_actionable_playbooks()
    {
        WinsightPrompts.ExploreApp("SampleWpfApp.exe").Should().Contain("observe_scene").And.Contain("capture_scene");
        WinsightPrompts.VerifyFlow("Increment counter").Should().Contain("act_verified").And.Contain("save_app_profile");
        WinsightPrompts.DiagnoseUiBug("Crash on save").Should().Contain("captureExceptions=true").And.Contain("read_managed_state");
    }
}
