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
using System.Globalization;
using Winsight.Mcp;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class SessionTargetTests
{
    [Fact]
    public void Resolve_prefers_an_explicit_process_id_over_the_default()
    {
        SessionTarget target = new();
        target.Set(100);

        target.TryResolve(200, out int pid, out _).Should().BeTrue();
        pid.Should().Be(200, "an explicit id always wins over the session default");
    }

    [Fact]
    public void Resolve_falls_back_to_the_session_default()
    {
        SessionTarget target = new();
        target.Set(100);

        target.TryResolve(null, out int pid, out _).Should().BeTrue();
        pid.Should().Be(100);
    }

    [Fact]
    public void Resolve_fails_clearly_when_neither_is_set()
    {
        SessionTarget target = new();

        bool ok = target.TryResolve(null, out _, out string error);

        ok.Should().BeFalse();
        error.Should().Contain("set_target");
    }

    [Fact]
    public void Clear_removes_the_default_so_resolution_fails_again()
    {
        SessionTarget target = new();
        target.Set(100);
        target.Clear();

        target.ProcessId.Should().BeNull();
        target.TryResolve(null, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Resolve_rejects_processes_outside_the_allowed_id_policy()
    {
        string? before = Environment.GetEnvironmentVariable(ToolAccessPolicy.AllowedProcessIdsVariable);
        Environment.SetEnvironmentVariable(ToolAccessPolicy.AllowedProcessIdsVariable, "-1");
        try
        {
            SessionTarget target = new();

            bool ok = target.TryResolve(Environment.ProcessId, out _, out string error);

            ok.Should().BeFalse();
            error.Should().Contain(ToolAccessPolicy.AllowedProcessIdsVariable);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ToolAccessPolicy.AllowedProcessIdsVariable, before);
        }
    }

    [Fact]
    public void Resolve_accepts_processes_inside_the_allowed_id_policy()
    {
        string? before = Environment.GetEnvironmentVariable(ToolAccessPolicy.AllowedProcessIdsVariable);
        Environment.SetEnvironmentVariable(
            ToolAccessPolicy.AllowedProcessIdsVariable,
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        try
        {
            SessionTarget target = new();

            bool ok = target.TryResolve(Environment.ProcessId, out int pid, out _);

            ok.Should().BeTrue();
            pid.Should().Be(Environment.ProcessId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ToolAccessPolicy.AllowedProcessIdsVariable, before);
        }
    }

    [Fact]
    public void Resolve_rejects_allowed_but_non_running_process_ids()
    {
        string? before = Environment.GetEnvironmentVariable(ToolAccessPolicy.AllowedProcessIdsVariable);
        Environment.SetEnvironmentVariable(
            ToolAccessPolicy.AllowedProcessIdsVariable,
            int.MaxValue.ToString(CultureInfo.InvariantCulture));
        try
        {
            SessionTarget target = new();

            bool ok = target.TryResolve(int.MaxValue, out _, out string error);

            ok.Should().BeFalse();
            error.Should().Contain("not running");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ToolAccessPolicy.AllowedProcessIdsVariable, before);
        }
    }
}
