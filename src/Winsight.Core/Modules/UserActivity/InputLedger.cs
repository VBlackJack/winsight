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
using System.IO.MemoryMappedFiles;

namespace Winsight.Core.Modules.UserActivity;

/// <summary>
/// Session-wide record of Winsight input, in named shared memory so every
/// Winsight process in the Windows session sees the injections of the others.
/// Each field is an aligned 32-bit slot written whole. Injection fields are
/// written only by the holder of the desktop input lease; the human field is a
/// best-effort latest value any process may refresh.
/// </summary>
internal static class InputLedger
{
    private const int Capacity = 64;
    private const int HasInjectionOffset = 0;
    private const int InjectionActiveOffset = 4;
    private const int InjectionStartOffset = 8;
    private const int InjectionEndOffset = 12;
    private const int HasHumanOffset = 16;
    private const int HumanTickOffset = 20;
    private const int True = 1;
    private const int False = 0;

    private static readonly Lock Gate = new();
    private static MemoryMappedViewAccessor? s_view;

    public static LedgerState Read()
    {
        lock (Gate)
        {
            MemoryMappedViewAccessor view = View();
            return new LedgerState(
                view.ReadInt32(HasInjectionOffset) == True,
                view.ReadInt32(InjectionActiveOffset) == True,
                view.ReadUInt32(InjectionStartOffset),
                view.ReadUInt32(InjectionEndOffset),
                view.ReadInt32(HasHumanOffset) == True,
                view.ReadUInt32(HumanTickOffset));
        }
    }

    public static void MarkInjectionStart(uint tick)
    {
        lock (Gate)
        {
            MemoryMappedViewAccessor view = View();
            view.Write(InjectionStartOffset, tick);
            view.Write(InjectionActiveOffset, True);
            view.Write(HasInjectionOffset, True);
        }
    }

    public static void MarkInjectionEnd(uint tick)
    {
        lock (Gate)
        {
            MemoryMappedViewAccessor view = View();
            view.Write(InjectionEndOffset, tick);
            view.Write(InjectionActiveOffset, False);
        }
    }

    public static void RecordHuman(uint tick)
    {
        lock (Gate)
        {
            MemoryMappedViewAccessor view = View();
            view.Write(HumanTickOffset, tick);
            view.Write(HasHumanOffset, True);
        }
    }

    // The mapping stays open for the life of the process, so the ledger
    // survives as long as any Winsight process in the session is running.
    private static MemoryMappedViewAccessor View()
    {
        if (s_view is null)
        {
            using Process process = Process.GetCurrentProcess();
            MemoryMappedFile file = MemoryMappedFile.CreateOrOpen(
                $@"Local\Winsight.InputLedger.{process.SessionId}", Capacity);
            s_view = file.CreateViewAccessor(0, Capacity);
        }
        return s_view;
    }
}
