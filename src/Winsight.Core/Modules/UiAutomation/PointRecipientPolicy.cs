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
/// Win32 facts about the window that would receive physical input at a point
/// (<c>WindowFromPoint</c>) and its root ancestor (<c>GetAncestor(GA_ROOT)</c>).
/// A zero handle or process id means the fact could not be read.
/// </summary>
/// <param name="Recipient">Window under the point.</param>
/// <param name="RecipientProcessId">Process that owns the window under the point.</param>
/// <param name="Root">Top-level window the recipient is parented into.</param>
/// <param name="RootProcessId">Process that owns that top-level window.</param>
public readonly record struct PointRecipient(long Recipient, uint RecipientProcessId, long Root, uint RootProcessId);

/// <summary>
/// Decides whether physical input at a point reaches the intended target.
/// The recipient is accepted when the target process owns it, or when it is a
/// child window of another process (a WebView2 <c>Chrome_WidgetWin_1</c> surface
/// owned by msedgewebview2.exe) parented into a top-level window of the target
/// process. Any other top-level window or process on top of the point is
/// occlusion and is refused.
/// </summary>
public static class PointRecipientPolicy
{
    /// <summary>True when input at the point reaches the target process and window.</summary>
    /// <param name="recipient">Facts about the window under the point.</param>
    /// <param name="targetProcessId">Process the input is meant for.</param>
    /// <param name="targetWindow">Top-level window the input is meant for.</param>
    /// <param name="allowOtherWindow">Accept any top-level window of the target process.</param>
    public static bool Accepts(PointRecipient recipient, int targetProcessId, long targetWindow, bool allowOtherWindow)
    {
        if (recipient.Recipient == 0 || recipient.RecipientProcessId == 0 || targetProcessId <= 0)
        {
            return false;
        }
        uint target = (uint)targetProcessId;
        bool inTargetWindow = recipient.Root != 0 && recipient.Root == targetWindow;
        if (recipient.RecipientProcessId == target)
        {
            return allowOtherWindow || inTargetWindow;
        }
        // A foreign-process recipient is only a hosted surface when its root is a target window.
        return recipient.Root != 0 && recipient.RootProcessId == target && (allowOtherWindow || inTargetWindow);
    }
}
