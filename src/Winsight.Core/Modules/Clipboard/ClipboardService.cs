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
using Winsight.Core.Models;

namespace Winsight.Core.Modules.Clipboard;

/// <summary>One clipboard format kept by a snapshot: its id and a copy of its memory block.</summary>
internal sealed record ClipboardFormatData(uint Format, byte[] Bytes);

/// <summary>
/// The clipboard as it was before Winsight replaced it. Formats whose data is
/// not a plain memory block (bitmaps, metafiles, palettes, private and GDI
/// handles) cannot be copied and are listed in <see cref="Skipped"/>.
/// </summary>
internal sealed record ClipboardSnapshot(IReadOnlyList<ClipboardFormatData> Formats, IReadOnlyList<uint> Skipped)
{
    public long TotalBytes => Formats.Sum(format => (long)format.Bytes.Length);
}

/// <summary>
/// Text in, text out, and a save and restore of everything else, through the
/// Win32 clipboard. Text Winsight writes is marked so it stays out of the
/// clipboard history, off the cloud clipboard and away from clipboard
/// monitors. Callers check <see cref="ClipboardPolicy"/> first; this class
/// never logs clipboard contents.
/// </summary>
public sealed class ClipboardService
{
    public const int DefaultTimeoutMs = 5000;

    /// <summary>A snapshot larger than this is refused rather than held in memory.</summary>
    public const long MaxSnapshotBytes = 64L * 1024 * 1024;

    internal const string ExcludeFromMonitorsFormat = "ExcludeClipboardContentFromMonitorProcessing";
    internal const string HistoryFormat = "CanIncludeInClipboardHistory";
    internal const string CloudFormat = "CanUploadToCloudClipboard";

    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const int OpenAttempts = 10;
    private const int OpenRetryDelayMs = 20;

    // Clipboard formats whose handle is not an HGLOBAL memory block.
    private static readonly HashSet<uint> HandleFormats = new()
    {
        2,    // CF_BITMAP
        3,    // CF_METAFILEPICT
        9,    // CF_PALETTE
        14,   // CF_ENHMETAFILE
        0x80, // CF_OWNERDISPLAY
        0x82, // CF_DSPBITMAP
        0x83, // CF_DSPMETAFILEPICT
        0x8E, // CF_DSPENHMETAFILE
    };
    private const uint PrivateFirst = 0x0200;
    private const uint GdiObjectLast = 0x03FF;

    /// <summary>Read the clipboard text; data is null when the clipboard holds no text.</summary>
    public OperationResult ReadText() =>
        Run(window =>
        {
            Open(window);
            try
            {
                IntPtr data = GetClipboardData(CfUnicodeText);
                if (data == IntPtr.Zero)
                {
                    return OperationResult.OkWith(new ClipboardTextResult(null, 0));
                }
                IntPtr text = GlobalLock(data);
                try
                {
                    string value = Marshal.PtrToStringUni(text) ?? string.Empty;
                    return OperationResult.OkWith(new ClipboardTextResult(value, value.Length));
                }
                finally
                {
                    GlobalUnlock(data);
                }
            }
            finally
            {
                CloseClipboard();
            }
        });

    /// <summary>Replace the clipboard with <paramref name="text"/>, kept out of history and the cloud.</summary>
    public OperationResult WriteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Run(window =>
        {
            Open(window);
            try
            {
                EmptyClipboard();
                SetUnicodeText(text);
                MarkPrivate();
                return OperationResult.OkWith(new ClipboardTextResult(null, text.Length));
            }
            finally
            {
                CloseClipboard();
            }
        });
    }

    internal ClipboardSnapshot Capture() =>
        Invoke(window =>
        {
            Open(window);
            try
            {
                List<ClipboardFormatData> formats = new();
                List<uint> skipped = new();
                long total = 0;
                for (uint format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
                {
                    if (!IsMemoryFormat(format))
                    {
                        skipped.Add(format);
                        continue;
                    }
                    byte[]? bytes = ReadBlock(format);
                    if (bytes is null)
                    {
                        skipped.Add(format);
                        continue;
                    }
                    total += bytes.Length;
                    if (total > MaxSnapshotBytes)
                    {
                        throw new InvalidOperationException(string.Create(
                            CultureInfo.InvariantCulture,
                            $"The clipboard holds more than {MaxSnapshotBytes} bytes; it was not saved, so nothing was pasted."));
                    }
                    formats.Add(new ClipboardFormatData(format, bytes));
                }
                return new ClipboardSnapshot(formats, skipped);
            }
            finally
            {
                CloseClipboard();
            }
        });

    /// <summary>Put a snapshot back. The restored content is kept out of history, so it is not listed twice.</summary>
    internal void Restore(ClipboardSnapshot snapshot) =>
        Invoke(window =>
        {
            Open(window);
            try
            {
                EmptyClipboard();
                foreach (ClipboardFormatData format in snapshot.Formats)
                {
                    SetBlock(format.Format, format.Bytes);
                }
                if (snapshot.Formats.Count > 0)
                {
                    MarkPrivate();
                }
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        });

    internal static bool IsMemoryFormat(uint format) =>
        !HandleFormats.Contains(format) && format is < PrivateFirst or > GdiObjectLast;

    private static OperationResult Run(Func<IntPtr, OperationResult> work)
    {
        try
        {
            return Invoke(work);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            return OperationResult.Failure(ex.Message);
        }
    }

    private static T Invoke<T>(Func<IntPtr, T> work) => ClipboardThread.Instance.Invoke(work, DefaultTimeoutMs);

    private static void Open(IntPtr window)
    {
        for (int attempt = 1; attempt <= OpenAttempts; attempt++)
        {
            if (OpenClipboard(window))
            {
                return;
            }
            Thread.Sleep(OpenRetryDelayMs);
        }
        throw new InvalidOperationException(
            "clipboard_busy: another application keeps the clipboard open. Retry in a moment.");
    }

    private static byte[]? ReadBlock(uint format)
    {
        IntPtr data = GetClipboardData(format);
        if (data == IntPtr.Zero)
        {
            return null;
        }
        IntPtr source = GlobalLock(data);
        if (source == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            byte[] bytes = new byte[(int)GlobalSize(data)];
            Marshal.Copy(source, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            GlobalUnlock(data);
        }
    }

    private static void SetUnicodeText(string text)
    {
        byte[] bytes = new byte[(text.Length + 1) * sizeof(char)];
        System.Text.Encoding.Unicode.GetBytes(text, 0, text.Length, bytes, 0);
        SetBlock(CfUnicodeText, bytes);
    }

    // Registered formats that ask Windows not to keep, sync or broadcast this content.
    private static void MarkPrivate()
    {
        byte[] no = BitConverter.GetBytes(0);
        SetBlock(RegisterClipboardFormat(ExcludeFromMonitorsFormat), no);
        SetBlock(RegisterClipboardFormat(HistoryFormat), no);
        SetBlock(RegisterClipboardFormat(CloudFormat), no);
    }

    private static void SetBlock(uint format, byte[] bytes)
    {
        IntPtr memory = GlobalAlloc(GmemMoveable, (nuint)Math.Max(1, bytes.Length));
        if (memory == IntPtr.Zero)
        {
            throw new InvalidOperationException("Out of memory while writing the clipboard.");
        }
        IntPtr target = GlobalLock(memory);
        Marshal.Copy(bytes, 0, target, bytes.Length);
        GlobalUnlock(memory);
        if (SetClipboardData(format, memory) == IntPtr.Zero)
        {
            GlobalFree(memory);
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture, $"Windows refused clipboard format {format}."));
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClipboardFormatW")]
    private static extern uint RegisterClipboardFormat(string name);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(IntPtr memory);
}
