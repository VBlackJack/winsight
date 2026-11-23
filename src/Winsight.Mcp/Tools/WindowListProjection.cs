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

using Winsight.Core.Models;

namespace Winsight.Mcp.Tools;

/// <summary>
/// One window as list_windows reports it. Mirrors <see cref="WindowDescriptor"/>;
/// <see cref="Title"/> is empty and <see cref="TitleWithheld"/> is true when the
/// window belongs to a process other than the session target and the caller did
/// not opt in with includeTitles: the title of someone else's document, chat
/// channel or browser tab does not belong in an agent's log.
/// </summary>
public sealed record ListedWindow(
    int ProcessId,
    long WindowHandle,
    string Title,
    string ProcessName,
    bool IsForeground,
    int X,
    int Y,
    int Width,
    int Height,
    string Kind,
    string ClassName,
    long OwnerHandle,
    bool IsModal,
    bool TitleWithheld);

/// <summary>Pure title-withholding projection applied by the list_windows tool layer.</summary>
public static class WindowListProjection
{
    /// <summary>
    /// Project discovered windows for the wire. A title is kept when
    /// <paramref name="includeTitles"/> is true or the window belongs to
    /// <paramref name="targetProcessId"/>; otherwise it is blanked and flagged.
    /// Every other field is copied unchanged.
    /// </summary>
    public static IReadOnlyList<ListedWindow> Project(
        IEnumerable<WindowDescriptor> windows,
        int? targetProcessId,
        bool includeTitles)
    {
        ArgumentNullException.ThrowIfNull(windows);
        return windows
            .Select(window =>
            {
                bool keep = includeTitles ||
                    (targetProcessId is int target && window.ProcessId == target);
                return new ListedWindow(
                    window.ProcessId,
                    window.WindowHandle,
                    keep ? window.Title : string.Empty,
                    window.ProcessName,
                    window.IsForeground,
                    window.X,
                    window.Y,
                    window.Width,
                    window.Height,
                    window.Kind,
                    window.ClassName,
                    window.OwnerHandle,
                    window.IsModal,
                    TitleWithheld: !keep);
            })
            .ToList();
    }
}
