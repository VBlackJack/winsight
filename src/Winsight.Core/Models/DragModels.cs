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
/// One end of a drag: either a located element (its center, shifted by the
/// offset) or a point. A point is window-relative or screen-space depending
/// on the drag's coordinate space.
/// </summary>
public sealed record DragEndpoint(
    ElementLocator? Locator = null,
    int? X = null,
    int? Y = null,
    int OffsetX = 0,
    int OffsetY = 0)
{
    public static DragEndpoint ForElement(ElementLocator locator, int offsetX = 0, int offsetY = 0) =>
        new(locator, null, null, offsetX, offsetY);

    public static DragEndpoint ForPoint(int x, int y) => new(null, x, y);

    /// <summary>True when the endpoint names a point rather than an element.</summary>
    public bool IsPoint => Locator is null;

    /// <summary>Null when the endpoint is well formed, otherwise why it is not.</summary>
    public string? Validate(string role)
    {
        bool hasLocator = Locator is not null && !Locator.IsEmpty;
        bool hasPoint = X is not null && Y is not null;
        if (hasLocator == hasPoint)
        {
            return $"The drag {role} needs exactly one of an element locator or both {role}X and {role}Y.";
        }
        if (Locator is not null && Locator.IsEmpty)
        {
            return $"The drag {role} locator is empty.";
        }
        return null;
    }
}

/// <summary>
/// Pacing of a drag. OLE drag and drop (WPF DoDragDrop) starts only after the
/// button has been held and the pointer has left the system drag rectangle,
/// and a drop target reacts only to moves it receives while hovered; each
/// pause gives the target application's message loop time to keep up.
/// </summary>
public sealed record DragOptions(
    int HoldMs = DragOptions.DefaultHoldMs,
    int Steps = DragOptions.DefaultSteps,
    int StepDelayMs = DragOptions.DefaultStepDelayMs,
    int DropDelayMs = DragOptions.DefaultDropDelayMs)
{
    public const int DefaultHoldMs = 150;
    public const int DefaultSteps = 12;
    public const int DefaultStepDelayMs = 25;
    public const int DefaultDropDelayMs = 250;
    public const int MaxHoldMs = 5000;
    public const int MinSteps = 1;
    public const int MaxSteps = 100;
    public const int MaxStepDelayMs = 1000;
    public const int MaxDropDelayMs = 5000;

    /// <summary>Null when every value is in range, otherwise the first violation.</summary>
    public string? Validate()
    {
        if (HoldMs is < 0 or > MaxHoldMs)
        {
            return $"holdMs must be between 0 and {MaxHoldMs}.";
        }
        if (Steps is < MinSteps or > MaxSteps)
        {
            return $"steps must be between {MinSteps} and {MaxSteps}.";
        }
        if (StepDelayMs is < 0 or > MaxStepDelayMs)
        {
            return $"stepDelayMs must be between 0 and {MaxStepDelayMs}.";
        }
        if (DropDelayMs is < 0 or > MaxDropDelayMs)
        {
            return $"dropDelayMs must be between 0 and {MaxDropDelayMs}.";
        }
        return null;
    }
}

/// <summary>Screen-space path a drag followed.</summary>
public sealed record DragResult(
    int FromX,
    int FromY,
    int ToX,
    int ToY,
    int Moves);
