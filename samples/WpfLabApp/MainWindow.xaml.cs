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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Winsight.Samples.WpfLab;

public partial class MainWindow : Window
{
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;
    private const int KeyDownStateMask = 0x8000;
    private const int ScanCodeShift = 16;
    private const int ScanCodeMask = 0xFF;
    private const int ExtendedKeyBit = 24;
    private const int MaxLogEntries = 300;
    private const int AddTabDelayMs = 700;
    private const string TabDragFormat = "Winsight.WpfLab.TabHeader";
    private const string PointerProbeArgument = "--pointer-probe";
    private const int PointerProbeWidth = 480;
    private const int PointerProbeHeight = 320;
    private static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(1);
    private static readonly string[] InitialTabs = { "alpha", "beta", "gamma" };
    private static readonly (string Host, string Protocol, int LatencyMs)[] Servers =
    {
        ("lab-a", "SSH", 3),
        ("lab-b", "RDP", 7),
        ("lab-c", "SFTP", 1),
    };

    private readonly DispatcherTimer _clock;
    private readonly List<(TreeViewItem Item, (string Host, string Protocol, int LatencyMs) Server)> _serverItems = new();
    private LowLevelKeyboardProbe? _keyboardProbe;
    private PointerProbeWindow? _pointerProbe;
    private int _probePresses;
    private Point _dragStart;
    private TabItem? _dragCandidate;
    private int _addedTabs;

    public MainWindow()
    {
        InitializeComponent();
        foreach (string header in InitialTabs)
        {
            SessionsTabs.Items.Add(CreateTab(header));
        }
        SessionsTabs.SelectedIndex = 0;
        foreach ((string Host, string Protocol, int LatencyMs) server in Servers)
        {
            TreeViewItem item = new() { AllowDrop = true };
            item.DragOver += OnTreeItemDragOver;
            item.Drop += OnTreeItemDrop;
            _serverItems.Add((item, server));
            ServerTree.Items.Add(item);
        }
        RefreshServerNames();
        RefreshTabOrder();

        _clock = new DispatcherTimer { Interval = ClockInterval };
        _clock.Tick += (_, _) => RefreshServerNames();
        _clock.Start();

        SizeChanged += (_, _) => SizeLabel.Text = string.Create(
            CultureInfo.InvariantCulture, $"Size: {(int)ActualWidth}x{(int)ActualHeight}");
        PreviewKeyDown += OnPreviewKeyDown;
        ContentRendered += OnContentRendered;
    }

    // The pointer probe opens on request only: a second top-level window would
    // change which window the other scenarios focus and capture by default.
    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (!Environment.GetCommandLineArgs().Contains(PointerProbeArgument, StringComparer.OrdinalIgnoreCase) ||
            !NativeMethods.GetWindowRect(new WindowInteropHelper(this).Handle, out NativeRect frame))
        {
            return;
        }
        int left = frame.Left + (frame.Right - frame.Left - PointerProbeWidth) / 2;
        int top = frame.Top + (frame.Bottom - frame.Top - PointerProbeHeight) / 2;
        _pointerProbe = PointerProbeWindow.Open(
            left, top, PointerProbeWidth, PointerProbeHeight, OnProbePress, OnProbeText);
    }

    // Control characters are spelled out so the readout stays on one line.
    private void OnProbeText(string typed) =>
        ProbeTypedText.Text = typed.Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    private void OnProbePress(ProbePress press)
    {
        _probePresses++;
        ProbePressKind.Text = press.Kind.ToString();
        ProbePressPoint.Text = string.Create(CultureInfo.InvariantCulture, $"{press.PressX},{press.PressY}");
        ProbeHoverPoint.Text = string.Create(CultureInfo.InvariantCulture, $"{press.HoverX},{press.HoverY}");
        ProbePressCount.Text = _probePresses.ToString(CultureInfo.InvariantCulture);
        AppendLog($"probe {press.Kind} press={ProbePressPoint.Text} hover={ProbeHoverPoint.Text}");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        _keyboardProbe = new LowLevelKeyboardProbe(OnHookKey);
    }

    protected override void OnClosed(EventArgs e)
    {
        _clock.Stop();
        _keyboardProbe?.Uninstall();
        _pointerProbe?.Dispose();
        base.OnClosed(e);
    }

    private void OnHookKey(HookKeyEvent keyEvent)
    {
        Key key = KeyInterop.KeyFromVirtualKey(keyEvent.VirtualKey);
        string flags = string.Create(
            CultureInfo.InvariantCulture,
            $"vk=0x{keyEvent.VirtualKey:X2} scan=0x{keyEvent.ScanCode:X2} extended={keyEvent.IsExtended} injected={keyEvent.IsInjected}");
        AppendLog($"hook {(keyEvent.IsKeyDown ? "down" : "up  ")} {flags} async={FormatModifiers(keyEvent.AsyncModifiers)}");
        if (keyEvent.IsKeyDown && !IsModifierKey(key))
        {
            LastHookChord.Text = FormatChord(keyEvent.AsyncModifiers, key);
            LastHookFlags.Text = flags;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WmKeyDown or WmSysKeyDown)
        {
            long data = lParam.ToInt64();
            int virtualKey = wParam.ToInt32();
            int scanCode = (int)((data >> ScanCodeShift) & ScanCodeMask);
            bool extended = ((data >> ExtendedKeyBit) & 1) != 0;
            Key key = KeyInterop.KeyFromVirtualKey(virtualKey);
            ModifierKeys modifiers = ReadSyncModifiers();
            string flags = string.Create(
                CultureInfo.InvariantCulture,
                $"vk=0x{virtualKey:X2} scan=0x{scanCode:X2} extended={extended}");
            AppendLog($"msg  down {flags} sync={FormatModifiers(modifiers)}");
            if (!IsModifierKey(key))
            {
                LastMessageChord.Text = FormatChord(modifiers, key);
                LastMessageFlags.Text = flags;
            }
        }
        return IntPtr.Zero;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        AppendLog($"wpf  down key={FormatKey(key)} modifiers={FormatModifiers(Keyboard.Modifiers)}");
        if (!IsModifierKey(key))
        {
            LastWpfChord.Text = FormatChord(Keyboard.Modifiers, key);
        }
    }

    private void AppendLog(string line)
    {
        KeyLog.Items.Insert(0, line);
        while (KeyLog.Items.Count > MaxLogEntries)
        {
            KeyLog.Items.RemoveAt(KeyLog.Items.Count - 1);
        }
    }

    private TabItem CreateTab(string header)
    {
        TabItem tab = new()
        {
            Header = header,
            AllowDrop = true,
            Content = new TextBlock { Text = $"Session {header}", Margin = new Thickness(8) },
        };
        tab.PreviewMouseLeftButtonDown += OnTabPreviewMouseDown;
        tab.PreviewMouseMove += OnTabPreviewMouseMove;
        tab.DragOver += OnTabDragOver;
        tab.Drop += OnTabDrop;
        return tab;
    }

    private void OnTabPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragCandidate = sender as TabItem;
    }

    private void OnTabPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null)
        {
            return;
        }
        Vector moved = e.GetPosition(this) - _dragStart;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }
        TabItem source = _dragCandidate;
        _dragCandidate = null;
        DataObject data = new(TabDragFormat, (string)source.Header);
        DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
    }

    private void OnTabDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(TabDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTabDrop(object sender, DragEventArgs e)
    {
        if (sender is not TabItem target || e.Data.GetData(TabDragFormat) is not string header)
        {
            return;
        }
        TabItem? source = SessionsTabs.Items.OfType<TabItem>().FirstOrDefault(t => (string)t.Header == header);
        if (source is null || ReferenceEquals(source, target))
        {
            DropStatus.Text = $"Dropped {header} on itself";
            e.Handled = true;
            return;
        }
        int targetIndex = SessionsTabs.Items.IndexOf(target);
        SessionsTabs.Items.Remove(source);
        SessionsTabs.Items.Insert(targetIndex, source);
        SessionsTabs.SelectedItem = source;
        DropStatus.Text = string.Create(CultureInfo.InvariantCulture, $"Moved {header} to {targetIndex}");
        RefreshTabOrder();
        e.Handled = true;
    }

    private void OnTreeItemDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(TabDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTreeItemDrop(object sender, DragEventArgs e)
    {
        if (sender is not TreeViewItem item || e.Data.GetData(TabDragFormat) is not string header)
        {
            return;
        }
        string host = _serverItems.First(entry => ReferenceEquals(entry.Item, item)).Server.Host;
        DropStatus.Text = $"Dropped {header} on {host}";
        e.Handled = true;
    }

    private async void OnAddTabLaterClick(object sender, RoutedEventArgs e)
    {
        await Task.Delay(AddTabDelayMs);
        _addedTabs++;
        SessionsTabs.Items.Add(CreateTab(string.Create(CultureInfo.InvariantCulture, $"extra-{_addedTabs}")));
        RefreshTabOrder();
    }

    private void RefreshServerNames()
    {
        string clock = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        foreach ((TreeViewItem item, (string host, string protocol, int latencyMs)) in _serverItems)
        {
            item.Header = string.Create(
                CultureInfo.InvariantCulture,
                $"{host}, protocol {protocol}, state Reachable ({latencyMs} ms) · {clock}");
        }
    }

    private void RefreshTabOrder()
    {
        TabOrder.Text = "Tabs: " + string.Join(",", SessionsTabs.Items.OfType<TabItem>().Select(t => (string)t.Header));
    }

    private static ModifierKeys ReadSyncModifiers()
    {
        ModifierKeys modifiers = ModifierKeys.None;
        if (IsSyncKeyDown(VkControl))
        {
            modifiers |= ModifierKeys.Control;
        }
        if (IsSyncKeyDown(VkShift))
        {
            modifiers |= ModifierKeys.Shift;
        }
        if (IsSyncKeyDown(VkMenu))
        {
            modifiers |= ModifierKeys.Alt;
        }
        if (IsSyncKeyDown(VkLwin) || IsSyncKeyDown(VkRwin))
        {
            modifiers |= ModifierKeys.Windows;
        }
        return modifiers;
    }

    private static bool IsSyncKeyDown(int virtualKey) =>
        (NativeMethods.GetKeyState(virtualKey) & KeyDownStateMask) != 0;

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    private static string FormatChord(ModifierKeys modifiers, Key key)
    {
        string prefix = FormatModifiers(modifiers);
        return prefix.Length == 0 ? FormatKey(key) : prefix + "+" + FormatKey(key);
    }

    private static string FormatModifiers(ModifierKeys modifiers)
    {
        List<string> parts = new();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }
        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }
        return string.Join("+", parts);
    }

    // Key has aliased members (Prior/PageUp, Next/PageDown, Return/Enter) whose
    // ToString() is not guaranteed, and digits are D0-D9; spell them the way a
    // chord is written so the probe text compares directly with it.
    private static string FormatKey(Key key) => key switch
    {
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Enter => "Enter",
        Key.Capital => "CapsLock",
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture),
        _ => key.ToString(),
    };

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
