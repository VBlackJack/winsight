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

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>Cross-process arbitration within the interactive Windows session; sync and reentrant.</summary>
public sealed class DesktopInputLease : IDisposable
{
    private const int DefaultWaitMs = 1000;
    [ThreadStatic] private static int _depth;
    private readonly Mutex? _mutex;
    private bool _disposed;

    private DesktopInputLease(Mutex? mutex) => _mutex = mutex;

    public static DesktopInputLease Acquire(int timeoutMs = DefaultWaitMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeoutMs);
        if (_depth > 0)
        {
            _depth++;
            return new DesktopInputLease(null);
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
        _depth = 1;
        return new DesktopInputLease(mutex);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _depth--;
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
