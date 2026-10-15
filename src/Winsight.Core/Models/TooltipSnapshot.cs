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
/// Compact UIA snapshot of a tooltip observed after hover.
/// <see cref="Source"/> tells where the text came from:
/// <see cref="SourcePopup"/> when a tooltip window opened (its bounds and
/// <see cref="WindowHandle"/> are the popup's), or <see cref="SourceHelpText"/>
/// when no popup appeared and the element's AutomationProperties.HelpText was
/// used instead (its bounds are then the element's, and the handle is zero).
/// </summary>
public sealed record TooltipSnapshot(
    string Text,
    int X,
    int Y,
    int Width,
    int Height,
    string Source = TooltipSnapshot.SourcePopup,
    long WindowHandle = 0)
{
    public const string SourcePopup = "popup";
    public const string SourceHelpText = "helpText";
}
