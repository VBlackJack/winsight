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

namespace Winsight.Core.Modules.UserActivity;

/// <summary>
/// Lightweight periodic background sampler that polls <c>GetLastInputInfo</c>
/// and cursor position at <see cref="UserActivityPolicy.ActivitySampleMs"/>
/// (default 250 ms, 0 disables) so <c>recentInputs</c> and the periodic
/// micro-move detector observe input events even between MCP tool calls.
/// </summary>
internal sealed class ActivityBackgroundSampler : IDisposable
{
    private readonly Action _sampleAction;
    private readonly ITimer? _timer;
    private int _running;
    private int _disposed;

    public ActivityBackgroundSampler(Action sampleAction, int intervalMs, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sampleAction);
        _sampleAction = sampleAction;
        IntervalMs = intervalMs;
        if (intervalMs > 0)
        {
            TimeSpan period = TimeSpan.FromMilliseconds(intervalMs);
            _timer = (timeProvider ?? TimeProvider.System).CreateTimer(OnTick, null, period, period);
        }
    }

    public int IntervalMs { get; }

    public bool IsEnabled => _timer is not null && Volatile.Read(ref _disposed) == 0;

    private void OnTick(object? state)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }
        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            return;
        }
        try
        {
            _sampleAction();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or ThreadAbortException))
        {
            // Background sampling must never crash the host process.
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _timer?.Dispose();
    }
}
