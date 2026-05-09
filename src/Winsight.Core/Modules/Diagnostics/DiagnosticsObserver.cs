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
using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.Diagnostics;

[SupportedOSPlatform("windows")]
public sealed class DiagnosticsObserver
{
    public const int DefaultDurationMs = 2000;
    public const int MinDurationMs = 500;
    public const int MaxDurationMs = 30000;

    private const string EventCountersEventName = "EventCounters";
    private const string CounterIntervalSeconds = "1";

    public const int DefaultMaxEvents = 500;
    public const int MaxAllowedEvents = 10000;

    public CounterSnapshot ObserveCounters(int processId, string providerName, int durationMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ValidateDuration(durationMs);

        var client = new DiagnosticsClient(processId);
        var providers = new[]
        {
            new EventPipeProvider(
                name: providerName,
                eventLevel: System.Diagnostics.Tracing.EventLevel.Informational,
                keywords: 0xFFFFFFFFL,
                arguments: new Dictionary<string, string>
                {
                    ["EventCounterIntervalSec"] = CounterIntervalSeconds,
                }),
        };

        var counters = new ConcurrentDictionary<string, double>();
        var sampleCount = 0;

        using var session = client.StartEventPipeSession(providers, requestRundown: false);
        using var source = new EventPipeEventSource(session.EventStream);

        source.Dynamic.All += traceEvent =>
        {
            if (!string.Equals(traceEvent.EventName, EventCountersEventName, StringComparison.Ordinal))
            {
                return;
            }
            if (TryExtractCounter(traceEvent, out var name, out var value))
            {
                counters[name] = value;
                Interlocked.Increment(ref sampleCount);
            }
        };

        var processingTask = Task.Run(() =>
        {
            try
            {
                source.Process();
            }
            catch (Exception)
            {
                // Session was stopped; expected end-of-stream paths can throw.
            }
        });

        Thread.Sleep(durationMs);
        try
        {
            session.Stop();
        }
        catch (ServerNotAvailableException)
        {
            // Target process exited mid-collection.
        }

        processingTask.Wait(TimeSpan.FromSeconds(2));

        return new CounterSnapshot(
            ProcessId: processId,
            ProviderName: providerName,
            DurationMs: durationMs,
            SamplesObserved: sampleCount,
            Counters: counters.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private static bool TryExtractCounter(TraceEvent traceEvent, out string name, out double value)
    {
        name = string.Empty;
        value = 0;
        if (traceEvent.PayloadValue(0) is not IDictionary<string, object> outerPayload)
        {
            return false;
        }
        if (!outerPayload.TryGetValue("Payload", out var innerObj) ||
            innerObj is not IDictionary<string, object> inner)
        {
            return false;
        }
        if (!inner.TryGetValue("Name", out var nameObj) || nameObj is null)
        {
            return false;
        }
        name = nameObj.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        // Mean for averaging counters (cpu-usage, gc-heap-size).
        // Increment for monotonic counters (alloc-rate, exception-count).
        if (inner.TryGetValue("Mean", out var meanObj) && TryToDouble(meanObj, out value))
        {
            return true;
        }
        if (inner.TryGetValue("Increment", out var incObj) && TryToDouble(incObj, out value))
        {
            return true;
        }
        return false;
    }

    private static bool TryToDouble(object? raw, out double value)
    {
        switch (raw)
        {
            case double d:
                value = d;
                return true;
            case float f:
                value = f;
                return true;
            case int i:
                value = i;
                return true;
            case long l:
                value = l;
                return true;
            case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                value = parsed;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    public EventCaptureResult CaptureEvents(
        int processId,
        IReadOnlyList<string> providerNames,
        int durationMs,
        int maxEvents = DefaultMaxEvents,
        string? eventNameFilter = null)
    {
        ArgumentNullException.ThrowIfNull(providerNames);
        if (providerNames.Count == 0)
        {
            throw new ArgumentException(
                "At least one provider name is required.",
                nameof(providerNames));
        }
        ValidateDuration(durationMs);
        if (maxEvents <= 0 || maxEvents > MaxAllowedEvents)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEvents),
                $"maxEvents must be between 1 and {MaxAllowedEvents}.");
        }

        var client = new DiagnosticsClient(processId);
        var providers = providerNames
            .Select(name => new EventPipeProvider(
                name: name,
                eventLevel: System.Diagnostics.Tracing.EventLevel.Informational,
                keywords: 0xFFFFFFFFL))
            .ToArray();

        var captured = new ConcurrentQueue<DiagnosticEvent>();
        var capturedCount = 0;
        var truncated = false;

        using var session = client.StartEventPipeSession(providers, requestRundown: false);
        using var source = new EventPipeEventSource(session.EventStream);

        source.Dynamic.All += traceEvent =>
        {
            if (Volatile.Read(ref capturedCount) >= maxEvents)
            {
                Volatile.Write(ref truncated, true);
                return;
            }
            if (eventNameFilter is not null &&
                !string.Equals(traceEvent.EventName, eventNameFilter, StringComparison.Ordinal))
            {
                return;
            }

            captured.Enqueue(new DiagnosticEvent(
                TimestampUtc: traceEvent.TimeStamp.ToUniversalTime(),
                ProviderName: traceEvent.ProviderName,
                EventName: traceEvent.EventName ?? string.Empty,
                EventId: (int)traceEvent.ID,
                Level: traceEvent.Level.ToString(),
                Payload: ExtractPayload(traceEvent)));
            Interlocked.Increment(ref capturedCount);
        };

        var processingTask = Task.Run(() =>
        {
            try
            {
                source.Process();
            }
            catch (Exception)
            {
                // Session stopped; end-of-stream paths can throw.
            }
        });

        Thread.Sleep(durationMs);
        try
        {
            session.Stop();
        }
        catch (ServerNotAvailableException)
        {
            // Target process exited mid-collection.
        }

        processingTask.Wait(TimeSpan.FromSeconds(2));

        return new EventCaptureResult(
            ProcessId: processId,
            Providers: providerNames,
            DurationMs: durationMs,
            TotalCaptured: captured.Count,
            Truncated: truncated,
            Events: captured.ToArray());
    }

    private static Dictionary<string, object?> ExtractPayload(TraceEvent traceEvent)
    {
        var names = traceEvent.PayloadNames;
        if (names is null || names.Length == 0)
        {
            return new Dictionary<string, object?>(0);
        }

        var payload = new Dictionary<string, object?>(names.Length, StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }
            object? raw;
            try
            {
                raw = traceEvent.PayloadValue(i);
            }
            catch (Exception)
            {
                // PayloadValue can throw on parser-internal errors for malformed events.
                continue;
            }
            payload[name] = PayloadNormalizer.Normalize(raw);
        }
        return payload;
    }

    private static void ValidateDuration(int durationMs)
    {
        if (durationMs < MinDurationMs || durationMs > MaxDurationMs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationMs),
                $"durationMs must be between {MinDurationMs} and {MaxDurationMs}.");
        }
    }
}
