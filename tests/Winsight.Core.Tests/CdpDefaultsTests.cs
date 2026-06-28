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
using Winsight.Core.Modules.Cdp;
using Xunit;

namespace Winsight.Core.Tests;

public sealed class CdpDefaultsTests
{
    [Fact]
    public void NormalizeCommandTimeoutMs_accepts_values_within_the_cap()
    {
        CdpDefaults.NormalizeCommandTimeoutMs(1234).Should().Be(1234);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(CdpDefaults.MaxCommandTimeoutMs + 1)]
    public void NormalizeCommandTimeoutMs_rejects_values_outside_the_cap(int timeoutMs)
    {
        Action act = () => CdpDefaults.NormalizeCommandTimeoutMs(timeoutMs);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
