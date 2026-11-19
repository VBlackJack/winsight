# Architecture

*[Version française](fr/architecture.md)*

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
│   • Models        - pure data records (DTOs)            │
│   • UiAutomation  - FlaUI UIA3 finder / BFS / actions   │
│   • Lifecycle     - launch and close applications       │
│   • Orchestration - run_steps batches and guards        │
│   • ManagedState  - CLRMD non-suspending heap walk      │
│   • Capture       - PrintWindow PW_RENDERFULLCONTENT    │
│   • WebView2      - WMI process tree + CDP detection    │
│   • Diagnostics   - EventPipe counters + events         │
│   • Vision        - IElementGrounder + vision backends  │
│   • VisualRegression - baseline diffs and visual waits  │
│   • UserActivity  - shared-desktop input attribution    │
│   • Clipboard     - text, save and restore, private     │
│   • Ocr           - Windows.Media.Ocr text reading      │
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

- `WindowDescriptor`: top-level window snapshot.
- `UiElementNode`: a single node in a recursive UIA tree.
- `ElementLocator`: input criteria for finding an element (exact id/name/
  className, case-insensitive controlType and nameContains, index pick).
- `ElementCandidate`: compact element descriptor reported on ambiguous
  or failed lookups.
- `ElementState`: UI-level state snapshot returned by `get_element_state`
  (value, toggle, selection, focus, geometry).
- `WaitCondition` + `WaitOutcome`: condition enum and result payload of
  `wait_for_element`; `ClickStyle`: click variants for `click_element`.
  Both enums ship wire-format parsers (`exists`/`not_exists`/...,
  `single`/`double`/`right`).
- `OperationResult`: uniform `{success, error, data}` envelope used by
  every tool's failure path (and by several tools' success path too).
- `ScreenshotResult`: path / dimensions / capture timestamp returned by
  `capture_screenshot` (whose tool also attaches the PNG inline as an MCP image).
- `WebView2Endpoint`: descendant WebView2 process descriptor with the
  CDP port it was launched with (if any) and whether the port is
  actually listening.
- `CounterSnapshot`: output of `observe_counters`: provider, duration,
  sample count, and a `counters` map of counter-name to last observed
  value.
- `DiagnosticEvent` + `EventCaptureResult`: output of `capture_events`:
  per-event metadata (timestamp, provider, name, level, normalized
  payload) bounded by a configurable cap with a `truncated` flag.
- `GroundedElement`: bounding box, confidence, and reasoning returned
  by `find_element_visually`.
- `UserActivitySnapshot` and `ForegroundWindowInfo`: output of
  `get_user_activity` (human idle time, last input source, foreground window
  with its title withheld outside the target, locked session).
- `ClipboardTextResult` and `TypeKeysResult`: clipboard text and length, and
  what `type_keys` sent (mode, window, focused element, clipboard restore).
- `WindowRegion`: a rectangle in window coordinates, shared by the visual
  waits, OCR and capture cropping.
- `VisualTextResult`, `VisualTextLine`, `VisualTextWord`: OCR output with
  rectangles in window coordinates.

### Winsight.Core/Modules/UiAutomation

Wraps FlaUI (UIA3):

- `WindowDiscovery`: enumerates top-level windows through Win32 `EnumWindows`
  (so owned dialogs, popups, menus, and tooltips are discoverable, not just
  reachable), classifies each via `WindowClassifier`, and resolves process
  metadata. Optional per-process scoping returns the full window stack of one
  app, including untitled surfaces. Uses `GetForegroundWindow` for the
  foreground flag.
- `WindowClassifier`: pure classification of a window into a kind
  (main/dialog/popup/menu/tooltip/other) plus modality, from its Win32 class
  name, caption/popup styles, and ownership; unit-testable without live windows.
- `WindowGeometry`: resolves a process window's screen-space bounds (topmost,
  or title-scoped), mirroring `WindowCapture`'s window selection.
- `WindowPointMapper`: pure window-relative ↔ screen coordinate translation and
  bounds checks; backs `click_point` and the screen coordinates enriched onto
  `find_element_visually` results.
- `UiTreeInspector`: bounded BFS traversal of the UIA tree for a given process
  window (default depth 5, capped at 20), with optional window-title scoping
  and controlType/nameContains pruning; nodes carry Value-pattern content.
- `AffordanceScanner`: the perception layer behind `describe_ui`: walks the
  process windows breadth-first, keeps interactable control types, reuses
  `ElementStateReader.BuildState` for each, and pairs them with
  `AffordanceRecommender` (a pure function that picks the smallest unique
  locator) to return a compact, position-ordered affordance list.
- `UiSnapshotter` / `UiDiffer`: the feedback sense behind `snapshot_ui` /
  `diff_ui`: `UiSnapshotter` captures a `UiSnapshot` (windows + affordances) by
  composing `WindowDiscovery` and `AffordanceScanner`; `UiDiffer` is a pure
  function that diffs two snapshots into a `UiChange` (windows matched by handle,
  elements by a stable key). The MCP layer keeps the per-process baseline in a
  session-scoped `UiSnapshotStore`.
- `NativeWindowEnumerator` (internal): Win32 `EnumWindows` listing of the
  visible top-level windows of a process. Required because UIA reparents
  owned windows (modal dialogs) under their owner and omits them from both
  desktop and owner child enumerations; rooting searches at Win32 handles
  makes every window addressable.
- `LocatorMatcher`: pure matching semantics between an `ElementLocator`
  and an element snapshot (exact, substring, near-miss), unit-testable
  without live UIA.
- `ElementFinder` (internal): strict single-element resolution across all
  windows of the process: fails with candidate listings on ambiguity (no
  silent first-match), suggests near misses when nothing matches, and
  tolerates `COMException` from elements tearing down mid-walk.
- `ActionInvoker`: clicks (Invoke / Toggle / mouse fallback; double and
  right click variants), coordinate clicks (`ClickPoint`, window- or
  screen-space, the action partner of visual grounding), types (writable Value
  pattern / keyboard fallback), selects items in ComboBox/ListBox/Tab
  containers, and scrolls off-screen elements into view before physical actions.
  Coordinate actions (`ClickPoint`, `ScrollPoint`, `HoverPoint`) share one path:
  resolve the window, optionally raise it (`focusWindow`), validate the point
  recipient, then hand the point to `PointerDispatch`. `TypeKeys` types into the
  foreground window of the target without a UIA element, as keystrokes or as a
  paste that restores the clipboard.
- `PointerDispatch`: every physical pointer action in three beats: move, rest
  for `settleMs` (default 150 ms), read the pointer back (`cursor_moved` when
  other input moved it), revalidate the recipient, then press, turn the wheel or
  hold without moving again. Immediate-mode UIs (iced on winit) resolve hover
  once per frame from the last `WM_MOUSEMOVE`; a move and a press sent together
  act on the previous position. `IPointerDriver` lets unit tests record the
  sequence.
- `TextTyper`: plans text as `KEYEVENTF_UNICODE` strokes (Enter and Tab as
  keys, surrogate pairs as two units) and sends them paced, checking before each
  stroke that the target still owns the foreground.
- `DesktopInputLease`: session-wide mutex that serializes physical input
  across Winsight processes. The outermost lease also enforces the
  yield-to-user policy and records its injection window in the input ledger
  (see UserActivity).
- `ElementStateReader`: builds `ElementState` snapshots from located
  elements (the UI-level oracle behind `get_element_state`).
- `AssertionEvaluator`: pure operator evaluation (equals/contains/greater_than/
  checked/enabled/...) over an `ElementState`, behind the `assert_element` tool and
  the `assert` run_steps step; text operators compare Value, falling back to Name.
- `ElementWaiter`: polls a locator until a `WaitCondition` holds or a
  timeout elapses (`UiaDefaults` carries the defaults and caps).
- `UiaDefaults`: centralized timeouts, poll intervals, and candidate
  caps; wait timeout overridable via `WINSIGHT_UIA_WAIT_TIMEOUT_MS`.

### Winsight.Core/Modules/Lifecycle

- `AppLauncher`: starts an executable and waits until its window is present
  (main window, or one matching a title fragment) before returning a classified
  `LaunchResult`, removing the shell-out-then-poll startup race; also closes a
  process gracefully (`CloseMainWindow`), preserving a still-running application,
  or terminates it only when force is explicitly requested. Reuses
  `WindowDiscovery` to detect the ready window.

### Winsight.Core/Modules/Orchestration

- `StepRunner`: executes a `BatchStep[]` sequence in one pass, dispatching each
  step to the existing action/sync/oracle/capture primitives (no duplicated
  logic). Steps are exception-isolated (one failure becomes that step's result,
  not a batch abort), stop-on-error halts at the first failure, and the run
  returns a `BatchResult` journal. Backs the `run_steps` tool. Before each
  screen-changing step it applies the yield-to-user policy and, unless
  `stopOnForegroundChange` is off, `ForegroundGuard`. When a
  `wait_visual_change` step follows a screen-changing step, the runner captures
  the reference image before that step, so a fast change is not missed. The
  `assert` step with operator `text_visible` reads the window through OCR.
- `ForegroundGuard`: pure rule that stops a batch when the foreground moved,
  between steps, to a window outside the batch's processes (someone took the
  screen). Dialogs of the target, transitions without a foreground and the
  loss of the baseline window are tolerated. `IForegroundReader` lets tests
  script the foreground.

### Winsight.Core/Modules/ManagedState

Wraps Microsoft.Diagnostics.Runtime (CLRMD).

- `ManagedStateReader`: attaches non-suspending to a managed process, walks
  the heap, finds the first instance of the requested type, and reads a named
  field. Supports primitive element types and reference summaries.

The host process **must** match the target architecture (x64 by default in
this repo). Cross-bitness inspection is not supported.

### Winsight.Core/Modules/Capture

Window screenshot capture via the Win32 `PrintWindow` API with the
`PW_RENDERFULLCONTENT` flag (Windows 8.1+).

- `WindowCapture`: locates visible top-level Win32 windows by process id,
  optionally filters by title substring, allocates a matching `Bitmap`, calls
  `PrintWindow`, and saves a PNG to the requested path. Full-window captures
  default to the topmost visible window in z-order so modal dialogs and popups
  are addressable; client-area baseline captures prefer the process main window
  unless a title scope is provided. `CaptureFrame` returns the same full-window
  capture in memory, optionally cropped to a `WindowRegion`, as a
  `CapturedFrame` (bitmap, window identity, region) for the visual waits and OCR.

The `PW_RENDERFULLCONTENT` flag is what lets the call render DirectX-composed
WPF windows correctly. Without it, accelerated surfaces come back as black
rectangles.

### Winsight.Core/Modules/WebView2

Discovers embedded WebView2 instances and their Chrome DevTools Protocol
endpoints, so AI agents can attach Playwright/Puppeteer over CDP.

- `WebView2Detector`: single-query WMI walk of `Win32_Process`, traverses
  the descendant tree from a host pid, and reports each `msedgewebview2.exe`
  child along with any `--remote-debugging-port=N` it was launched with.
  Cross-references with `IPGlobalProperties.GetActiveTcpListeners()` to
  confirm whether the port is actually listening.
- `WebView2CommandLineParser`: pure-function helper that extracts the
  debugging port from a process command line. Easy to unit-test.

This module does not start or modify the WebView2 environment. Enabling CDP
must happen in the host app code (via
`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` or
`CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`); the detector
just reports what is already there and emits an actionable hint when nothing
usable is found.

### Winsight.Core/Modules/Diagnostics

Non-intrusive observability of managed (.NET 6+) processes via the
official EventPipe IPC, the same channel `dotnet-counters` and
`dotnet-trace` use. Crucially, this is **not** raw ETW: no admin
privileges required, no `CreateRemoteThread` heuristics that EDR products
flag, and no source code changes in the target.

- `DiagnosticsObserver.ObserveCounters`: opens an EventPipe session for
  one provider with `EventCounterIntervalSec=1`, listens for
  `EventCounters` events for a bounded window (500ms-30s), aggregates the
  last observed `Mean` (averaging counters) or `Increment` (monotonic
  counters) value per counter name, and returns the snapshot.
- `DiagnosticsObserver.CaptureEvents`: opens an EventPipe session
  spanning one or more providers, captures every event (or only those
  matching an exact name filter), normalizes payloads to JSON-safe
  representations, and returns a bounded list with a `Truncated` flag
  when the cap is reached. Default cap is 500 events; max 10,000.
- `DiagnosticsObserver.CaptureExceptionsDuring`: the causal oracle behind
  `run_steps captureExceptions`: starts a CLR-exception EventPipe session,
  settles, runs the supplied action, drains, and stops, returning the exceptions
  thrown during the action. Uses the strongly-typed `ClrTraceEventParser`
  (`ExceptionStart`) because the Dynamic parser cannot decode the manifest-based
  `Microsoft-Windows-DotNETRuntime` payloads over EventPipe.
- `PayloadNormalizer`: pure-function helper that converts arbitrary
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
   rate, GC frequency, thread pool growth, useful for "did this scenario
   leak?" oracles that a single point-in-time field read cannot answer.

CLRMD remains the right tool when you need to read a specific field on a
specific instance, especially during a single deterministic test step.

### Winsight.Core/Modules/Vision

Locates UI elements that UIA cannot see (custom Canvas, SkiaSharp,
DirectX surfaces, WebView2 content) by submitting a window screenshot
plus a natural-language query to a vision model and reading back a
bounding box.

- `IElementGrounder`, the contract: `FindAsync(screenshotPath, query, ct)`
  returns a `GroundedElement` (center coordinates, bounding box,
  confidence, reasoning, source).
- `AnthropicVisionGrounder`: default implementation. Reads
  `ANTHROPIC_API_KEY` from the environment, calls the Anthropic Messages
  API with the screenshot encoded as base64 and a tightly-constrained
  prompt that asks for JSON-only output. Default model is
  `claude-sonnet-4-6`; override via `WINSIGHT_VISION_MODEL`. The endpoint
  itself can be overridden via `WINSIGHT_VISION_API_URL` for self-hosted
  proxies or routing through a corporate gateway.
- `AnthropicResponseParser`: extracts the model's JSON object from the
  response envelope, robust to wrapping prose, markdown code fences,
  numeric strings, and braces inside reasoning text.
- `OnnxLocalGrounder`: selectable with `WINSIGHT_VISION_BACKEND=onnx`. A fully
  local backend running Qwen2.5-VL-3B as a 3-ONNX split (vision encoder + token
  embedding + decoder) on plain ONNX Runtime with a hand-rolled mRoPE generation
  loop. It validates `WINSIGHT_VISION_ONNX_PATH` and requires
  `WINSIGHT_VISION_EP=cpu`, fails explicitly when misconfigured, and never falls
  back to the cloud. Execution is CPU-only in this build (the decoder's fused
  MultiHeadAttention op is not yet supported on the DirectML EP; a tracked
  follow-up).
- `QwenImagePreprocessing` / `QwenVisionTokenizer`: C# reimplementations of the
  Qwen2.5-VL smart-resize + patchify and the byte-level BPE tokenizer (via
  `CodeGenTokenizer` over a vocab patched with Qwen special tokens).
- `GroundingCoordinateParser`: turns the model's textual point `[x,y]` or box
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

### Winsight.Core/Modules/VisualRegression

Compares a captured UI screenshot to an approved baseline using deterministic
image metrics. This module intentionally does not use Anomalib/PatchCore:
Winsight drives the app into known states, so it has a baseline and does not
need unsupervised novelty detection.

- `VisualDiffer`: loads two same-sized PNGs, computes grayscale SSIM (Gaussian
  11x11, sigma 1.5) both as a global mean and as the worst NxN block (local SSIM,
  to catch small localized changes), computes a pixelmatch-style YIQ pixel-diff
  ratio with anti-aliased-edge detection, and writes an optional red heatmap.
- `VisualDiffOptions`: global + local SSIM thresholds, pixel-diff threshold,
  pixelmatch color threshold, ignore rectangles, and optional diff-map path.
  Defaults are read from `WINSIGHT_VISUAL_SSIM_THRESHOLD`,
  `WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD`, `WINSIGHT_VISUAL_PIXEL_THRESHOLD`, and
  `WINSIGHT_VISUAL_COLOR_THRESHOLD`.
- `VisualDiffResult`: pass/fail (or baseline-created/updated) verdict, global
  SSIM, local SSIM minimum, pixel-diff ratio, diff-map path, and reasoning
  returned by `compare_visual_baseline`. Captures use the window client area for
  determinism.
- `VisualIgnoreRegion`: JSON-friendly rectangle DTO used by the MCP tool to
  mask dynamic UI such as clocks, cursors, caret blinks, or animations.

The comparison fails hard on image-size differences. Ignore rectangles exclude
pixels from both the SSIM denominator and the pixel-diff ratio.

- `VisualDiffer.Compare(Bitmap, Bitmap, options)`: the same comparison on
  in-memory images, used to watch a window rather than check a baseline file.
- `VisualWaiter`: backs `wait_for_visual_change` (poll until a capture differs
  from a reference) and `wait_for_visual_stable` (poll until a number of
  consecutive comparisons show no change). The loops take an injected frame
  source and `IWaitClock`, so they are tested without a desktop.

### Winsight.Core/Modules/UserActivity

Tells a person's input from Winsight's on a shared desktop, without a
low-level hook (which endpoint products flag).

- `InputLedger`: named shared memory (`Local\Winsight.InputLedger.<session>`)
  holding the last injection window (start, end, in progress) and the last
  input attributed to a human. Every Winsight process in the session writes
  its injections there through `DesktopInputLease`.
- `InputAttributor`: pure attribution by time: the last input
  (`GetLastInputInfo`) is Winsight's when it falls inside the injection window
  widened by 50 ms, a human's otherwise. Handles the 32-bit tick wrap and
  ignores an injection left active by a crashed process after 60 s.
- `UserActivityMonitor`: snapshot for `get_user_activity` (human idle time,
  foreground window and process, locked session through
  `WTSQuerySessionInformation`) and the polling behind `wait_for_user_idle`.
- `UserActivityPolicy`: reads `WINSIGHT_YIELD_TO_USER_MS`; screen-changing
  tools, `run_steps` steps and the outermost input lease fail with
  `user_active` while a human input is more recent than the threshold.

Known limits: a person's input made during a Winsight injection is masked by
it, and input injected by another tool counts as human.

### Winsight.Core/Modules/Clipboard

- `ClipboardThread`: a dedicated STA thread that owns a message-only window
  and pumps its messages for the life of the process. The clipboard owner
  receives messages when another application takes the clipboard; an owner
  that did not pump would stall it.
- `ClipboardService`: reads and writes text, and saves and restores every
  clipboard format held in a memory block (bitmaps, metafiles and private
  handles are reported as skipped). Text Winsight writes carries
  `ExcludeClipboardContentFromMonitorProcessing`,
  `CanIncludeInClipboardHistory=0` and `CanUploadToCloudClipboard=0`.
- `ClipboardPolicy`: every clipboard tool and `type_keys mode=paste` refuse
  with `clipboard_disabled` unless `WINSIGHT_ALLOW_CLIPBOARD` is set.

### Winsight.Core/Modules/Ocr

- `VisualTextReader`: captures the window or a region in memory, enlarges it
  (default x2, bounded by the engine maximum), and runs `Windows.Media.Ocr`,
  offline, in the requested language or the user profile languages. Lines and
  words come back with rectangles mapped to window coordinates
  (`OcrGeometry`). This is why Core targets `net10.0-windows10.0.19041.0`.
- `VisualTextMatcher`: the `text_visible` comparison: case-insensitive,
  whitespace collapsed, line by line and across lines.

### Winsight.Mcp

A `Microsoft.Extensions.Hosting` host that registers the engine modules as
singletons, then plugs them into `AddMcpServer().WithStdioServerTransport()`,
registering tools (`WithToolsFromAssembly`), read-only resources
(`WinsightResources`: `winsight://guide`, `state`, `windows`, `scene/latest`),
and reusable prompts (`WinsightPrompts`: `explore_app`, `verify_flow`,
`diagnose_ui_bug`). Every tool declares explicit MCP annotations (`ReadOnly`,
`Destructive`, `Idempotent`, `OpenWorld`), and `McpServerConfiguration` sets
startup `ServerInstructions` plus optional `WINSIGHT_TOOL_PROFILE=compact`
filtering.

A singleton `SessionTarget` holds the session default process and optional
named aliases (`set_target` / `switch_target` / `get_target` / `clear_target`),
so an agent driving one or multiple apps need not repeat raw process ids.
Verified scene tools share DI-managed singletons through `SceneServices`
(including `UiaSceneDriver`, `VisualTextReader`, `WebView2Detector`,
`ApplicationProfiles` and `SessionMemory` with consecutive-failure loop
detection). `UiEventWatcher` provides bounded real-time UIA event hooks and
`UiAccessibilityAuditor` runs deterministic QA checks on captured UIA scenes.

`ToolListAnnouncer` sends `notifications/tools/list_changed` once the client
has initialized. `ServerBuildInfo` gives `get_winsight_guide` the build this
process runs (product version with commit, file time, process start), the
tools it registered, and whether the source output that `tools\run-mcp.ps1`
copied it from has been rebuilt since (`WINSIGHT_SOURCE_BUILD_DIR`). A client
session keeps the server process it started with, so a stale tool list is
visible there.

Logging is forced to stderr: stdout is reserved for the MCP protocol.

## Threading and lifetime

- Each tool call constructs and disposes its own `UIA3Automation` instance.
  This keeps tools stateless and avoids COM apartment surprises across
  parallel MCP requests.
- CLRMD's `DataTarget` is similarly scoped per call. Attach is non-suspending
  (`suspend: false`) to avoid GC interaction issues on .NET 9 / .NET 10.

## Tool boundary safety

Every tool method on `Winsight.Mcp` is wrapped by `ToolSafety.SafeInvoke`
(`SafeInvokeAsync` for the vision tool, `SafeInvokeContent` for the
image-returning capture tools). The guard:

- Catches every non-fatal exception thrown inside the tool body and
  returns a structured `OperationResult { success: false, error }` JSON
  the agent can read and recover from. `SafeInvokeContent` returns the
  same failure JSON inside an error `CallToolResult` text block, so the
  error shape is identical whether a tool returns a string or rich
  content (text plus an inline image).
- Mirrors the original exception (with full stack) to **stderr** so a
  human attaching to the host process can see what happened. Stdout
  remains reserved for MCP protocol traffic.
- Records a compact JSONL tool event when `WINSIGHT_TRACE` or
  `WINSIGHT_TRACE_DIR` is set: tool name, duration, success/error, and
  payload byte count.
- Lets truly fatal exceptions propagate (`OutOfMemoryException`,
  `StackOverflowException`, `ThreadAbortException`): masking those
  would hide a process-wide failure.

The reason this guard exists rather than relying on the MCP framework's
own error handling: a single unhandled exception inside any tool (a
`COMException` from a UIA call, a CLRMD attach failure on an
inaccessible process) was observed to **terminate the .NET host
process**, which in turn closes the stdio transport and renders every
other tool permanently unusable for the rest of the agent's session.
The universal guard pins the failure to the calling tool only.

## Policy and replay

`SessionTarget` is also the process-policy choke point. `TryResolve` and
`TrySet` enforce optional allowlists before any process-scoped tool runs:
`WINSIGHT_ALLOWED_PROCESS_IDS`, `WINSIGHT_ALLOWED_PROCESS_NAMES`, and
`WINSIGHT_ALLOWED_EXECUTABLES` for `launch_app`. `ToolAccessPolicy.GuardMutation`
gates every screen-changing tool: dry-run preview first, then the
yield-to-user refusal. The CDP tools also require
the requested port to be a listening socket owned by a WebView2 process
discovered under the authorized host process, so a raw loopback port is not
enough to bypass the process policy. `WINSIGHT_DRY_RUN` blocks mutating MCP
tools (`click_*`, typing, selection, key chords, JS evaluation, launch/close,
and `run_steps` screenshot writes) and returns a structured preview instead.

`run_steps` can write a replay artifact containing the prepared batch and
journal. `replay_steps` reads that artifact and re-runs it, dry-run by
default, so an agent can debug a scenario before repeating side effects.

## Agent experience layer

`AgentExperienceTool` is the high-level surface intended for AI agents:

- `observe` fuses window discovery, affordance scanning, likely next actions,
  session memory, and optional screenshots.
- Each observed affordance carries a stable `affordanceId`; `nextActions`
  includes the preferred tool, locator args, dry-run defaults, risk, and
  expected feedback so agents can act without re-deriving the call shape.
- `plan_steps` turns a human goal into a conservative `BatchStep` draft that
  should be dry-run before execution.
- `repair_locator` ranks current UI and memory candidates when a locator fails.
- `act_and_observe`, `click_and_wait`, and `type_and_assert` collapse common
  act/sync/assert/diff loops into one call.
- `get_agent_state` returns the current target, guardrails, memory, last
  failure, recommended next step, and embedded recipes.
- `get_winsight_guide` exposes the preferred loop directly through MCP, so an
  agent does not need external documentation in context.

`SessionMemory` records recently observed locators by process/window and keeps a
short list of repairable failures. It is intentionally session-scoped, not
persisted to disk. Repairable high-level failures share the same data shape:
`code`, `cause`, `suggestedTool`, optional `suggestedArgs`, optional
`candidates`, and `retryable`.

## Failure modes and how they are reported

| Situation                                 | Behavior                                              |
| ----------------------------------------- | ----------------------------------------------------- |
| Window has no name                        | Skipped from desktop-wide `list_windows`; included in a per-process listing |
| COM exception on a single window          | Caught locally, that window is skipped                |
| Element not found                         | `OperationResult.Failure(...)` (success = false)      |
| Process has no managed runtime            | `OperationResult.Failure(...)`                        |
| Type/field not found in heap              | `OperationResult.Failure(...)`                        |
| Architecture / elevation mismatch         | CLRMD attach throws; ToolSafety wraps it as `OperationResult.Failure(message)` and the host stays up |
| Any other unhandled exception in a tool   | ToolSafety wraps it as `OperationResult.Failure(message)`, stderr-logs the full stack, returns to caller |
| A person used the desktop more recently than `WINSIGHT_YIELD_TO_USER_MS` | `user_active: ...`, nothing is sent |
| The foreground moved between `run_steps` steps | The batch stops with `foreground_changed` and `foregroundChanged: true` |
| The pointer moved during a settle         | `cursor_moved: ...`, the press, wheel or hold is not sent |
| Clipboard tool without `WINSIGHT_ALLOW_CLIPBOARD` | `clipboard_disabled: ...` |
| No OCR component for the language          | `ocr_unavailable: ...` listing the installed languages |
| A visual or idle wait never holds          | `timed_out: ...` with the last measurements |

## What is intentionally absent at this point

- GPU acceleration for the local ONNX grounder. The local Qwen2.5-VL backend
  ships (CPU); DirectML/CUDA execution is a follow-up (the decoder's fused
  MultiHeadAttention op is not yet supported on the DirectML EP). See
  [local-onnx-grounder.md](local-onnx-grounder.md).
- Baseline-free visual novelty detection. The shipped visual-regression path is
  baseline-based SSIM + pixel diff; unsupervised anomaly detection would be a
  separate feature if there is a concrete need later.
- Process-injection-based instrumentation (DLL injection, MinHook).
- Low-level input hooks (`WH_MOUSE_LL`, `WH_KEYBOARD_LL`) in the server.
  User activity is attributed by time through the input ledger instead; the
  WPF lab's keyboard hook is a test target, not part of Winsight.
- DPI-aware coordinate mapping for multi-monitor setups.
- D3D11 `Present` hook for capturing surfaces UIA cannot see.
- Cross-elevation UIA / CLRMD inspection. A standard-user winsight host
  cannot inspect or attach to processes that ship
  `<requestedExecutionLevel level="requireAdministrator" />`. The tool
  failures surface cleanly through `ToolSafety` but the operations
  themselves cannot succeed. To drive elevated apps, run the winsight
  host at the same or higher integrity level.

These are tracked in the README capability matrix.
