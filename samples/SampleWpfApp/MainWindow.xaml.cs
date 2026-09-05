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
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Winsight.Samples.SimpleCounter.ViewModels;

namespace Winsight.Samples.SimpleCounter;

public partial class MainWindow : Window
{
    private const string RefuseCloseArgument = "--refuse-close";
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_HOTKEY = 0x0312;
    private const int ShortcutHotKeyId = 1;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const uint VK_N = 0x4E;

    private IntPtr _windowHandle;

    public MainWindow()
    {
        InitializeComponent();
        if (Environment.GetCommandLineArgs().Contains(RefuseCloseArgument, StringComparer.Ordinal))
        {
            // A deterministic unsaved-work fixture for the lifecycle safety tests.
            Closing += (_, args) => args.Cancel = true;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        HwndSource? source = HwndSource.FromHwnd(_windowHandle);
        source?.AddHook(WndProc);
        NativeMethods.RegisterHotKey(_windowHandle, ShortcutHotKeyId, MOD_CONTROL | MOD_SHIFT, VK_N);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_windowHandle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, ShortcutHotKeyId);
        }
        base.OnClosed(e);
    }

    private void OnOpenDialogClick(object sender, RoutedEventArgs e)
    {
        NoteDialog dialog = new() { Owner = this };
        if (dialog.ShowDialog() == true && DataContext is CounterViewModel viewModel)
        {
            viewModel.LastNote = dialog.NoteText;
        }
    }

    // Throws and swallows an exception so the runtime still emits an
    // ExceptionThrown event, giving the causal exception oracle something to
    // observe without crashing the sample app.
    private void OnThrowClick(object sender, RoutedEventArgs e)
    {
        try
        {
            throw new InvalidOperationException("Winsight sample: deliberate exception");
        }
        catch (InvalidOperationException)
        {
            // Publish a completion oracle: UIA Invoke may return before this handler runs.
            if (DataContext is CounterViewModel viewModel) viewModel.LastNote = "Exception handler completed";
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == ShortcutHotKeyId)
        {
            handled = SetShortcutNote();
        }
        else if (msg == WM_KEYDOWN &&
                 KeyInterop.KeyFromVirtualKey(wParam.ToInt32()) == Key.N &&
                 IsKeyDown(VK_CONTROL) &&
                 IsKeyDown(VK_SHIFT))
        {
            handled = SetShortcutNote();
        }

        return IntPtr.Zero;
    }

    private bool SetShortcutNote()
    {
        if (DataContext is not CounterViewModel viewModel)
        {
            return false;
        }

        viewModel.LastNote = "Shortcut: Ctrl+Shift+N";
        return true;
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (NativeMethods.GetKeyState(virtualKey) & 0x8000) != 0;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
