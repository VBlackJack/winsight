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
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Reports where keyboard input would land right now: the UIA focused
/// element, the Win32 focus window of the foreground thread, and whether
/// either sits inside a WebView2 surface.
/// </summary>
public sealed class FocusedElementReader
{
    private const int MaxAncestorDepth = 32;
    private const int MaxClassNameLength = 256;
    private const int MaxTitleLength = 512;
    private const uint RootAncestor = 2;

    /// <summary>Describe the focused element relative to the target process.</summary>
    public OperationResult Read(int processId)
    {
        using UIA3Automation automation = new();
        FocusedElementInfo? info = ReadFocus(automation, processId);
        return info is null
            ? OperationResult.Failure("No element owns keyboard focus (the desktop or a secure surface may be focused).")
            : OperationResult.OkWith(info);
    }

    /// <summary>The focused element, or null when UIA reports none.</summary>
    internal static FocusedElementInfo? ReadFocus(UIA3Automation automation, int targetProcessId)
    {
        AutomationElement? focused;
        try
        {
            focused = automation.FocusedElement();
        }
        catch (COMException)
        {
            focused = null;
        }
        string win32FocusClass = ReadClassName(ReadWin32Focus());
        return focused is null ? null : Describe(automation, focused, targetProcessId, win32FocusClass);
    }

    /// <summary>
    /// WebView2 evidence for an element about to receive keyboard input. The
    /// Win32 focus class is left out: the element is focused only afterwards.
    /// </summary>
    internal static IReadOnlyList<string> WebView2EvidenceFor(UIA3Automation automation, AutomationElement element) =>
        Describe(automation, element, targetProcessId: 0, win32FocusClass: string.Empty).WebView2Evidence;

    private static FocusedElementInfo Describe(
        UIA3Automation automation,
        AutomationElement element,
        int targetProcessId,
        string win32FocusClass)
    {
        int elementProcessId = SafeRead(() => element.Properties.ProcessId.ValueOrDefault, 0);
        string processName = ResolveProcessName(elementProcessId);
        string frameworkId = SafeRead(() => element.Properties.FrameworkId.ValueOrDefault ?? string.Empty, string.Empty);
        (List<string> classNames, IntPtr nativeHandle) = WalkAncestors(automation, element);
        IntPtr topLevel = nativeHandle == IntPtr.Zero ? IntPtr.Zero : GetAncestor(nativeHandle, RootAncestor);
        uint topLevelProcessId = 0;
        if (topLevel != IntPtr.Zero)
        {
            _ = GetWindowThreadProcessId(topLevel, out topLevelProcessId);
        }
        System.Drawing.Rectangle rect = SafeRead(() => element.BoundingRectangle, System.Drawing.Rectangle.Empty);
        IReadOnlyList<string> evidence = WebView2FocusClassifier.Evidence(
            frameworkId, processName, classNames, win32FocusClass);

        bool belongsToTarget = BelongsToTarget(targetProcessId, elementProcessId, topLevelProcessId);
        return new FocusedElementInfo(
            ControlType: SafeRead(() => element.Properties.ControlType.ValueOrDefault.ToString(), string.Empty),
            AutomationId: SafeRead(() => element.Properties.AutomationId.ValueOrDefault ?? string.Empty, string.Empty),
            // A focused browser tab or chat item exposes the page title or message text as its
            // Name: it is withheld, like the window title, when the focus is outside the target.
            Name: belongsToTarget
                ? SafeRead(() => element.Properties.Name.ValueOrDefault ?? string.Empty, string.Empty)
                : string.Empty,
            ClassName: classNames.Count > 0 ? classNames[0] : string.Empty,
            FrameworkId: frameworkId,
            ProcessId: elementProcessId,
            ProcessName: processName,
            BelongsToTarget: belongsToTarget,
            IsWebView2: evidence.Count > 0,
            WebView2Evidence: evidence,
            Win32FocusClassName: win32FocusClass,
            TopLevelWindowHandle: topLevel.ToInt64(),
            TopLevelWindowTitle: belongsToTarget ? ReadTitle(topLevel) : string.Empty,
            X: rect.X,
            Y: rect.Y,
            Width: rect.Width,
            Height: rect.Height,
            TitleWithheld: !belongsToTarget);
    }

    /// <summary>
    /// True when the focus sits in the target process or in a top-level window it
    /// hosts. A zero target id (no target known) never matches.
    /// </summary>
    internal static bool BelongsToTarget(int targetProcessId, int elementProcessId, uint topLevelProcessId) =>
        targetProcessId != 0 &&
        (elementProcessId == targetProcessId || topLevelProcessId == (uint)targetProcessId);

    // ClassName of the element and its ancestors (nearest first), plus the
    // nearest native window handle so the top-level host can be resolved.
    private static (List<string> ClassNames, IntPtr NativeHandle) WalkAncestors(
        UIA3Automation automation,
        AutomationElement element)
    {
        List<string> classNames = new();
        IntPtr nativeHandle = IntPtr.Zero;
        ITreeWalker walker = automation.TreeWalkerFactory.GetRawViewWalker();
        AutomationElement? current = element;
        for (int depth = 0; current is not null && depth < MaxAncestorDepth; depth++)
        {
            AutomationElement node = current;
            classNames.Add(SafeRead(() => node.Properties.ClassName.ValueOrDefault ?? string.Empty, string.Empty));
            if (nativeHandle == IntPtr.Zero)
            {
                nativeHandle = SafeRead(() => node.Properties.NativeWindowHandle.ValueOrDefault, IntPtr.Zero);
            }
            current = SafeRead<AutomationElement?>(() => walker.GetParent(node), null);
        }
        return (classNames, nativeHandle);
    }

    private static IntPtr ReadWin32Focus()
    {
        GuiThreadInfo info = new() { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(0, ref info) ? info.Focus : IntPtr.Zero;
    }

    private static string ReadClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }
        char[] buffer = new char[MaxClassNameLength];
        int length = GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static string ReadTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }
        char[] buffer = new char[MaxTitleLength];
        int length = GetWindowText(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, length);
    }

    private static string ResolveProcessName(int processId)
    {
        if (processId <= 0)
        {
            return string.Empty;
        }
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static T SafeRead<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is COMException or FlaUI.Core.Exceptions.PropertyNotSupportedException)
        {
            return fallback;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public NativeRect CaretRect;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo lpgui);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, [Out] char[] text, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
