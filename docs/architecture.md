# Architecture

Winsight is layered so that each capability is independently usable, replaceable,
and testable. The MCP server is a thin adapter; the value lives in the engine.

## Layers

```
┌─────────────────────────────────────────────────────────┐
│  AI Agent (Claude Code / Cursor / custom)               │
└─────────────────────────────────────────────────────────┘
                         │  MCP (stdio)
                         ▼
┌─────────────────────────────────────────────────────────┐
│  Winsight.Mcp                                           │
│   - tool definitions (Tools/*.cs)                       │
│   - ToolSafety outer guard (Tools/ToolSafety.cs)        │
│   - JSON serialization (Serialization/JsonOptions.cs)   │
│   - hosting (Program.cs)                                │
└─────────────────────────────────────────────────────────┘
                         │  in-process
                         ▼
┌─────────────────────────────────────────────────────────┐
│  Winsight.Core                                          │
│   • Models        — pure data records (DTOs)            │
│   • UiAutomation  — FlaUI UIA3 finder / BFS / actions   │
│   • ManagedState  — CLRMD non-suspending heap walk      │
│   • Capture       — PrintWindow PW_RENDERFULLCONTENT    │
│   • WebView2      — WMI process tree + CDP detection    │
│   • Diagnostics   — EventPipe counters + events         │
│   • Vision        — IElementGrounder + vision backends  │
└─────────────────────────────────────────────────────────┘
                         │  Win32 / .NET diagnostic APIs
                         ▼
┌─────────────────────────────────────────────────────────┐
│  Target application (any WPF / Win32 / WebView2 app)    │
└─────────────────────────────────────────────────────────┘
```

## Module responsibilities

### Winsight.Core/Models

Pure data records. No behavior, no IO.

- `WindowDescriptor` — top-level window snapshot.
- `UiElementNode` — a single node in a recursive UIA tree.
- `ElementLocator` — input criteria for finding an element (exact id/name/
  className, case-insensitive controlType and nameContains, index pick).
- `ElementCandidate` — compact element descriptor reported on ambiguous
  or failed lookups.
- `ElementState` — UI-level state snapshot returned by `get_element_state`
  (value, toggle, selection, focus, geometry).
- `WaitCondition` + `WaitOutcome` — condition enum and result payload of
  `wait_for_element`; `ClickStyle` — click variants for `click_element`.
  Both enums ship wire-format parsers (`exists`/`not_exists`/…,
  `single`/`double`/`right`).
- `OperationResult` — uniform `{success, error, data}` envelope used by
  every tool's failure path (and by several tools' success path too).
- `ScreenshotResult` — path / dimensions / capture timestamp returned by
  `capture_screenshot`.
- `WebView2Endpoint` — descendant WebView2 process descriptor with the
  CDP port it was launched with (if any) and whether the port is
  actually listening.
- `CounterSnapshot` — output of `observe_counters`: provider, duration,
  sample count, and a `counters` map of counter-name to last observed
  value.
- `DiagnosticEvent` + `EventCaptureResult` — output of `capture_events`:
  per-event metadata (timestamp, provider, name, level, normalized
  payload) bounded by a configurable cap with a `truncated` flag.
- `GroundedElement` — bounding box, confidence, and reasoning returned
  by `find_element_visually`.

### Winsight.Core/Modules/UiAutomation

Wraps FlaUI (UIA3):

- `WindowDiscovery` — enumerates top-level windows and resolves process metadata.
  Uses `GetForegroundWindow` for the foreground flag.
- `UiTreeInspector` — bounded BFS traversal of the UIA tree for a given process
  window (default depth 5, capped at 20), with optional window-title scoping
  and controlType/nameContains pruning; nodes carry Value-pattern content.
- `NativeWindowEnumerator` (internal) — Win32 `EnumWindows` listing of the
  visible top-level windows of a process. Required because UIA reparents
  owned windows (modal dialogs) under their owner and omits them from both
  desktop and owner child enumerations; rooting searches at Win32 handles
  makes every window addressable.
- `LocatorMatcher` — pure matching semantics between an `ElementLocator`
  and an element snapshot (exact, substring, near-miss), unit-testable
  without live UIA.
- `ElementFinder` (internal) — strict single-element resolution across all
  windows of the process: fails with candidate listings on ambiguity (no
  silent first-match), suggests near misses when nothing matches, and
  tolerates `COMException` from elements tearing down mid-walk.
- `ActionInvoker` — clicks (Invoke / Toggle / mouse fallback; double and
  right click variants), types (writable Value pattern / keyboard
  fallback), selects items in ComboBox/ListBox/Tab containers, and scrolls
  off-screen elements into view before physical actions.
- `ElementStateReader` — builds `ElementState` snapshots from located
  elements (the UI-level oracle behind `get_element_state`).
- `ElementWaiter` — polls a locator until a `WaitCondition` holds or a
  timeout elapses (`UiaDefaults` carries the defaults and caps).
- `UiaDefaults` — centralized timeouts, poll intervals, and candidate
  caps; wait timeout overridable via `WINSIGHT_UIA_WAIT_TIMEOUT_MS`.

### Winsight.Core/Modules/ManagedState

Wraps Microsoft.Diagnostics.Runtime (CLRMD).

- `ManagedStateReader` — attaches non-suspending to a managed process, walks
  the heap, finds the first instance of the requested type, and reads a named
  field. Supports primitive element types and reference summaries.

The host process **must** match the target architecture (x64 by default in
this repo). Cross-bitness inspection is not supported.

### Winsight.Core/Modules/Capture

Window screenshot capture via the Win32 `PrintWindow` API with the
`PW_RENDERFULLCONTENT` flag (Windows 8.1+).

- `WindowCapture` — locates a top-level window by process id, allocates a
  matching `Bitmap`, calls `PrintWindow`, and saves a PNG to the requested
  path.

The `PW_RENDERFULLCONTENT` flag is what lets the call render DirectX-composed
WPF windows correctly. Without it, accelerated surfaces come back as black
rectangles.

### Winsight.Core/Modules/WebView2

Discovers embedded WebView2 instances and their Chrome DevTools Protocol
endpoints, so AI agents can attach Playwright/Puppeteer over CDP.

- `WebView2Detector` — single-query WMI walk of `Win32_Process`, traverses
  the descendant tree from a host pid, and reports each `msedgewebview2.exe`
  child along with any `--remote-debugging-port=N` it was launched with.
  Cross-references with `IPGlobalProperties.GetActiveTcpListeners()` to
  confirm whether the port is actually listening.
- `WebView2CommandLineParser` — pure-function helper that extracts the
  debugging port from a process command line. Easy to unit-test.

This module does not start or modify the WebView2 environment. Enabling CDP
must happen in the host app code (via
`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` or
`CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`); the detector
just reports what is already there and emits an actionable hint when nothing
usable is found.

### Winsight.Core/Modules/Diagnostics

Non-intrusive observability of managed (.NET 6+) processes via the
official EventPipe IPC — the same channel `dotnet-counters` and
`dotnet-trace` use. Crucially, this is **not** raw ETW: no admin
privileges required, no `CreateRemoteThread` heuristics that EDR products
flag, and no source code changes in the target.

- `DiagnosticsObserver.ObserveCounters` — opens an EventPipe session for
  one provider with `EventCounterIntervalSec=1`, listens for
  `EventCounters` events for a bounded window (500ms–30s), aggregates the
  last observed `Mean` (averaging counters) or `Increment` (monotonic
  counters) value per counter name, and returns the snapshot.
- `DiagnosticsObserver.CaptureEvents` — opens an EventPipe session
  spanning one or more providers, captures every event (or only those
  matching an exact name filter), normalizes payloads to JSON-safe
  representations, and returns a bounded list with a `Truncated` flag
  when the cap is reached. Default cap is 500 events; max 10,000.
- `PayloadNormalizer` — pure-function helper that converts arbitrary
  TraceEvent payload values into a JSON-friendly representation
  (primitives pass through, `DateTime` is converted to UTC, `Guid` and
  enums become strings, byte arrays become hex, dictionaries and
  enumerables recurse with a depth cap of 4). Easy to unit-test.

The observer is the recommended oracle for two reasons it beats CLRMD:

1. EDR-safe in enterprise environments. CLRMD attach uses
   `CreateRemoteThread`/`WriteProcessMemory` heuristics that match
   malware patterns and trip CrowdStrike Falcon, SentinelOne, and
   Microsoft Defender for Endpoint. EventPipe IPC does not.
2. Time-series semantic. Counters give you allocation rate, exception
   rate, GC frequency, thread pool growth — useful for "did this scenario
   leak?" oracles that a single point-in-time field read cannot answer.

CLRMD remains the right tool when you need to read a specific field on a
specific instance, especially during a single deterministic test step.

### Winsight.Core/Modules/Vision

Locates UI elements that UIA cannot see (custom Canvas, SkiaSharp,
DirectX surfaces, WebView2 content) by submitting a window screenshot
plus a natural-language query to a vision model and reading back a
bounding box.

- `IElementGrounder` — the contract: `FindAsync(screenshotPath, query, ct)`
  returns a `GroundedElement` (center coordinates, bounding box,
  confidence, reasoning, source).
- `AnthropicVisionGrounder` — default implementation. Reads
  `ANTHROPIC_API_KEY` from the environment, calls the Anthropic Messages
  API with the screenshot encoded as base64 and a tightly-constrained
  prompt that asks for JSON-only output. Default model is
  `claude-sonnet-4-6`; override via `WINSIGHT_VISION_MODEL`. The endpoint
  itself can be overridden via `WINSIGHT_VISION_API_URL` for self-hosted
  proxies or routing through a corporate gateway.
- `AnthropicResponseParser` — extracts the model's JSON object from the
  response envelope, robust to wrapping prose, markdown code fences,
  numeric strings, and braces inside reasoning text.
- `OnnxLocalGrounder` — selectable with `WINSIGHT_VISION_BACKEND=onnx`. A fully
  local backend running Qwen2.5-VL-3B as a 3-ONNX split (vision encoder + token
  embedding + decoder) on plain ONNX Runtime with a hand-rolled mRoPE generation
  loop. It validates `WINSIGHT_VISION_ONNX_PATH` and `WINSIGHT_VISION_EP=cpu|dml`,
  fails explicitly when misconfigured, and never falls back to the cloud.
  Execution is CPU-only in this build (the decoder's fused MultiHeadAttention op
  is not yet supported on the DirectML EP — a tracked follow-up).
- `QwenImagePreprocessing` / `QwenVisionTokenizer` — C# reimplementations of the
  Qwen2.5-VL smart-resize + patchify and the byte-level BPE tokenizer (via
  `CodeGenTokenizer` over a vocab patched with Qwen special tokens).
- `GroundingCoordinateParser` — turns the model's textual point `[x,y]` or box
  `[x1,y1,x2,y2]` output (absolute pixels in the resized image space) into one
  click center; the grounder denormalizes it to original-image pixels and stores
  it in `GroundedElement` as a zero-area box.

#### Why a cloud vision API first

The cloud backend ships by default for zero-setup grounding. The local ONNX
backend is opt-in and carries provisioning cost: a multi-gigabyte model
(downloaded out of the repo) and C#-side preprocessing/tokenization. See
[docs/local-onnx-grounder.md](local-onnx-grounder.md).

Going through the Anthropic API gets the agent visual grounding today,
with the trade-off that screenshots leave the host. The interface
(`IElementGrounder`) is the forward-compatibility hedge: when the local
ONNX path is proven and matters more than cloud convenience (regulated
content, offline CI, high-volume runs), `OnnxLocalGrounder` can grow
behind the same contract without touching any tool surface.

### Winsight.Mcp

A `Microsoft.Extensions.Hosting` host that registers the engine modules as
singletons, then plugs them into `AddMcpServer().WithStdioServerTransport()`.
Tools are discovered by reflection from any `[McpServerToolType]` class in
the assembly.

Logging is forced to stderr — stdout is reserved for the MCP protocol.

## Threading and lifetime

- Each tool call constructs and disposes its own `UIA3Automation` instance.
  This keeps tools stateless and avoids COM apartment surprises across
  parallel MCP requests.
- CLRMD's `DataTarget` is similarly scoped per call. Attach is non-suspending
  (`suspend: false`) to avoid GC interaction issues on .NET 9 / .NET 10.

## Tool boundary safety

Every tool method on `Winsight.Mcp` is wrapped by `ToolSafety.SafeInvoke`
(or `SafeInvokeAsync` for the vision tool). The guard:

- Catches every non-fatal exception thrown inside the tool body and
  returns a structured `OperationResult { success: false, error }` JSON
  the agent can read and recover from.
- Mirrors the original exception (with full stack) to **stderr** so a
  human attaching to the host process can see what happened. Stdout
  remains reserved for MCP protocol traffic.
- Lets truly fatal exceptions propagate (`OutOfMemoryException`,
  `StackOverflowException`, `ThreadAbortException`) — masking those
  would hide a process-wide failure.

The reason this guard exists rather than relying on the MCP framework's
own error handling: a single unhandled exception inside any tool (a
`COMException` from a UIA call, a CLRMD attach failure on an
inaccessible process) was observed to **terminate the .NET host
process**, which in turn closes the stdio transport and renders every
other tool permanently unusable for the rest of the agent's session.
The universal guard pins the failure to the calling tool only.

## Failure modes and how they are reported

| Situation                                 | Behavior                                              |
| ----------------------------------------- | ----------------------------------------------------- |
| Window has no name                        | Skipped from `list_windows` (kept implicit)           |
| COM exception on a single window          | Caught locally, that window is skipped                |
| Element not found                         | `OperationResult.Failure(...)` (success = false)      |
| Process has no managed runtime            | `OperationResult.Failure(...)`                        |
| Type/field not found in heap              | `OperationResult.Failure(...)`                        |
| Architecture / elevation mismatch         | CLRMD attach throws; ToolSafety wraps it as `OperationResult.Failure(message)` and the host stays up |
| Any other unhandled exception in a tool   | ToolSafety wraps it as `OperationResult.Failure(message)`, stderr-logs the full stack, returns to caller |

## What is intentionally absent at this point

- GPU acceleration for the local ONNX grounder. The local Qwen2.5-VL backend
  ships (CPU); DirectML/CUDA execution is a follow-up (the decoder's fused
  MultiHeadAttention op is not yet supported on the DirectML EP). See
  [local-onnx-grounder.md](local-onnx-grounder.md).
- Visual regression (Anomalib / PatchCore) — Phase 2 backlog.
- Process-injection-based instrumentation (DLL injection, MinHook).
- DPI-aware coordinate mapping for multi-monitor setups.
- D3D11 `Present` hook for capturing surfaces UIA cannot see.
- Cross-elevation UIA / CLRMD inspection. A standard-user winsight host
  cannot inspect or attach to processes that ship
  `<requestedExecutionLevel level="requireAdministrator" />`. The tool
  failures surface cleanly through `ToolSafety` but the operations
  themselves cannot succeed. To drive elevated apps, run the winsight
  host at the same or higher integrity level.

These are tracked in the README capability matrix.
