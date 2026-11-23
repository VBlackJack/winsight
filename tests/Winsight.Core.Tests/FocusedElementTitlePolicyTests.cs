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

public sealed class FocusedElementTitlePolicyTests
{
    private const int TargetPid = 100;
    private const int OtherPid = 200;
    private const int NoTarget = 0;

    [Fact]
    public void Focus_in_the_target_process_belongs_to_the_target()
    {
        FocusedElementReader.BelongsToTarget(TargetPid, TargetPid, OtherPid).Should().BeTrue();
    }

    [Fact]
    public void Focus_in_a_window_hosted_by_the_target_belongs_to_the_target()
    {
        FocusedElementReader.BelongsToTarget(TargetPid, OtherPid, TargetPid).Should().BeTrue();
    }

    [Fact]
    public void Focus_elsewhere_does_not_belong_so_its_title_is_withheld()
    {
        FocusedElementReader.BelongsToTarget(TargetPid, OtherPid, OtherPid).Should().BeFalse();
    }

    [Fact]
    public void No_known_target_never_matches()
    {
        FocusedElementReader.BelongsToTarget(NoTarget, NoTarget, NoTarget).Should().BeFalse();
    }
}
