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

public sealed class WindowClassifierTests
{
    [Theory]
    // Win32 standard classes win regardless of style/ownership.
    [InlineData("#32770", false, false, false, "dialog")]   // MessageBox / common dialog
    [InlineData("#32768", false, true, false, "menu")]      // native menu
    [InlineData("tooltips_class32", false, false, false, "tooltip")]
    // Owned captioned window is a dialog (e.g. a WPF modal Window).
    [InlineData("HwndWrapper", true, false, true, "dialog")]
    // Captionless popup-styled window is an overlay (dropdown, flyout, context menu).
    [InlineData("Popup", false, true, true, "popup")]
    // Unowned captioned window is a main window.
    [InlineData("HwndWrapper", true, false, false, "main")]
    // Nothing recognizable.
    [InlineData("SomeClass", false, false, false, "other")]
    public void ClassifyKind_MapsStyleAndOwnershipToKind(
        string className, bool hasCaption, bool isPopupStyle, bool hasOwner, string expectedKind)
    {
        WindowClassifier.ClassifyKind(className, hasCaption, isPopupStyle, hasOwner)
            .Should().Be(expectedKind);
    }

    // Extended styles measured on a WPF ToolTip popup (0x080800A8) and on a
    // WPF main window (0x00040100).
    private const long WpfTooltipExStyle = 0x080800A8L;
    private const long WpfMainExStyle = 0x00040100L;

    [Theory]
    [InlineData("tooltip", 0L, false, true)]
    [InlineData("menu", 0L, false, true)]
    [InlineData("popup", WpfTooltipExStyle, true, true)]      // WPF ToolTip popup
    [InlineData("popup", 0L, true, true)]                      // owned dropdown or flyout
    [InlineData("popup", WindowClassifier.ExtendedStyleToolWindow, false, true)]
    [InlineData("other", WindowClassifier.ExtendedStyleNoActivate, false, true)]
    [InlineData("main", WpfMainExStyle, false, false)]
    [InlineData("dialog", 0L, true, false)]                    // modal dialog stays a primary target
    [InlineData("popup", 0L, false, false)]                    // borderless unowned main window
    public void IsTransient_flags_overlays_but_not_main_windows_or_dialogs(
        string kind, long extendedStyle, bool hasOwner, bool expected)
    {
        WindowClassifier.IsTransient(kind, extendedStyle, hasOwner).Should().Be(expected);
    }

    [Fact]
    public void IsModal_TrueOnlyWhenOwnedAndOwnerDisabled()
    {
        WindowClassifier.IsModal(hasOwner: true, ownerEnabled: false).Should().BeTrue();
        WindowClassifier.IsModal(hasOwner: true, ownerEnabled: true).Should().BeFalse();
        WindowClassifier.IsModal(hasOwner: false, ownerEnabled: true).Should().BeFalse();
    }
}
