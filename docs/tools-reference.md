# Tools reference

This document describes the MCP tools exposed by `Winsight.Mcp` to AI agents.
Every tool returns a JSON string. Errors — whether from a known failure mode
or an unexpected exception inside the tool body — are uniformly surfaced
inside an `OperationResult` shape (`{ "success": false, "error": "..." }`)
rather than as MCP exceptions, so an agent can read and recover from them.
A universal `ToolSafety` guard at the tool boundary catches anything the
implementation throws and converts it into that shape, so a single rogue
exception cannot tear down the host process or kill the rest of the agent's
session.

## `list_windows`

Enumerate top-level windows currently on the desktop.

**Parameters** — none.

**Returns** — JSON array of `WindowDescriptor`:

```json
[
  {
    "processId": 14820,
    "windowHandle": 1247868,
    "title": "Winsight Sample - Counter",
    "processName": "SampleWpfApp",
    "isForeground": true,
    "x": 750, "y": 400, "width": 420, "height": 280
  }
]
```

## `inspect_ui_tree`

Walk the UI Automation tree of a process's main window.

**Parameters**

| Name        | Type | Default | Description                           |
| ----------- | ---- | ------- | ------------------------------------- |
| `processId` | int  | —       | Process id obtained from list_windows |
| `maxDepth`  | int  | 5       | Tree depth, capped at 20              |

**Returns** — JSON `UiElementNode` (recursive):

```json
{
  "automationId": "MainWindow",
  "name": "Winsight Sample - Counter",
  "controlType": "Window",
  "className": "Window",
  "isEnabled": true,
  "isVisible": true,
  "x": 750, "y": 400, "width": 420, "height": 280,
  "children": [
    {
      "automationId": "IncrementButton",
      "name": "Increment",
      "controlType": "Button",
      "...": "..."
    }
  ]
}
```

## `click_element`

Locate an element and trigger its primary action.

**Parameters**

| Name           | Type   | Required | Description                              |
| -------------- | ------ | -------- | ---------------------------------------- |
| `processId`    | int    | yes      | Target process id                        |
| `automationId` | string | one of   | UIA AutomationId                         |
| `name`         | string | one of   | UIA Name                                 |
| `className`    | string | one of   | UIA ClassName                            |

At least one of `automationId`, `name`, or `className` must be set.

**Resolution order**: Invoke pattern → Toggle pattern → mouse click on center.

**Returns** — `OperationResult`:

```json
{ "success": true }
```

or

```json
{ "success": false, "error": "Element not found for locator ..." }
```

## `type_text`

Locate an element and write text into it.

**Parameters**

| Name           | Type   | Required | Description                              |
| -------------- | ------ | -------- | ---------------------------------------- |
| `processId`    | int    | yes      | Target process id                        |
| `text`         | string | yes      | Text to send                             |
| `automationId` | string | one of   | UIA AutomationId                         |
| `name`         | string | one of   | UIA Name                                 |
| `className`    | string | one of   | UIA ClassName                            |

**Resolution order**: Value pattern → focus + keyboard.Type.

**Returns** — `OperationResult` (same shape as `click_element`).

## `read_managed_state`

Attach to a managed process and read a field on the first heap instance of
the requested type.

**Parameters**

| Name        | Type   | Description                                                    |
| ----------- | ------ | -------------------------------------------------------------- |
| `processId` | int    | Target process id                                              |
| `typeName`  | string | Fully qualified .NET type name (namespace + class)             |
| `fieldName` | string | Field name on the type, including backing-field naming         |

**Returns** — `OperationResult` with `data` carrying the field value:

```json
{ "success": true, "data": 42 }
```

For reference fields, `data` is a string of the form
`"Type.Name @ 0xADDRESS"` (a hex pointer). Primitive value types come back
as native JSON numbers / booleans / strings.

**Important constraints**

- The MCP server process **must** match the target's bitness (x64 here).
- Attach is non-suspending. The target keeps running during inspection, so
  the field's value is read at one point in time and may have been mutated
  by the time the agent sees the result.
- ClrMD does not evaluate getters or properties. To read `MyVm.Count` where
  `Count` is `get => _internalCount;`, query the backing field
  `_internalCount` directly.

## `capture_screenshot`

Capture a PNG of the target process's main window using `PrintWindow` with
`PW_RENDERFULLCONTENT`. This flag makes DirectX-composed WPF windows and
other accelerated surfaces render correctly into the bitmap; without it the
result is often a black rectangle.

**Parameters**

| Name         | Type   | Required | Description                                            |
| ------------ | ------ | -------- | ------------------------------------------------------ |
| `processId`  | int    | yes      | Target process id                                      |
| `outputPath` | string | no       | Absolute file path. Default: `%TEMP%\winsight\screenshot-{utc-timestamp}.png` |

**Returns** — `OperationResult` wrapping a `ScreenshotResult` payload:

```json
{
  "success": true,
  "data": {
    "path": "C:\\Users\\me\\AppData\\Local\\Temp\\winsight\\screenshot-20260509-143012-457.png",
    "width": 420,
    "height": 280,
    "capturedAtUtc": "2026-05-09T14:30:12.4571234Z"
  }
}
```

On failure (window minimized, `PrintWindow` returned false, output path
not writable, target requires administrator and the host does not, …):

```json
{
  "success": false,
  "error": "PrintWindow returned false (Win32 error 0x00000005=5). Window may be minimized, occluded, or use a composition surface that does not support PrintWindow (e.g. WinUI3 with DesktopChildSiteBridge)."
}
```

**Notes**

- Rendering occurs synchronously. Latency is dominated by the window's
  redraw cost (a few ms for typical WPF windows).
- The capture covers the window's bounding rectangle including non-client
  area (title bar, borders).
- Minimized windows, occluded windows, and modern WinUI3 composition
  surfaces (Win11 Notepad, recent Settings panes) may fail `PrintWindow`.
  The error message includes the Win32 error code so the agent can decide
  whether to retry, restore the window, or fall back to a different
  capture strategy.

## `detect_webview2_endpoints`

Walk the descendant process tree of the host pid, find every
`msedgewebview2.exe`, and report whether each one was launched with the
`--remote-debugging-port` flag (Chrome DevTools Protocol). When a port is
exposed and listening, an MCP-driving agent can then attach Playwright or
Puppeteer to `http://localhost:{port}` to drive the embedded web content
directly.

**Parameters**

| Name            | Type | Required | Description                          |
| --------------- | ---- | -------- | ------------------------------------ |
| `hostProcessId` | int  | yes      | The WPF/Win32 host pid (from list_windows) |

**Returns** — JSON object:

```json
{
  "endpoints": [
    {
      "hostProcessId": 14820,
      "webView2ProcessId": 17240,
      "remoteDebuggingPort": 9222,
      "isPortListening": true,
      "commandLine": "\"...\\msedgewebview2.exe\" --type=... --remote-debugging-port=9222 ..."
    }
  ],
  "hint": null
}
```

When no usable endpoint is found, `hint` carries an actionable message:

| Situation                           | Hint                                                                 |
| ----------------------------------- | -------------------------------------------------------------------- |
| No `msedgewebview2.exe` descendant  | App may not embed WebView2 yet, or it is still initializing.         |
| Found but no `--remote-debugging-port` | The host app must set `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222` (or `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`) before `EnsureCoreWebView2Async`. |
| Flag present but port not listening | Renderer may have crashed or not finished startup.                   |

**Notes**

- Detection uses a single WMI query (`Win32_Process`) and an
  `IPGlobalProperties.GetActiveTcpListeners()` snapshot — fast (~50 ms) and
  safe (no probe traffic on the target ports).
- Listening detection looks at any local interface, not only loopback —
  this matches WebView2's binding behavior.

## `observe_counters`

Subscribe to .NET `EventCounters` from a managed process via EventPipe (the
official Microsoft diagnostic IPC, the same channel `dotnet-counters` and
`dotnet-trace` use). Use this as a **non-intrusive oracle**: read counter
values before and after an action to assert behavior without touching the
target process's memory or code.

**Why prefer this over `read_managed_state` in enterprise environments**:
EventPipe IPC is invisible to EDR products (CrowdStrike, SentinelOne,
Microsoft Defender for Endpoint). CLRMD attach uses
`CreateRemoteThread`/`WriteProcessMemory` patterns that match malware
heuristics.

**Parameters**

| Name           | Type   | Default            | Description                              |
| -------------- | ------ | ------------------ | ---------------------------------------- |
| `processId`    | int    | —                  | Managed target process id                |
| `providerName` | string | `System.Runtime`   | EventCounter provider                    |
| `durationMs`   | int    | 2000               | Observation window. Min 500, max 30000   |

**Returns** — JSON `CounterSnapshot`:

```json
{
  "processId": 14820,
  "providerName": "System.Runtime",
  "durationMs": 2000,
  "samplesObserved": 38,
  "counters": {
    "cpu-usage": 1.4,
    "working-set": 134217728,
    "gc-heap-size": 8388608,
    "gen-0-gc-count": 0,
    "exception-count": 0,
    "threadpool-thread-count": 12,
    "monitor-lock-contention-count": 0,
    "active-timer-count": 3,
    "alloc-rate": 152384
  }
}
```

**Common providers**

| Provider                            | Counters of interest                                      |
| ----------------------------------- | --------------------------------------------------------- |
| `System.Runtime` (default)          | cpu-usage, gc-heap-size, alloc-rate, exception-count, threadpool-thread-count |
| `Microsoft.AspNetCore.Hosting`      | requests-per-second, current-requests, failed-requests    |
| `System.Net.Http`                   | requests-started-rate, requests-failed-rate, http3.0-connections-current-total |
| `Microsoft-AspNetCore-Server-Kestrel` | connections-per-second, total-connections                |
| any custom `EventSource`            | counters declared via `EventCounter` / `IncrementingEventCounter` |

**Constraints**

- Target must be .NET Core 3.0+ / .NET 5+ / .NET 6+. Not supported on .NET
  Framework 4.x.
- Same user account as the target (or admin).
- The first ~1 second of the window may return empty counters because the
  runtime needs one interval to emit them. Default 2000 ms window is the
  minimum that reliably yields a full set.
- The `Counters` map records the **last** observed value per counter
  during the window. `samplesObserved` reports how many counter events
  arrived in total, useful to detect a process that died mid-collection.

## `capture_events`

Capture EventPipe events from one or more providers in a managed process.
Useful for observing every exception thrown, every custom `EventSource`
event, or any other diagnostic stream — without injecting code into the
target.

**Parameters**

| Name              | Type     | Default | Description                                              |
| ----------------- | -------- | ------- | -------------------------------------------------------- |
| `processId`       | int      | —       | Managed target process id                                |
| `providers`       | string[] | —       | EventSource provider names                               |
| `durationMs`      | int      | 3000    | Capture window. Min 500, max 30000                       |
| `maxEvents`       | int      | 500     | Hard cap on events kept (1..10000). Sets `truncated` if hit |
| `eventNameFilter` | string?  | null    | Exact-match (case-sensitive) event name filter           |

**Returns** — JSON `EventCaptureResult`:

```json
{
  "processId": 14820,
  "providers": ["Microsoft-Windows-DotNETRuntime"],
  "durationMs": 3000,
  "totalCaptured": 2,
  "truncated": false,
  "events": [
    {
      "timestampUtc": "2026-05-09T14:30:12.4571234Z",
      "providerName": "Microsoft-Windows-DotNETRuntime",
      "eventName": "ExceptionThrown_V1",
      "eventId": 80,
      "level": "Warning",
      "payload": {
        "ExceptionType": "System.InvalidOperationException",
        "ExceptionMessage": "Sequence contains no elements",
        "ExceptionEIP": "0x7ffabc123456",
        "ExceptionHRESULT": -2147467261,
        "ExceptionFlags": 16,
        "ClrInstanceID": 9
      }
    }
  ]
}
```

**Common patterns**

| Goal | Providers | eventNameFilter |
| ---- | --------- | --------------- |
| Every exception thrown | `["Microsoft-Windows-DotNETRuntime"]` | `"ExceptionThrown_V1"` |
| Task lifecycle events | `["System.Threading.Tasks.TplEventSource"]` | (none) |
| HTTP client requests | `["System.Net.Http"]` | (none) |
| Custom app telemetry | `["MyCompany.MyApp"]` | (specific event name) |

**Notes**

- Payloads are normalized to JSON-safe types: primitives pass through,
  `DateTime` is converted to UTC, `Guid` and enums become strings, byte
  arrays become hex, and dictionaries/enumerables recurse with a depth
  cap of 4.
- High-rate providers (`Microsoft-Windows-DotNETRuntime` with all
  keywords) can flood the cap quickly. Use `eventNameFilter` to narrow
  the stream.
- Same .NET Core 3.0+ / process-bitness constraints as `observe_counters`.

## `find_element_visually`

Locate a UI element from a natural-language description. The tool
captures a screenshot of the target window, submits it together with the
query to a vision model, and returns the bounding box. Use this when
`inspect_ui_tree` returns no useful AutomationId (custom Canvas,
SkiaSharp, DirectX surfaces, or any content UIA cannot see).

**Parameters**

| Name        | Type   | Required | Description                                      |
| ----------- | ------ | -------- | ------------------------------------------------ |
| `processId` | int    | yes      | Target process id (from list_windows)            |
| `query`     | string | yes      | Natural-language description of the target       |

**Returns** — JSON `GroundedElement`:

```json
{
  "x": 245,
  "y": 178,
  "width": 96,
  "height": 32,
  "confidence": 0.94,
  "reasoning": "Button labeled 'Increment' centered in the lower stack panel.",
  "source": "claude-sonnet-4-6"
}
```

`x` and `y` are the **center** of the bounding box, in **screenshot
pixel space** (origin at the top-left of the captured window). To
convert to screen coordinates, add the window's origin from
`list_windows`.

If the call fails — missing API key, network error, or unparseable model
output — the tool returns an `OperationResult` failure object instead:

```json
{ "success": false, "error": "ANTHROPIC_API_KEY environment variable is not set. ..." }
```

**Configuration** (environment variables)

| Variable                  | Default                                  | Purpose                          |
| ------------------------- | ---------------------------------------- | -------------------------------- |
| `ANTHROPIC_API_KEY`       | —                                        | Required                         |
| `WINSIGHT_VISION_MODEL`   | `claude-sonnet-4-6`                      | Override the model               |
| `WINSIGHT_VISION_API_URL` | `https://api.anthropic.com/v1/messages`  | Override the endpoint (proxies)  |

**Privacy** — screenshots are transmitted to the configured API. For
regulated or offline scenarios, wait for the planned `OnnxLocalGrounder`
that will run ShowUI-π locally; the contract (`IElementGrounder`) is
already in place so the tool surface stays identical.

