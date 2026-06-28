# Changelog

All notable changes to Winsight are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

#### Agent-first workflow and hardening

- Added high-level MCP tools for AI agents: `observe`, `plan_steps`,
  `repair_locator`, `act_and_observe`, `click_and_wait`, `type_and_assert`,
  `get_session_memory`, `clear_session_memory`, and `get_winsight_guide`.
- Added session-scoped locator memory and replay artifacts for `run_steps`.
- Added configurable guardrails: process/executable allowlists, global dry-run,
  JSONL traces, and an MCP runner that avoids locking build outputs.
- Added Windows CI for build, unit tests, E2E, MCP smoke, and NuGet audits.
- Added explainable locator scores and reasons in candidate diagnostics.
- Added UIA/CDP/diagnostics payload bounds and CPU-only ONNX validation.

#### Phase 1 — UIA + CLRMD foundation

- `Winsight.Core` engine targeting `net10.0-windows`, x64.
- `Winsight.Mcp` server speaking MCP over stdio, built on
  ModelContextProtocol 1.0.0 and `Microsoft.Extensions.Hosting`.
- `samples/SampleWpfApp` — a deliberately minimal WPF counter app used as
  the integration target.
- 5 tools:
  - `list_windows` — top-level window enumeration via FlaUI UIA3.
  - `inspect_ui_tree` — bounded BFS of the UIA tree (max depth 20).
  - `click_element` — Invoke / Toggle / mouse fallback.
  - `type_text` — Value pattern / keyboard fallback.
  - `read_managed_state` — CLRMD non-suspending attach + heap walk.
- Solution layout: `Directory.Build.props` (treat warnings as errors),
  `Directory.Packages.props` (central package management), `.slnx`
  solution file (.NET 10 format).

#### Phase 2 — perception, oracle, vision

- `WindowCapture` — PrintWindow with `PW_RENDERFULLCONTENT` so
  DirectX-composed WPF surfaces capture without going black.
- `capture_screenshot` and `find_element_visually` can target modal dialogs,
  popups, and secondary windows with `windowTitle`; default capture now uses
  visible top-level Win32 windows in z-order instead of UIA desktop children.
- `WebView2Detector` + `WebView2CommandLineParser` — single-WMI-query
  walk of the host's process tree, finds `msedgewebview2.exe`
  descendants, parses `--remote-debugging-port`, cross-references
  `IPGlobalProperties.GetActiveTcpListeners()`. Returns an actionable
  hint (in tool output) when no usable CDP endpoint is found.
- `DiagnosticsObserver.ObserveCounters` — EventPipe-based EventCounter
  aggregator. Uses `Microsoft.Diagnostics.NETCore.Client` and
  `Microsoft.Diagnostics.Tracing.TraceEvent` — same channel as
  `dotnet-counters` / `dotnet-trace`, so it is invisible to enterprise
  EDR products that flag CLRMD attach.
- `DiagnosticsObserver.CaptureEvents` — captures arbitrary EventPipe
  events from one or more providers, normalizes payloads via
  `PayloadNormalizer` (depth-capped recursion, JSON-safe types), bounded
  by configurable `maxEvents` and `durationMs`.
- `IElementGrounder` interface + `AnthropicVisionGrounder`
  implementation — calls the Anthropic Messages API with the screenshot
  encoded as base64 and a tightly-constrained prompt, parses the
  response via `AnthropicResponseParser` (robust to wrapping prose, code
  fences, numeric strings, embedded braces). Configured via
  `ANTHROPIC_API_KEY`, `WINSIGHT_VISION_MODEL`,
  `WINSIGHT_VISION_API_URL` environment variables.
- 5 additional tools:
  - `capture_screenshot`
  - `detect_webview2_endpoints`
  - `observe_counters`
  - `capture_events`
  - `find_element_visually`

#### Phase 2.1 — WebView2 CDP driving

- `Cdp` module (`Winsight.Core/Modules/Cdp`) — dependency-free Chrome
  DevTools Protocol client over a raw `ClientWebSocket` (no Playwright /
  Puppeteer). `CdpTargetDiscovery` lists debuggable targets via
  `/json/list`; `CdpClient` attaches to a page target, correlates command
  ids, skips event frames, and times out per command
  (`WINSIGHT_CDP_TIMEOUT_MS`, default 10000 ms).
- `CdpKeyEventFactory` — expands text into `Input.dispatchKeyEvent`
  keyDown/keyUp sequences (`\n`/`\r\n` → Enter with
  `windowsVirtualKeyCode` 13, `\t` → Tab). Real key events are what make
  typing reach keydown-driven surfaces such as xterm.js terminals hosted
  in `HwndHost`/WebView2 — the exact case where UIA `type_text` fails.
- 3 additional tools (10 → 13):
  - `list_cdp_targets` — targets of a CDP endpoint, with a hint when no
    `page` target is available yet.
  - `evaluate_javascript` — `Runtime.evaluate` with `returnByValue` +
    `awaitPromise`; script exceptions surface as structured failures.
  - `type_into_webview` — types text as real key events, with an optional
    `focusSelector` that fails explicitly when nothing matches.
- 15 new unit tests (target parsing, key event sequences, response/
  exception parsing). `smoke-mcp.ps1` expected tool count bumped to 13.

#### Phase 2.2 — UIA test ergonomics

- `ElementLocator` extended with `controlType` (case-insensitive),
  `nameContains` (case-insensitive substring), and `index` (explicit pick
  among multiple matches). Matching logic extracted into a pure,
  unit-tested `LocatorMatcher`.
- **Strict locator resolution** — when several elements match and no
  `index` is given, the call fails and reports the candidates
  (id/name/controlType/window) in the error message and the `data`
  payload, instead of silently acting on the first BFS hit. When nothing
  matches, near-miss suggestions are returned.
- **Multi-window + modal dialog support** — element searches now cover
  every visible top-level window of the process, enumerated through Win32
  `EnumWindows` and rooted at their handles (`NativeWindowEnumerator`).
  This is required because UIA reparents owned windows (modal dialogs)
  under their owner and then omits them from both desktop and owner child
  listings, making them unreachable through tree walks. A new
  `windowTitle` parameter scopes any UIA tool to one window.
- `get_element_state` tool + `ElementStateReader` — UI-level oracle
  returning name, Value-pattern content, toggle state, selection
  (item + container), enabled/visible/focus flags, and geometry.
- `wait_for_element` tool + `ElementWaiter` — condition-based
  synchronization (`exists`, `not_exists`, `enabled`, `visible`) with
  configurable timeout (`WINSIGHT_UIA_WAIT_TIMEOUT_MS`, capped at
  120000 ms) and poll interval; returns elapsed time, match count, and
  the final element state.
- `select_item` tool — selects an item by visible text in a ComboBox,
  ListBox, or Tab (descendant SelectionItem fallback elsewhere); lists
  the available items on failure.
- `click_element` gains `clickStyle` (`single`/`double`/`right`) and
  scrolls off-screen elements into view before physical clicks.
- `inspect_ui_tree` gains `windowTitle`, `controlType`, and
  `nameContains` filters (pruning keeps matching nodes and their
  ancestors) and now reports each node's Value-pattern content, so the
  tree doubles as a read-only oracle.
- UIA walks are hardened against `COMException` from windows or elements
  tearing down mid-traversal (transient states are skipped or retried on
  the next poll instead of failing the tool call).
- `SampleWpfApp` extended with a ComboBox, a CheckBox, and a modal note
  dialog so E2E scenarios exercise selection, toggling, window-scoped
  locators, and dialog synchronization.
- 3 additional tools (13 → 16); `smoke-mcp.ps1` expected tool count
  bumped to 16. 29 new unit tests + 1 new E2E scenario (E2E now runs
  sequentially via `DisableTestParallelization` — concurrent scenarios
  race on physical input and the live desktop tree).

#### Phase 2.3 — UIA focus, semantic matching, and stability

- `send_key_chord` tool — parses chords such as `Ctrl+Shift+N`, focuses
  the selected process window first, verifies `GetForegroundWindow`, and
  dispatches through FlaUI's SendInput-backed keyboard primitives
  instead of `SendKeys`.
- `type_text` gains `focusWindow`, an optional foreground-window check
  before writing text or falling back to keyboard input.
- `ElementLocator` gains `descendantText`, a case-insensitive bounded
  descendant Name/Value match for WPF controls whose parent Name exposes
  view-model metadata while the real label lives in a child `TextBlock`.
- `wait_for_element` gains `value_equals`, `value_contains`, and
  `stable_for`. Value waits assert Value-pattern content; `stable_for`
  requires the full `ElementState` snapshot to remain unchanged for
  `stableForMs`, covering debounce/filter delays.
- `hover_element` tool — moves the mouse to an element center and captures
  newly appearing nearby tooltips, filtering tooltip snapshots that were
  already present before the hover.
- `SampleWpfApp` now covers a global keyboard shortcut, tooltip hover,
  and semantic descendant-text locators for deterministic E2E validation.
- 2 additional tools (16 → 18); `smoke-mcp.ps1` expected tool count
  bumped to 18.

#### Phase 2.4 — configurable capture output

- `capture_screenshot` and `find_element_visually` resolve their default
  capture path through a shared `ScreenshotOutput` helper that honors the
  `WINSIGHT_OUTPUT_DIR` environment variable (fallback `%TEMP%\winsight`), so a
  supervisor agent can read captures from a known or shared directory. An
  explicit `outputPath` argument still takes precedence; the directory is
  created on first write. Tool count unchanged (18).

#### Phase 2.5 — local ONNX vision grounder

- `OnnxLocalGrounder` — fully local `IElementGrounder` selected with
  `WINSIGHT_VISION_BACKEND=onnx`. Runs Qwen2.5-VL-3B as a 3-ONNX split (vision
  encoder + token embedding + decoder, fp16) on plain `Microsoft.ML.OnnxRuntime`
  with a hand-rolled mRoPE generation loop; nothing leaves the host and it never
  falls back to the cloud. CPU execution (DirectML/CUDA is a follow-up; the
  decoder's fused MultiHeadAttention op is unsupported on the DirectML EP).
- Supporting pieces: `QwenImagePreprocessing` (smart-resize + CLIP normalize +
  patchify via `System.Drawing`), `QwenVisionTokenizer` (Qwen byte-level BPE via
  `Microsoft.ML.Tokenizers` `CodeGenTokenizer` over a special-token-patched
  vocab), `GroundingCoordinateParser` (point/box pixel output → click center),
  and `VisionGrounderRegistration` (env-driven backend selection, explicit-failure
  fallback for unknown backends).
- `tools/provision-vision-model.ps1` downloads the model package
  (`onnx-community/Qwen2.5-VL-3B-Instruct-ONNX` + tokenizer files); weights are
  never committed. New dependencies: `Microsoft.ML.OnnxRuntime`,
  `Microsoft.ML.Tokenizers`. See `docs/local-onnx-grounder.md`.

#### Phase 2.6 — visual regression baselines

- `VisualRegression` module (`Winsight.Core/Modules/VisualRegression`) —
  baseline-based UI screenshot comparison in pure .NET: global SSIM (Gaussian
  11x11, sigma 1.5) plus a worst-block local SSIM (catches small localized
  changes the global mean misses), plus a pixelmatch-style YIQ pixel-diff with
  anti-aliased-edge detection (robust to ClearType subpixel rendering). Ignore
  rectangles exclude dynamic regions; diff heatmaps are emitted as PNGs.
- `WindowCapture.CaptureClientArea` — captures the window client area only (no
  title bar/borders), so OS chrome variance (theme/DPI/focus) does not produce
  false diffs. The visual baseline tool uses it.
- `compare_visual_baseline` MCP tool — captures the target window client area,
  resolves a relative baseline key under `WINSIGHT_VISUAL_BASELINE_DIR` (default
  `%LOCALAPPDATA%\winsight\visual-baselines`), creates/updates missing
  baselines on request, and otherwise returns a `VisualDiffResult` verdict
  (now including the local SSIM minimum). Thresholds are configurable via
  `WINSIGHT_VISUAL_SSIM_THRESHOLD`, `WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD`,
  `WINSIGHT_VISUAL_PIXEL_THRESHOLD`, and `WINSIGHT_VISUAL_COLOR_THRESHOLD`.
  Calibrated on the sample app (two-polarity E2E gate): a same-vs-same recapture
  passes and an Increment-click change is caught by the local SSIM criterion.
- Anomalib/PatchCore is no longer the visual-regression direction for v0:
  Winsight drives known UI states and therefore has approved baselines, so a
  deterministic structural diff is the chosen first implementation.
- 19 total tools; `smoke-mcp.ps1` expected tool count bumped to 19.

#### Phase 2.7 — coordinate clicks, closing the perception-to-action loop

- `click_point` MCP tool — clicks a coordinate without UIA element location, the
  action partner of `find_element_visually` and the escape hatch for surfaces
  UIA cannot address (custom Canvas, SkiaSharp, DirectX). `coordinateSpace`
  defaults to `window` (same origin as a full-window screenshot, so a grounded
  `x,y` passes straight through) or `screen` for absolute desktop coordinates;
  out-of-bounds window-relative points fail explicitly with the window size.
  Returns the absolute `screenX/screenY` clicked.
- `ActionInvoker.ClickPoint` — physical mouse click (single/double/right) at a
  window-relative or screen point, backed by `WindowGeometry` (resolves the
  topmost/title-scoped window origin, mirroring `WindowCapture.Capture`) and the
  pure `WindowPointMapper` (window→screen translation + bounds check).
- `find_element_visually` now enriches `GroundedElement` with `screenX/screenY`
  (window origin added server-side, so no manual coordinate math) and accepts an
  opt-in `act=single|double|right` to click the grounded point in the same call,
  returning both the grounding and the click result.
- 20 total tools; `smoke-mcp.ps1` expected tool count bumped to 20.

#### Phase 2.8 — multi-window discoverability

- `list_windows` now enumerates through Win32 instead of UIA desktop children,
  so owned surfaces that UIA hides — modal dialogs, MessageBoxes, dropdown
  menus, flyouts, tooltips — are discoverable, not merely reachable. Each
  `WindowDescriptor` carries `kind` (main/dialog/popup/menu/tooltip/other),
  `className`, `ownerHandle`, and `isModal`; windows are returned in z-order
  (topmost first), so the leading entry is the active overlay.
- `list_windows` gains an optional `processId`: with it, the listing is
  exhaustive (including untitled dialogs, popups, and menus of that process),
  giving an agent the full window stack of the app under test; without it,
  desktop-wide discovery keeps the named-windows-only contract to avoid noise.
- `WindowClassifier` — pure, unit-tested classification from Win32 class name,
  caption/popup styles, and ownership; `isModal` is derived from an owned window
  whose owner is disabled.

#### Phase 2.9 — application lifecycle

- `launch_app` MCP tool — starts an executable and blocks until its window is
  present (main window, or one matching `waitForWindowTitle`), returning the new
  `processId` and a classified window descriptor ready to drive. Removes the
  shell-out-then-poll-`list_windows` startup race. Fails explicitly when the
  executable is missing, the process exits during startup, or no window appears
  within the timeout (default 15s, max 120s).
- `close_app` MCP tool — graceful shutdown (`CloseMainWindow`) with a kill
  fallback, or `force=true` to kill the process tree immediately; an
  already-gone process is reported as success.
- `AppLauncher` (`Winsight.Core/Modules/Lifecycle`) backs both, reusing
  `WindowDiscovery` to detect the ready window.
- 22 total tools; `smoke-mcp.ps1` expected tool count bumped to 22.

#### Phase 2.10 — batch step execution

- `run_steps` MCP tool — runs a sequence of UI steps in one call instead of one
  round trip per action. A whole scenario (drive, synchronize, assert, capture)
  becomes a single call that returns a per-step journal
  (`{ success, completed, total, failedAtIndex, steps[] }`). Actions: `click`,
  `click_point`, `type`, `key_chord`, `select`, `hover`, `wait`, `get_state`,
  `screenshot`. `stopOnError` (default true) halts at the first failing step; a
  batch-level `processId` default applies to steps that omit their own.
- `StepRunner` (`Winsight.Core/Modules/Orchestration`) dispatches each step to
  the same primitive the matching single-action tool uses, so semantics are
  identical and no logic is duplicated; each step is exception-isolated so one
  bad step cannot tear down the batch. `launch_app`/`close_app` are not step
  actions (the process id is needed to scope steps).
- 23 total tools; `smoke-mcp.ps1` expected tool count bumped to 23.

#### Phase 2.11 — inline image screenshots

- `capture_screenshot` now returns a `CallToolResult` with a text block (the
  unchanged `OperationResult`/`ScreenshotResult` JSON, so existing parsers keep
  working) plus an inline PNG image block, so a supervising agent sees the UI
  directly instead of reading the file from disk. `includeImage=false` opts back
  into text-only.
- `compare_visual_baseline` likewise attaches the diff heatmap PNG inline when a
  comparison runs, so the agent sees where the UI changed.
- `ToolSafety.SafeInvokeContent` — content-returning variant of the boundary
  guard: a non-fatal throw becomes an error `CallToolResult` whose text block
  carries the same `OperationResult.Failure` JSON, so the error shape is
  identical whether a tool returns a string or rich content.
- No new tools (23 total); the two capture tools changed return type only.

#### Phase 2.12 — causal exception oracle

- `run_steps` gains `captureExceptions`: an EventPipe session runs around the
  whole batch and the `BatchResult` gains an `exceptions` array listing the
  exceptions the target process threw *during* the steps (caught ones included),
  turning the time-windowed exception oracle into a causal one. Requires the
  batch-level `processId`; an empty array means none were observed.
- `DiagnosticsObserver.CaptureExceptionsDuring` backs it: start an EventPipe
  session (CLR `ExceptionKeyword`), settle, run the action, drain, stop. It uses
  the strongly-typed `ClrTraceEventParser.ExceptionStart` (the Dynamic parser
  cannot decode the manifest-based `Microsoft-Windows-DotNETRuntime` payloads
  over EventPipe), surfacing `ExceptionType` / `ExceptionMessage`.
- The sample app gained a "Throw Exception" button (`ThrowButton`) that throws
  and swallows an exception, as the E2E target for the oracle.
- No new tools (23 total).

#### Phase 2.16 — first-class assertions

- `assert_element` MCP tool and an `assert` step in `run_steps` — assert a
  condition on an element and get a clear pass/fail with expected-vs-actual
  detail, instead of abusing `wait_for_element`. Operators: equals, not_equals,
  contains, not_contains (on value, else name); greater_than, less_than
  (numeric); enabled, disabled, visible, hidden; checked, unchecked; selected;
  exists. A failing assertion returns `success: false` (and fails the batch step).
- `AssertionEvaluator` computes the verdict as a pure, unit-tested function.
- 30 total tools; `smoke-mcp.ps1` expected tool count bumped to 30.

#### Phase 2.15 — change perception (snapshot_ui / diff_ui)

- `snapshot_ui` and `diff_ui` MCP tools — the feedback sense: capture a
  structural baseline of a process (windows + actionable elements), then report
  what an action changed (windowsOpened/windowsClosed, elementsAdded/
  elementsRemoved, and elementsChanged with per-field before/after) without
  re-inspecting everything. `diff_ui` re-baselines so successive calls chain;
  with no baseline it returns `verdict: baseline_captured`. A session-scoped
  `UiSnapshotStore` keeps one baseline per process.
- `UiDiffer` computes the delta as a pure, unit-tested function (windows matched
  by handle; elements by a stable key with duplicate occurrences matched in
  order); `UiSnapshotter` captures a snapshot by composing `WindowDiscovery` and
  `AffordanceScanner`.
- 29 total tools; `smoke-mcp.ps1` expected tool count bumped to 29.

#### Phase 2.14 — affordance perception (describe_ui)

- `describe_ui` MCP tool — answers "what can I do here?" by returning a window's
  actionable elements as a compact, position-ordered list (top-to-bottom,
  left-to-right) instead of a full UIA tree, so an agent orients before acting.
  Each `UiInteractable` carries control type, label, value/toggle/selection and
  enabled/visible state, bounds, and a `recommendedLocator` — the smallest
  criterion (unique automationId, else unique name, else controlType+index) ready
  to feed click_element / type_text / select_item.
- `AffordanceScanner` (`Winsight.Core/Modules/UiAutomation`) walks the process
  windows breadth-first, keeps the interactable control types, and reuses
  `ElementStateReader.BuildState`; `AffordanceRecommender` computes the locators
  as a pure, unit-tested function.
- 27 total tools; `smoke-mcp.ps1` expected tool count bumped to 27.

#### Phase 2.13 — session target

- `set_target` / `get_target` / `clear_target` MCP tools and a session-scoped
  `SessionTarget` so an agent driving one app can set the target process once
  instead of repeating `processId` on every call.
- Every process-scoped tool now takes an optional `processId` that falls back to
  the session target (an explicit `processId` still overrides it); the parameter
  was moved last and made nullable across the UIA, capture, diagnostics,
  lifecycle, and orchestration tools. `run_steps` resolves its batch-level
  `processId` from the target too.
- A clear failure is returned when neither an explicit `processId` nor a session
  target is available.
- 26 total tools; `smoke-mcp.ps1` expected tool count bumped to 26.

#### Audit hardening

- `find_element_visually` now always returns an `OperationResult` (data is the
  grounded element, or `{ grounded, click }` with `act`), removing the prior
  three-shape inconsistency.
- `AnthropicVisionGrounder` gains a bounded per-request timeout
  (`WINSIGHT_VISION_TIMEOUT_MS`, default 60s) and a short retry on transient
  429/5xx, so a slow or rate-limited API cannot hang or fail a recoverable call.
- Window-scoped capture and the screen-coordinate resolver now share a single
  title-filter in `WindowGeometry`, so they cannot drift apart; `LocatorFactory`
  centralizes `ElementLocator` assembly across the UIA tools.
- `ScreenshotOutput` prunes captures to the most recent 50 per prefix on each
  write (best-effort), so a long session does not fill the output directory.
- `run_steps` gains `maxDurationMs`: a total batch budget that stops the run and
  flags `timedOut` when reached.
- README documents the security/trust model (the server grants the agent broad
  capability over its desktop session; run it only on dev machines).

#### Tool boundary hardening

- `ToolSafety.SafeInvoke` / `SafeInvokeAsync` — universal outer guard at
  the MCP tool boundary. Catches every non-fatal exception thrown inside
  a tool body and converts it into an `OperationResult { success: false,
  error: "<message>" }` JSON. Truly fatal exceptions (`OutOfMemoryException`,
  `StackOverflowException`, `ThreadAbortException`) are deliberately left
  to propagate.
- The guard is applied to all tool methods. Before, an unhandled
  exception inside any tool (e.g. a `COMException` from FlaUI/UIA, or a
  CLRMD attach failure on an inaccessible process) would terminate the
  `.NET` host, close the stdio transport, and render every other tool
  permanently dead for the rest of the agent session. After the guard,
  exceptions surface as structured, actionable JSON and the host stays up.
- `WindowCapture` — `PrintWindow` failure now includes the Win32
  `GetLastError` code and a hint that the surface may be a WinUI3
  composition that does not support `PrintWindow`.

#### Tests

- 151 unit tests covering record contracts, locators, waits, keyboard
  chord parsing, the CDP command-line parser, the EventPipe payload
  normalizer, the Anthropic response parser, the capture-screenshot tool
  wrapper, local ONNX registration/preprocessing, visual diffing, and the
  universal `ToolSafety` guard.
- New `Winsight.Mcp.Tests` project (with `InternalsVisibleTo`) so the
  tool boundary itself can be unit-tested without touching live processes.
- 5 end-to-end tests that drive `SampleWpfApp` from spawn to teardown,
  covering the counter golden path, richer UIA workflows, diagnostics/WebView2,
  vision grounding precision, and visual regression calibration.

#### Validation

- End-to-end dogfood validated against `SampleWpfApp` (golden path: UIA
  click + CLRMD field read + PrintWindow capture all succeed) and against
  `Win11Forge` (elevated app — UIA/CLRMD failures now return clean
  structured errors instead of generic `"An error occurred invoking 'X'"`,
  and the transport survives every failure).

#### Tooling

- `.mcp.json` at the repository root for project-scoped Claude Code
  registration.
- `tools/smoke-mcp.ps1` — JSON-RPC smoke test that spawns the server,
  performs `initialize` + `tools/list`, and asserts the tool count.

### Changed

- Style gate in `.editorconfig`: explicit types are required for ambiguous
  initializers (`IDE0008` is a build error under `EnforceCodeStyleInBuild` +
  `TreatWarningsAsErrors`), while `var` stays allowed when the type is apparent
  (`new T()`, casts) and for built-in types (`IDE0007` left unenforced). This
  keeps the gate auto-fixable by `dotnet format`, which cannot convert apparent
  or built-in `var`.

### Fixed

- `KeyboardChordParser` rejected unknown alphabetic tokens but accepted raw
  numeric strings: `Enum.TryParse` cast them to undefined `VirtualKeyShort`
  values and reported success. Numeric tokens outside the enum are now rejected
  via `Enum.IsDefined`.

### Compatibility notes

- Target processes for CLRMD and EventPipe must be .NET Core 3.0+ /
  .NET 5+. `.NET Framework 4.8` is not supported.
- The MCP server process must match the target process bitness. The
  default platform target throughout the solution is x64.
- `find_element_visually` requires `ANTHROPIC_API_KEY` to be set and
  transmits the screenshot to the configured API. The other 18 tools
  work without any API key.
- Windows UAC / UIPI: a winsight host running at standard-user level
  cannot enumerate, inspect, or attach to processes that require
  administrator (apps shipping `<requestedExecutionLevel
  level="requireAdministrator" .../>`). The tool calls now return clean
  structured failures in that case rather than generic errors, but
  driving such a target requires running winsight at the same or higher
  integrity level.
