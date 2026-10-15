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

public class PointRecipientPolicyTests
{
    private const int TargetProcess = 4200;
    private const uint TargetOwner = 4200;
    private const uint WebViewProcess = 9100;
    private const uint OtherProcess = 7300;
    private const long TargetWindow = 0x10010;
    private const long SecondTargetWindow = 0x10020;
    private const long OtherWindow = 0x20010;
    private const long ChildWindow = 0x30010;

    [Fact]
    public void The_target_window_itself_is_accepted()
    {
        PointRecipient recipient = new(TargetWindow, TargetOwner, TargetWindow, TargetOwner);

        PointRecipientPolicy.Accepts(recipient, TargetProcess, TargetWindow, allowOtherWindow: false)
            .Should().BeTrue();
    }

    [Fact]
    public void A_same_process_child_of_the_target_window_is_accepted()
    {
        PointRecipient recipient = new(ChildWindow, TargetOwner, TargetWindow, TargetOwner);

        PointRecipientPolicy.Accepts(recipient, TargetProcess, TargetWindow, allowOtherWindow: false)
            .Should().BeTrue();
    }

    [Fact]
    public void A_webview2_surface_hosted_by_the_target_window_is_accepted()
    {
        // Chrome_WidgetWin_1 owned by msedgewebview2.exe, parented into the host window.
        PointRecipient recipient = new(ChildWindow, WebViewProcess, TargetWindow, TargetOwner);

        PointRecipientPolicy.Accepts(recipient, TargetProcess, TargetWindow, allowOtherWindow: false)
            .Should().BeTrue();
    }

    [Fact]
    public void A_hosted_surface_in_another_target_window_needs_allow_other_window()
    {
        PointRecipient recipient = new(ChildWindow, WebViewProcess, SecondTargetWindow, TargetOwner);

        PointRecipientPolicy.Accepts(recipient, TargetProcess, TargetWindow, allowOtherWindow: false)
            .Should().BeFalse();
        PointRecipientPolicy.Accepts(recipient, TargetProcess, TargetWindow, allowOtherWindow: true)
            .Should().BeTrue();
    }

    [Fact]
    public void Another_target_window_on_top_needs_allow_other_window()
    {
        PointRecipient recipient = new(SecondTargetWindow, TargetOwner, SecondTargetWindow, TargetOwner);

        PointRecipientPolicy.Accepts(recipient, TargetProcess, TargetWindow, allowOtherWindow: false)
            .Should().BeFalse();
        PointRecipientPolicy.Accepts(recipient, TargetProcess, TargetWindow, allowOtherWindow: true)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_top_level_window_of_another_process_is_occlusion(bool allowOtherWindow)
    {
        PointRecipient recipient = new(OtherWindow, OtherProcess, OtherWindow, OtherProcess);

        PointRecipientPolicy.Accepts(recipient, TargetProcess, TargetWindow, allowOtherWindow)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_foreign_child_hosted_by_another_process_is_occlusion(bool allowOtherWindow)
    {
        // A WebView2 surface inside some other application's window on top of the target.
        PointRecipient recipient = new(ChildWindow, WebViewProcess, OtherWindow, OtherProcess);

        PointRecipientPolicy.Accepts(recipient, TargetProcess, TargetWindow, allowOtherWindow)
            .Should().BeFalse();
    }

    [Fact]
    public void A_foreign_child_whose_root_cannot_be_read_is_refused()
    {
        PointRecipient recipient = new(ChildWindow, WebViewProcess, 0, 0);

        PointRecipientPolicy.Accepts(recipient, TargetProcess, TargetWindow, allowOtherWindow: true)
            .Should().BeFalse();
    }

    [Fact]
    public void A_foreign_child_whose_root_owner_cannot_be_read_is_refused()
    {
        PointRecipient recipient = new(ChildWindow, WebViewProcess, TargetWindow, 0);

        PointRecipientPolicy.Accepts(recipient, TargetProcess, TargetWindow, allowOtherWindow: false)
            .Should().BeFalse();
    }

    [Fact]
    public void No_window_under_the_point_is_refused()
    {
        PointRecipientPolicy.Accepts(default, TargetProcess, TargetWindow, allowOtherWindow: true)
            .Should().BeFalse();
    }

    [Fact]
    public void A_recipient_whose_owner_cannot_be_read_is_refused()
    {
        PointRecipient recipient = new(TargetWindow, 0, TargetWindow, TargetOwner);

        PointRecipientPolicy.Accepts(recipient, TargetProcess, TargetWindow, allowOtherWindow: false)
            .Should().BeFalse();
    }
}
