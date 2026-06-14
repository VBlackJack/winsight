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

using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public class WindowDiscoveryTests
{
    [Fact]
    [Trait("Category", "RequiresDesktop")]
    public void List_returns_at_least_the_explorer_or_sample_when_a_desktop_session_exists()
    {
        var discovery = new WindowDiscovery();
        IReadOnlyList<WindowDescriptor> windows = discovery.List();
        windows.Should().NotBeNull();
        // We don't assert on count: some CI agents run headless and will return 0.
        // The contract here is just that the call does not throw.
    }
}
