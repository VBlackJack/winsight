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

using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

public sealed class WindowDiscovery
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public IReadOnlyList<WindowDescriptor> List()
    {
        var foregroundHandle = GetForegroundWindow().ToInt64();
        using var automation = new UIA3Automation();
        var desktop = automation.GetDesktop();
        var topLevel = desktop.FindAllChildren();
        var result = new List<WindowDescriptor>(topLevel.Length);
        foreach (var element in topLevel)
        {
            var descriptor = TryBuildDescriptor(element, foregroundHandle);
            if (descriptor is not null)
            {
                result.Add(descriptor);
            }
        }
        return result;
    }

    private static WindowDescriptor? TryBuildDescriptor(AutomationElement element, long foregroundHandle)
    {
        try
        {
            var title = element.Properties.Name.ValueOrDefault ?? string.Empty;
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }
            var hwnd = element.Properties.NativeWindowHandle.ValueOrDefault.ToInt64();
            var processId = element.Properties.ProcessId.ValueOrDefault;
            var rect = element.BoundingRectangle;
            return new WindowDescriptor(
                ProcessId: processId,
                WindowHandle: hwnd,
                Title: title,
                ProcessName: ResolveProcessName(processId),
                IsForeground: hwnd != 0 && hwnd == foregroundHandle,
                X: rect.X,
                Y: rect.Y,
                Width: rect.Width,
                Height: rect.Height);
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string ResolveProcessName(int processId)
    {
        if (processId <= 0)
        {
            return string.Empty;
        }
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return string.Empty;
        }
    }
}
