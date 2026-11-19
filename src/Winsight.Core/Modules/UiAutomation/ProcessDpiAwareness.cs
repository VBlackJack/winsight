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

using System.Runtime.InteropServices;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Enables Per-Monitor V2 DPI awareness for the hosting process so Win32
/// coordinates (GetCursorPos, GetWindowRect, GetSystemMetrics, SendInput) are
/// physical pixels rather than DPI-virtualized coordinates.
/// </summary>
public static class ProcessDpiAwareness
{
    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);
    private const int ProcessPerMonitorDpiAware = 2;
    private const int DpiAwarenessInvalid = -1;
    private const int DpiAwarenessUnaware = 0;
    private const int DpiAwarenessSystemAware = 1;
    private const int DpiAwarenessPerMonitorAware = 2;

    /// <summary>
    /// Request Per-Monitor V2 DPI awareness, falling back to Per-Monitor V1.
    /// Safe to call multiple times or when already configured by a manifest.
    /// </summary>
    public static bool TryEnablePerMonitorV2()
    {
        try
        {
            if (NativeMethods.SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2))
            {
                return true;
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Fall through to shcore.dll on older Windows builds.
        }

        try
        {
            return NativeMethods.SetProcessDpiAwareness(ProcessPerMonitorDpiAware) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Return a stable label describing the current thread's DPI awareness.</summary>
    public static string DescribeCurrent()
    {
        try
        {
            IntPtr context = NativeMethods.GetThreadDpiAwarenessContext();
            if (NativeMethods.AreDpiAwarenessContextsEqual(context, DpiAwarenessContextPerMonitorAwareV2))
            {
                return "per_monitor_aware_v2";
            }

            return NativeMethods.GetAwarenessFromDpiAwarenessContext(context) switch
            {
                DpiAwarenessUnaware => "unaware",
                DpiAwarenessSystemAware => "system_aware",
                DpiAwarenessPerMonitorAware => "per_monitor_aware",
                DpiAwarenessInvalid => "invalid",
                _ => "unknown",
            };
        }
        catch (EntryPointNotFoundException)
        {
            return "unknown";
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        public static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        public static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AreDpiAwarenessContextsEqual(IntPtr contextA, IntPtr contextB);
    }
}
