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
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class InvokeActionToolTests
{
    [Fact]
    public void Click_point_dry_run_validates_coordinate_space_before_preview()
    {
        string? before = Environment.GetEnvironmentVariable(ToolAccessPolicy.DryRunVariable);
        Environment.SetEnvironmentVariable(ToolAccessPolicy.DryRunVariable, "true");
        try
        {
            string json = InvokeActionTool.ClickPoint(
                new ActionInvoker(),
                new SessionTarget(),
                x: 1,
                y: 2,
                coordinateSpace: "desktop",
                processId: Environment.ProcessId);

            using JsonDocument document = JsonDocument.Parse(json);
            document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            document.RootElement.GetProperty("error").GetString().Should().Contain("coordinateSpace");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ToolAccessPolicy.DryRunVariable, before);
        }
    }
}
