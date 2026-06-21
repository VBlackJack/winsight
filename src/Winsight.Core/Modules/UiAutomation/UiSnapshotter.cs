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

using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Captures a <see cref="UiSnapshot"/> of a process — its windows plus its
/// actionable elements — by composing <see cref="WindowDiscovery"/> and
/// <see cref="AffordanceScanner"/>. Pairing two snapshots through
/// <see cref="UiDiffer"/> yields what an action changed.
/// </summary>
public sealed class UiSnapshotter
{
    private readonly WindowDiscovery _discovery;
    private readonly AffordanceScanner _scanner;

    public UiSnapshotter(WindowDiscovery discovery, AffordanceScanner scanner)
    {
        _discovery = discovery;
        _scanner = scanner;
    }

    public UiSnapshot Capture(int processId, string? windowTitle = null)
    {
        IReadOnlyList<WindowDescriptor> windows = _discovery.List(processId);
        IReadOnlyList<UiInteractable> interactables = _scanner.Describe(processId, windowTitle);
        return new UiSnapshot(windows, interactables);
    }
}
