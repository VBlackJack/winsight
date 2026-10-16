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

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Winsight.Core.Modules.Clipboard;

/// <summary>
/// A dedicated thread that owns a message-only window and pumps its messages
/// for the life of the process. Data placed on the clipboard belongs to the
/// window that emptied it, and Windows sends that owner messages when another
/// application takes the clipboard: an owner on a thread that does not pump
/// would stall that application. Every clipboard call runs here.
/// </summary>
internal sealed class ClipboardThread
{
    private const string StaticClass = "STATIC";
    private const uint WorkMessage = 0x8000 + 0x2A;
    private static readonly IntPtr MessageOnlyParent = new(-3);
    private static readonly Lazy<ClipboardThread> s_instance = new(() => new ClipboardThread());

    private readonly ConcurrentQueue<Action> _work = new();
    private IntPtr _window;
    private Exception? _startFailure;

    private ClipboardThread()
    {
        using ManualResetEventSlim ready = new();
        Thread thread = new(() => Pump(ready)) { IsBackground = true, Name = "Winsight clipboard owner" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
    }

    public static ClipboardThread Instance => s_instance.Value;

    /// <summary>Run <paramref name="work"/> on the owner thread with its window handle.</summary>
    public T Invoke<T>(Func<IntPtr, T> work, int timeoutMs)
    {
        if (_startFailure is not null)
        {
            throw new InvalidOperationException("The clipboard owner window could not be created.", _startFailure);
        }
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(() =>
        {
            try
            {
                completion.SetResult(work(_window));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        PostMessage(_window, WorkMessage, IntPtr.Zero, IntPtr.Zero);
        if (!completion.Task.Wait(timeoutMs))
        {
            throw new TimeoutException($"The clipboard did not answer within {timeoutMs} ms.");
        }
        return completion.Task.Result;
    }

    private void Pump(ManualResetEventSlim ready)
    {
        _window = CreateWindowEx(
            0, StaticClass, string.Empty, 0, 0, 0, 0, 0, MessageOnlyParent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_window == IntPtr.Zero)
        {
            _startFailure = new Win32Exception(Marshal.GetLastWin32Error());
            ready.Set();
            return;
        }
        ready.Set();
        while (GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.Message == WorkMessage)
            {
                while (_work.TryDequeue(out Action? item))
                {
                    item();
                }
                continue;
            }
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
