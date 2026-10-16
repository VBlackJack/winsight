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

using System.Globalization;
using FluentAssertions;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

/// <summary>
/// Attribute descriptions must be constants, so the settle bounds an agent
/// reads are spelled out by hand; this pins them to the values the engine enforces.
/// </summary>
public sealed class InvokeActionToolDescriptionTests
{
    [Fact]
    public void Settle_description_states_the_enforced_bounds_and_default()
    {
        string max = PointerDispatch.MaxSettleMs.ToString(CultureInfo.InvariantCulture);
        string fallback = PointerDispatch.DefaultSettleMs.ToString(CultureInfo.InvariantCulture);

        InvokeActionTool.SettleMsDescription.Should()
            .Contain($"0 to {max}")
            .And.Contain($"default {fallback}");
    }
}
