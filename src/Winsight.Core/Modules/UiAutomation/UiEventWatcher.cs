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
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.UIA3;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.UiAutomation;

public sealed record UiAutomationEventRecord(
    DateTime TimestampUtc,
    string EventKind,
    string ControlType,
    string Name,
    string? AutomationId,
    long WindowHandle);

public sealed record UiEventWatchResult(
    int ProcessId,
    int DurationMs,
    bool Truncated,
    IReadOnlyList<UiAutomationEventRecord> Events);

/// <summary>
/// Subscribes to native UI Automation events (WindowOpened, MenuOpened,
/// InvokeInvoked, StructureChanged) for a bounded duration so agents can catch
/// transient dialogs, popups, and structural updates without polling gaps.
/// </summary>
public sealed class UiEventWatcher
{
    public const int DefaultDurationMs = 3000;
    public const int MaxDurationMs = 30000;
    public const int DefaultMaxEvents = 50;
    public const int MaxEventsLimit = 200;

    public async Task<OperationResult> WatchAsync(
        int processId,
        int durationMs = DefaultDurationMs,
        int maxEvents = DefaultMaxEvents,
        string? windowTitle = null,
        CancellationToken cancellationToken = default)
    {
        if (durationMs is < 100 or > MaxDurationMs)
        {
            return OperationResult.Failure($"durationMs must be between 100 and {MaxDurationMs}.");
        }
        if (maxEvents is < 1 or > MaxEventsLimit)
        {
            return OperationResult.Failure($"maxEvents must be between 1 and {MaxEventsLimit}.");
        }

        using UIA3Automation automation = new();
        IReadOnlyList<WindowRoot> roots = ElementFinder.ResolveWindows(automation, processId, windowTitle);
        if (roots.Count == 0)
        {
            return OperationResult.Failure($"No visible windows found for process {processId}.");
        }

        object gate = new();
        List<UiAutomationEventRecord> captured = new();
        bool truncated = false;
        using CancellationTokenSource limitReached = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void Record(string kind, AutomationElement? sender)
        {
            if (sender is null) return;
            try
            {
                if (sender.Properties.ProcessId.ValueOrDefault != processId) return;
                UiAutomationEventRecord record = new(
                    DateTime.UtcNow,
                    kind,
                    sender.Properties.ControlType.ValueOrDefault.ToString(),
                    sender.Properties.Name.ValueOrDefault ?? string.Empty,
                    sender.Properties.AutomationId.ValueOrDefault,
                    sender.Properties.NativeWindowHandle.ValueOrDefault.ToInt64());

                lock (gate)
                {
                    if (captured.Count >= maxEvents)
                    {
                        truncated = true;
                        limitReached.Cancel();
                        return;
                    }
                    captured.Add(record);
                    if (captured.Count >= maxEvents)
                    {
                        limitReached.Cancel();
                    }
                }
            }
            catch (COMException)
            {
                // Transient element vanished before properties were read.
            }
        }

        List<IDisposable> handlers = new();
        try
        {
            foreach (WindowRoot root in roots)
            {
                try
                {
                    handlers.Add(root.Element.RegisterAutomationEvent(
                        automation.EventLibrary.Window.WindowOpenedEvent,
                        TreeScope.Subtree,
                        (element, _) => Record("window_opened", element)));
                    handlers.Add(root.Element.RegisterAutomationEvent(
                        automation.EventLibrary.Element.MenuOpenedEvent,
                        TreeScope.Subtree,
                        (element, _) => Record("menu_opened", element)));
                    handlers.Add(root.Element.RegisterAutomationEvent(
                        automation.EventLibrary.Invoke.InvokedEvent,
                        TreeScope.Subtree,
                        (element, _) => Record("invoked", element)));
                    handlers.Add(root.Element.RegisterStructureChangedEvent(
                        TreeScope.Subtree,
                        (element, changeType, _) => Record($"structure_{changeType}", element)));
                }
                catch (COMException)
                {
                    // Some providers decline specific event subscriptions; continue with remaining roots.
                }
            }

            try
            {
                await Task.Delay(durationMs, limitReached.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Max event count reached early.
            }
        }
        finally
        {
            foreach (IDisposable handler in handlers)
            {
                try
                {
                    handler.Dispose();
                }
                catch (COMException)
                {
                    // Target window may have closed before unregistration.
                }
            }
        }

        List<UiAutomationEventRecord> snapshot;
        bool wasTruncated;
        lock (gate)
        {
            snapshot = captured.ToList();
            wasTruncated = truncated;
        }

        return OperationResult.OkWith(new UiEventWatchResult(processId, durationMs, wasTruncated, snapshot));
    }
}
