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
using Winsight.Core.Modules.Capture;
using Xunit;

namespace Winsight.Core.Tests;

public class ScreenCapturePolicyTests
{
    private static OcclusionWindow Window(
        long handle,
        int pid,
        int left,
        int top,
        int width,
        int height,
        string title = "App",
        bool visible = true,
        bool minimized = false,
        bool cloaked = false,
        bool zeroAlpha = false) =>
        new(handle, pid, title, new WindowBounds(left, top, width, height), visible, minimized, cloaked, zeroAlpha);

    [Theory]
    [InlineData(null, CaptureSource.Window)]
    [InlineData("", CaptureSource.Window)]
    [InlineData("window", CaptureSource.Window)]
    [InlineData("WINDOW", CaptureSource.Window)]
    [InlineData("screen", CaptureSource.Screen)]
    [InlineData(" Screen ", CaptureSource.Screen)]
    public void CaptureSourceParser_parses_valid_values(string? raw, CaptureSource expected)
    {
        CaptureSourceParser.TryParse(raw, out CaptureSource parsed, out string? error).Should().BeTrue();
        parsed.Should().Be(expected);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("desktop")]
    [InlineData("hwnd")]
    [InlineData("monitor")]
    public void CaptureSourceParser_rejects_unknown_values(string raw)
    {
        CaptureSourceParser.TryParse(raw, out _, out string? error).Should().BeFalse();
        error.Should().Contain("window").And.Contain("screen");
    }

    [Fact]
    public void Evaluate_allows_visible_unoccluded_target()
    {
        OcclusionWindow target = Window(0x100, 10, 100, 100, 800, 600, "Target");
        OcclusionWindow sideWindow = Window(0x200, 20, 1000, 100, 400, 400, "Side");

        OcclusionCheckResult result = ScreenCapturePolicy.Evaluate(target, [sideWindow], allowOccluded: false);

        result.Allowed.Should().BeTrue();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void Evaluate_allows_same_process_popup_above_target()
    {
        OcclusionWindow target = Window(0x100, 10, 100, 100, 800, 600, "Target");
        OcclusionWindow menuPopup = Window(0x101, 10, 200, 200, 220, 300, "ContextMenu");

        OcclusionCheckResult result = ScreenCapturePolicy.Evaluate(target, [menuPopup], allowOccluded: false);

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_refuses_other_process_occluding_window()
    {
        OcclusionWindow target = Window(0x100, 10, 100, 100, 800, 600, "Target");
        OcclusionWindow otherApp = Window(0x200, 20, 300, 200, 500, 400, "OtherApp");

        OcclusionCheckResult result = ScreenCapturePolicy.Evaluate(target, [otherApp], allowOccluded: false);

        result.Allowed.Should().BeFalse();
        result.Occluder.Should().Be(otherApp);
        result.Reason.Should().Contain("window 0x200").And.Contain("of process 20")
            .And.Contain("allowOccluded=true").And.NotContain("OtherApp");
    }

    [Fact]
    public void Evaluate_allows_other_process_occluder_when_allowOccluded_is_true()
    {
        OcclusionWindow target = Window(0x100, 10, 100, 100, 800, 600, "Target");
        OcclusionWindow otherApp = Window(0x200, 20, 300, 200, 500, 400, "OtherApp");

        OcclusionCheckResult result = ScreenCapturePolicy.Evaluate(target, [otherApp], allowOccluded: true);

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ignores_cloaked_minimized_invisible_and_zero_alpha_other_windows()
    {
        OcclusionWindow target = Window(0x100, 10, 100, 100, 800, 600, "Target");
        OcclusionWindow[] ghosts =
        [
            Window(0x201, 20, 100, 100, 800, 600, "Hidden", visible: false),
            Window(0x202, 20, 100, 100, 800, 600, "Minimized", minimized: true),
            Window(0x203, 20, 100, 100, 800, 600, "Cloaked", cloaked: true),
            Window(0x204, 20, 100, 100, 800, 600, "Transparent", zeroAlpha: true),
        ];

        OcclusionCheckResult result = ScreenCapturePolicy.Evaluate(target, ghosts, allowOccluded: false);

        result.Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void Evaluate_refuses_not_visible_minimized_or_cloaked_target_even_when_allowOccluded(
        bool visible,
        bool minimized,
        bool cloaked)
    {
        OcclusionWindow target = Window(0x100, 10, 100, 100, 800, 600, "Target", visible, minimized, cloaked);

        OcclusionCheckResult result = ScreenCapturePolicy.Evaluate(target, [], allowOccluded: true);

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Contain("captureSource=\"screen\"");
    }
}
