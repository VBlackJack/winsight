# Changelog

All notable changes to Winsight are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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
