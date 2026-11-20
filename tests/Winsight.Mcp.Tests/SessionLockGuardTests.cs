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
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class SessionLockGuardTests
{
    [Fact]
    public void GuardMutation_refuses_with_session_locked_before_yield_when_session_is_locked()
    {
        bool yieldCalled = false;
        OperationResult? result = ToolAccessPolicy.GuardMutation(
            "click_point",
            new { x = 10, y = 20 },
            checkSessionLocked: () => "session_locked: Windows session is locked (source=wts).",
            checkYield: () =>
            {
                yieldCalled = true;
                return null;
            });

        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Error.Should().StartWith("session_locked:");
        yieldCalled.Should().BeFalse("session_locked takes precedence over yield-to-user");
    }

    [Fact]
    public void GetWinsightGuide_documents_session_locked_in_errorPolicy()
    {
        string json = AgentExperienceTool.GetWinsightGuide();

        json.Should().Contain("\"session_locked\"");
    }
}
