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

namespace Winsight.Core.Models;

/// <summary>
/// The element that owns keyboard focus, as UI Automation and Win32 report it.
/// <see cref="IsWebView2"/> is true when keystrokes would land inside a
/// WebView2 (Chromium) surface; <see cref="WebView2Evidence"/> names the
/// signals that decided it. <see cref="BelongsToTarget"/> is true when the
/// focus sits in the target process or in a window it hosts (a WebView2
/// renders in its own msedgewebview2 process, parented into the host window).
/// </summary>
public sealed record FocusedElementInfo(
    string ControlType,
    string AutomationId,
    string Name,
    string ClassName,
    string FrameworkId,
    int ProcessId,
    string ProcessName,
    bool BelongsToTarget,
    bool IsWebView2,
    IReadOnlyList<string> WebView2Evidence,
    string Win32FocusClassName,
    long TopLevelWindowHandle,
    string TopLevelWindowTitle,
    int X,
    int Y,
    int Width,
    int Height);
