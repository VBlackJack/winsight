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
using Winsight.Core.Modules.UserActivity;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Cross-process arbitration within the interactive Windows session; sync and
/// reentrant. The outermost lease also enforces the yield-to-user policy and
/// records its injection window in the session input ledger, so every
/// Winsight process can tell its own input from a human's.
/// </summary>
public sealed class DesktopInputLease : IDisposable
{
    private const int DefaultWaitMs = 1000;
    [ThreadStatic] private static int _depth;
    [ThreadStatic] private static uint _currentThreadLeaseId;
    private readonly Mutex? _mutex;
    private bool _disposed;

    private DesktopInputLease(Mutex? mutex, uint leaseThreadId)
    {
        _mutex = mutex;
        LeaseThreadId = leaseThreadId;
    }

    /// <summary>Win32 thread ID that acquired this lease.</summary>
    public uint LeaseThreadId { get; }

    /// <summary>Win32 thread ID if the current thread holds a desktop input lease, else null.</summary>
    internal static uint? CurrentThreadLeaseId => _depth > 0 ? _currentThreadLeaseId : null;

    public static DesktopInputLease Acquire(int timeoutMs = DefaultWaitMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeoutMs);
        uint currentTid = GetCurrentThreadId();
        if (_depth > 0)
        {
            _depth++;
            return new DesktopInputLease(null, currentTid);
        }
        using Process process = Process.GetCurrentProcess();
        Mutex mutex = new(false, $@"Local\Winsight.DesktopInput.{process.SessionId}");
        bool acquired;
        try { acquired = mutex.WaitOne(timeoutMs); }
        catch (AbandonedMutexException) { acquired = true; }
        catch { mutex.Dispose(); throw; }
        if (!acquired)
        {
            mutex.Dispose();
            throw new TimeoutException("desktop_busy: another Winsight instance is using this desktop. Observe again before retrying.");
        }
        string? refusal;
        try
        {
            // Sample before marking the injection, so the input that preceded it
            // is recorded as human even when the yield policy is off.
            long? humanIdleMs = UserActivityMonitor.ReadHumanIdleMs();
            refusal = UserActivityPolicy.CheckYield(UserActivityPolicy.YieldToUserMs, () => humanIdleMs);
            if (refusal is null)
            {
                InputLedger.MarkInjectionStart(UserActivityMonitor.TickNow());
            }
        }
        catch
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
        if (refusal is not null)
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            throw new InvalidOperationException(refusal);
        }
        _depth = 1;
        _currentThreadLeaseId = currentTid;
        return new DesktopInputLease(mutex, currentTid);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _depth--;
        if (_mutex is not null)
        {
            _currentThreadLeaseId = 0;
            InputLedger.MarkInjectionEnd(UserActivityMonitor.TickNow());
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
