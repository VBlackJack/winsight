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

public class WebView2FocusClassifierTests
{
    private static readonly string[] WpfAncestors = { "TextBox", "Grid", "Window" };

    [Fact]
    public void A_wpf_text_box_is_not_webview2()
    {
        WebView2FocusClassifier.Evidence(
                "WPF", "Heimdall", WpfAncestors, "HwndWrapper[Heimdall;;abc]")
            .Should().BeEmpty();
    }

    [Fact]
    public void Chromium_framework_id_is_webview2()
    {
        WebView2FocusClassifier.Evidence("Chrome", "Heimdall", Array.Empty<string>(), string.Empty)
            .Should().ContainSingle().Which.Should().Be("frameworkId=Chrome");
    }

    [Fact]
    public void Focus_owned_by_the_webview2_process_is_webview2()
    {
        WebView2FocusClassifier.Evidence(string.Empty, "msedgewebview2", Array.Empty<string>(), string.Empty)
            .Should().Contain("process=msedgewebview2");
    }

    [Fact]
    public void Chromium_render_widget_holding_win32_focus_is_webview2()
    {
        WebView2FocusClassifier.Evidence("WPF", "Heimdall", Array.Empty<string>(), "Chrome_RenderWidgetHostHWND")
            .Should().Contain("win32Focus=Chrome_RenderWidgetHostHWND");
    }

    [Theory]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("WebView2")]
    public void A_webview2_ancestor_is_webview2(string ancestorClass)
    {
        WebView2FocusClassifier.Evidence("WPF", "Heimdall", new[] { "Document", ancestorClass, "Window" }, string.Empty)
            .Should().ContainSingle().Which.Should().Be($"uiaClass={ancestorClass}");
    }
}
