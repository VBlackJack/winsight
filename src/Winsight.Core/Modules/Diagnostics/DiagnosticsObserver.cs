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
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
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

    /// <summary>The CLR runtime ETW/EventPipe provider that emits exception events.</summary>
    public const string DotNetRuntimeProvider = "Microsoft-Windows-DotNETRuntime";

    /// <summary>Event name fired by the runtime for every thrown exception (caught or not).</summary>
    public const string ExceptionThrownEventName = "ExceptionThrown_V1";

    // ExceptionKeyword (0x8000) limits the stream to exception events instead of the
    // full runtime firehose, keeping the causal capture cheap.
    private const long ExceptionKeyword = 0x8000L;
    private const int DefaultSettleMs = 300;
    private const int DefaultDrainMs = 300;

    /// <summary>
    /// Capture the exceptions the target process throws <em>during</em> an action,
    /// turning the time-windowed oracle into a causal one: start an EventPipe
    /// session, let it settle, run <paramref name="action"/>, drain briefly, then
    /// stop. Returns the <see cref="ExceptionThrownEventName"/> events observed in
    /// that window (caught exceptions included — the runtime emits them too).
    /// </summary>
    public IReadOnlyList<DiagnosticEvent> CaptureExceptionsDuring(
        int processId,
        Action action,
        int maxEvents = DefaultMaxEvents,
        int settleMs = DefaultSettleMs,
        int drainMs = DefaultDrainMs)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (maxEvents <= 0 || maxEvents > MaxAllowedEvents)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEvents),
                $"maxEvents must be between 1 and {MaxAllowedEvents}.");
        }

        var client = new DiagnosticsClient(processId);
        EventPipeProvider[] providers = new[]
        {
            new EventPipeProvider(
                name: DotNetRuntimeProvider,
                eventLevel: System.Diagnostics.Tracing.EventLevel.Informational,
                keywords: ExceptionKeyword),
        };

        var captured = new ConcurrentQueue<DiagnosticEvent>();
        var capturedCount = 0;

        using EventPipeSession session = client.StartEventPipeSession(providers, requestRundown: false);
        using var source = new EventPipeEventSource(session.EventStream);

        // Microsoft-Windows-DotNETRuntime is a manifest provider; over EventPipe
        // the Dynamic parser cannot decode its payloads. The strongly-typed CLR
        // parser knows the manifest and surfaces ExceptionType/ExceptionMessage.
        ClrTraceEventParser clrParser = new(source);
        clrParser.ExceptionStart += exceptionData =>
        {
            if (Volatile.Read(ref capturedCount) >= maxEvents)
            {
                return;
            }
            Dictionary<string, object?> payload = new(StringComparer.Ordinal)
            {
                ["ExceptionType"] = exceptionData.ExceptionType,
                ["ExceptionMessage"] = exceptionData.ExceptionMessage,
                ["ExceptionHRESULT"] = exceptionData.ExceptionHRESULT,
                ["ExceptionFlags"] = exceptionData.ExceptionFlags.ToString(),
            };
            captured.Enqueue(new DiagnosticEvent(
                TimestampUtc: exceptionData.TimeStamp.ToUniversalTime(),
                ProviderName: exceptionData.ProviderName,
                EventName: ExceptionThrownEventName,
                EventId: (int)exceptionData.ID,
                Level: exceptionData.Level.ToString(),
                Payload: payload));
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

        // Let the session begin receiving before the action runs, so exceptions at
        // the very start are not missed at the boundary.
        Thread.Sleep(settleMs);
        try
        {
            action();
        }
        finally
        {
            // Let the last in-flight exception events flush before stopping.
            Thread.Sleep(drainMs);
            try
            {
                session.Stop();
            }
            catch (ServerNotAvailableException)
            {
                // Target process exited mid-collection.
            }
            processingTask.Wait(TimeSpan.FromSeconds(2));
        }

        return captured.ToArray();
    }

    public CounterSnapshot ObserveCounters(int processId, string providerName, int durationMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ValidateDuration(durationMs);

        var client = new DiagnosticsClient(processId);
        EventPipeProvider[] providers = new[]
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

        using EventPipeSession session = client.StartEventPipeSession(providers, requestRundown: false);
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
        EventPipeProvider[] providers = providerNames
            .Select(name => new EventPipeProvider(
                name: name,
                eventLevel: System.Diagnostics.Tracing.EventLevel.Informational,
                keywords: 0xFFFFFFFFL))
            .ToArray();

        var captured = new ConcurrentQueue<DiagnosticEvent>();
        var capturedCount = 0;
        var truncated = false;

        using EventPipeSession session = client.StartEventPipeSession(providers, requestRundown: false);
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
