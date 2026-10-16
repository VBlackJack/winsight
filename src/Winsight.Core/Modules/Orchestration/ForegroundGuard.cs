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

using System.Globalization;
using System.Runtime.InteropServices;
using Winsight.Core.Modules.UserActivity;

namespace Winsight.Core.Modules.Orchestration;

/// <summary>The foreground window at one instant; a zero handle means Windows reported none.</summary>
internal readonly record struct ForegroundSample(long Handle, int ProcessId);

/// <summary>Reads the foreground window; replaced by a fixed value in tests.</summary>
internal interface IForegroundReader
{
    ForegroundSample Read();

    bool Exists(long handle);
}

internal sealed class NativeForegroundReader : IForegroundReader
{
    public static readonly NativeForegroundReader Instance = new();

    public ForegroundSample Read()
    {
        IntPtr handle = GetForegroundWindow();
        if (handle == IntPtr.Zero || GetWindowThreadProcessId(handle, out uint owner) == 0)
        {
            return default;
        }
        return new ForegroundSample(handle.ToInt64(), (int)owner);
    }

    public bool Exists(long handle) => IsWindow(new IntPtr(handle));

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);
}

/// <summary>
/// Stops a batch when the foreground moves under it to a window the batch did
/// not cause: on a shared desktop that is a person taking the screen back.
/// The baseline is the foreground after the last screen-changing step, so
/// windows the steps raise or open themselves are never flagged. Tolerated:
/// no foreground now or at the baseline (a transition), a window of a process
/// the batch targets (a dialog the application opened late), and the loss of
/// the baseline window itself (the application closed it, and Windows picked
/// another).
/// </summary>
internal static class ForegroundGuard
{
    public const string ForegroundChangedCode = "foreground_changed";

    public static bool Accepts(
        ForegroundSample baseline,
        ForegroundSample current,
        bool baselineStillExists,
        IReadOnlySet<int> batchProcessIds) =>
        current.Handle == 0 ||
        current.Handle == baseline.Handle ||
        batchProcessIds.Contains(current.ProcessId) ||
        baseline.Handle == 0 ||
        !baselineStillExists;

    public static string Describe(ForegroundSample baseline, ForegroundSample current) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{ForegroundChangedCode}: the foreground moved from window 0x{baseline.Handle:x} " +
            $"({Process(baseline.ProcessId)}) to window 0x{current.Handle:x} ({Process(current.ProcessId)}) " +
            $"between steps, outside the batch. Someone may be using this desktop: the batch stopped before " +
            $"touching the screen again. Check get_user_activity, observe, then rerun the remaining steps.");

    private static string Process(int processId) =>
        processId == 0
            ? "no process"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"process {processId} {UserActivityMonitor.ProcessName(processId) ?? "exited"}");
}
