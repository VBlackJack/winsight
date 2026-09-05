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
using FluentAssertions;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public sealed class DesktopInputLeaseTests
{
    [Fact]
    public void Lease_is_reentrant_on_its_owning_thread()
    {
        using DesktopInputLease first = DesktopInputLease.Acquire();
        using DesktopInputLease second = DesktopInputLease.Acquire(0);
    }

    [Fact]
    public async Task Another_process_is_excluded_and_abandoned_ownership_is_recovered()
    {
        using Process self = Process.GetCurrentProcess();
        ProcessStartInfo start = new("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(@"$m = [Threading.Mutex]::new($false, 'Local\Winsight.DesktopInput." + self.SessionId + "'); $null=$m.WaitOne(); [Console]::WriteLine('locked'); $null=[Console]::ReadLine()");
        using Process child = Process.Start(start)!;
        try
        {
            string? ready = await child.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            ready.Should().Be("locked");
            Action attempt = () => { using DesktopInputLease lease = DesktopInputLease.Acquire(0); };
            attempt.Should().Throw<TimeoutException>();
            child.Kill();
            await child.WaitForExitAsync(TestContext.Current.CancellationToken);
            using DesktopInputLease recovered = DesktopInputLease.Acquire();
        }
        finally { if (!child.HasExited) child.Kill(); }
    }
}
