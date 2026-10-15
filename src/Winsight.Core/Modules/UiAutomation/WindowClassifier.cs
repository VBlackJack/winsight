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

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Classifies a top-level window into a coarse kind an agent can reason about
/// — main window, dialog (incl. MessageBox), popup/overlay, menu, or tooltip —
/// from its Win32 class name, window styles, and ownership. The goal is to make
/// transient surfaces (modal dialogs, dropdown menus, flyouts) discoverable, not
/// merely reachable: an agent can list a process's windows and immediately tell
/// which one is the dialog to act on.
/// </summary>
public static class WindowClassifier
{
    /// <summary>Win32 standard dialog box class (used by MessageBox and common dialogs).</summary>
    private const string DialogClass = "#32770";

    /// <summary>Win32 standard menu class (native dropdown/context menus).</summary>
    private const string MenuClass = "#32768";

    /// <summary>Win32 standard tooltip class.</summary>
    private const string TooltipClass = "tooltips_class32";

    public const string KindMain = "main";
    public const string KindDialog = "dialog";
    public const string KindPopup = "popup";
    public const string KindMenu = "menu";
    public const string KindTooltip = "tooltip";
    public const string KindOther = "other";

    /// <summary>
    /// Classify a window from its class name and style/ownership flags.
    /// Known Win32 classes win first; otherwise an owned captioned window is a
    /// dialog (e.g. a WPF modal Window), a captionless popup-styled window is an
    /// overlay (dropdown, flyout, context menu), and an unowned captioned window
    /// is a main window.
    /// </summary>
    public static string ClassifyKind(string className, bool hasCaption, bool isPopupStyle, bool hasOwner)
    {
        if (string.Equals(className, TooltipClass, StringComparison.OrdinalIgnoreCase))
        {
            return KindTooltip;
        }
        if (string.Equals(className, MenuClass, StringComparison.Ordinal))
        {
            return KindMenu;
        }
        if (string.Equals(className, DialogClass, StringComparison.Ordinal))
        {
            return KindDialog;
        }
        if (hasOwner && hasCaption)
        {
            return KindDialog;
        }
        if (isPopupStyle && !hasCaption)
        {
            return KindPopup;
        }
        if (!hasOwner && hasCaption)
        {
            return KindMain;
        }
        return KindOther;
    }

    /// <summary>WS_EX_TOOLWINDOW: kept off the taskbar and Alt+Tab.</summary>
    public const long ExtendedStyleToolWindow = 0x00000080L;

    /// <summary>WS_EX_NOACTIVATE: never becomes the foreground window when clicked.</summary>
    public const long ExtendedStyleNoActivate = 0x08000000L;

    /// <summary>
    /// Whether a window is a transient overlay (tooltip, menu, dropdown, flyout)
    /// rather than a surface an agent means when it names no window. Such a
    /// window sits at the top of the z-order while it is open, so taking "the
    /// topmost window" would inspect, capture or focus it instead of the main
    /// window or dialog. A WPF ToolTip, for one, is an owned captionless popup
    /// with WS_EX_NOACTIVATE and WS_EX_TOOLWINDOW.
    /// </summary>
    public static bool IsTransient(string kind, long extendedStyle, bool hasOwner)
    {
        if (kind is KindTooltip or KindMenu)
        {
            return true;
        }
        if ((extendedStyle & ExtendedStyleNoActivate) != 0)
        {
            return true;
        }
        return kind == KindPopup && (hasOwner || (extendedStyle & ExtendedStyleToolWindow) != 0);
    }

    /// <summary>
    /// Whether a window is application-modal: it has an owner and that owner is
    /// disabled, which is how the platform blocks interaction with the parent
    /// while a modal dialog is up.
    /// </summary>
    public static bool IsModal(bool hasOwner, bool ownerEnabled) => hasOwner && !ownerEnabled;
}
