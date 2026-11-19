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

/// <summary>Stable names of synthetic pointer movement methods, in dispatch/fallback order.</summary>
public static class PointerMoveMethods
{
    public const string SendInputVirtualDesk = "send_input_virtual_desk";
    public const string SendInputPrimaryAbsolute = "send_input_primary_absolute";
    public const string SendInputRelative = "send_input_relative";
    public const string SetCursorPos = "set_cursor_pos";

    public static readonly IReadOnlyList<string> FallbackOrder =
    [
        SendInputPrimaryAbsolute,
        SendInputRelative,
        SetCursorPos,
    ];

    public static readonly IReadOnlyList<string> AllOrdered =
    [
        SendInputVirtualDesk,
        SendInputPrimaryAbsolute,
        SendInputRelative,
        SetCursorPos,
    ];
}

/// <summary>Rectangle returned by GetClipCursor.</summary>
public sealed record PointerClipRect(int Left, int Top, int Right, int Bottom, int Width, int Height);

/// <summary>Cursor visibility and suppression state from GetCursorInfo.</summary>
public sealed record CursorStateInfo(
    uint Flags,
    bool Showing,
    bool Suppressed,
    int ScreenX,
    int ScreenY);

/// <summary>Windows pointer ballistics and speed from SPI_GETMOUSE and SPI_GETMOUSESPEED.</summary>
public sealed record MouseAccelerationInfo(
    int Threshold1,
    int Threshold2,
    int EnhancePointerPrecision,
    int MouseSpeed);

/// <summary>Foreground window handle and owning process.</summary>
public sealed record ForegroundProcessInfo(
    long Handle,
    int ProcessId,
    string ProcessName);

/// <summary>Measured outcome of one pointer move method in <c>diagnose_pointer</c>.</summary>
public sealed record PointerMethodAttempt(
    string Method,
    uint ApiResult,
    int LastWin32Error,
    CursorPoint? CursorImmediate,
    CursorPoint? PhysicalCursorImmediate,
    CursorPoint? CursorAfter50Ms,
    CursorPoint? PhysicalCursorAfter50Ms,
    CursorPoint? CursorAfter150Ms,
    CursorPoint? PhysicalCursorAfter150Ms,
    bool ReachedTarget,
    bool Restored);

/// <summary>Full read-only diagnostic report returned by <c>diagnose_pointer</c>.</summary>
public sealed record PointerDiagnosticReport(
    int TargetX,
    int TargetY,
    CursorPoint? StartCursorPos,
    CursorPoint? StartPhysicalCursorPos,
    PointerClipRect? ClipCursor,
    CursorStateInfo? CursorInfo,
    IReadOnlyList<string> MouseButtonsDown,
    MouseAccelerationInfo? MouseAcceleration,
    ForegroundProcessInfo? ForegroundWindow,
    WindowBounds VirtualDesktop,
    WindowBounds PrimaryMonitor,
    string DpiAwareness,
    InputContextSnapshot InputContext,
    IReadOnlyList<PointerMethodAttempt> Attempts,
    string? FirstWorkingMethod);
