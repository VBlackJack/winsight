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

public sealed class ProcessIntegrityTests
{
    [Fact]
    public void Current_process_integrity_level_is_readable()
    {
        ProcessIntegrityInfo current = ProcessIntegrity.QueryCurrent();

        current.Rid.Should().NotBeNull();
        current.AccessDenied.Should().BeFalse();
        current.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Higher_target_integrity_is_reported_as_uipi_blocked()
    {
        ProcessIntegrityInfo medium = new(ProcessIntegrity.RidMedium, "medium", false, null);
        ProcessIntegrityInfo high = new(ProcessIntegrity.RidHigh, "high", false, null);

        IntegrityComparison comparison = ProcessIntegrity.Compare(medium, high);

        comparison.IsUipiBlocked.Should().BeTrue();
        comparison.Warning.Should().StartWith($"{ProcessIntegrity.UipiBlockedCode}:")
            .And.Contain("medium")
            .And.Contain("high")
            .And.Contain("silently drop");
    }

    [Fact]
    public void Same_or_lower_target_integrity_is_allowed()
    {
        ProcessIntegrityInfo high = new(ProcessIntegrity.RidHigh, "high", false, null);
        ProcessIntegrityInfo medium = new(ProcessIntegrity.RidMedium, "medium", false, null);

        ProcessIntegrity.Compare(medium, medium).IsUipiBlocked.Should().BeFalse();
        ProcessIntegrity.Compare(high, medium).IsUipiBlocked.Should().BeFalse();
        ProcessIntegrity.Compare(high, high).IsUipiBlocked.Should().BeFalse();
    }

    [Fact]
    public void Access_denied_on_target_token_reports_unknown_and_does_not_block_input()
    {
        ProcessIntegrityInfo medium = new(ProcessIntegrity.RidMedium, "medium", false, null);
        ProcessIntegrityInfo denied = new(null, "unknown", AccessDenied: true, Win32Error: 5);

        IntegrityComparison comparison = ProcessIntegrity.Compare(medium, denied);

        comparison.IsUipiBlocked.Should().BeFalse();
        comparison.Warning.Should().BeNull();
        comparison.Target.Name.Should().Be("unknown");
        comparison.Target.AccessDenied.Should().BeTrue();
        comparison.Target.Win32Error.Should().Be(5);
    }

    [Theory]
    [InlineData(ProcessIntegrity.RidUntrusted, "untrusted")]
    [InlineData(ProcessIntegrity.RidLow, "low")]
    [InlineData(ProcessIntegrity.RidMedium, "medium")]
    [InlineData(ProcessIntegrity.RidMediumPlus, "medium_plus")]
    [InlineData(ProcessIntegrity.RidHigh, "high")]
    [InlineData(ProcessIntegrity.RidSystem, "system")]
    [InlineData(ProcessIntegrity.RidProtectedProcess, "protected_process")]
    public void Classifies_standard_integrity_rids(uint rid, string expectedName)
    {
        ProcessIntegrity.ClassifyRid(rid).Should().Be(expectedName);
    }
}
