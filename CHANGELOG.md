# Changelog

All notable changes to Winsight are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.5.0] - 2026-11-23

### Added

- `list_windows` parameter `includeTitles` (default `false`) and `titleWithheld` on every entry; `get_focused_element` also reports `titleWithheld`.
- `read_text_visually(language=...)` same-script fallback: when the requested language has no OCR component, an installed recognizer of the same script is used (same primary language, then the user-profile recognizer, then the first of that script). The result carries `language` (the recognizer used), `requestedLanguage`, and a `warning` starting with `ocr_language_fallback:`. With no same-script recognizer the call still fails with `ocr_unavailable`; a malformed tag fails with `ocr_language_invalid`.
- Uniform capture warning: `capture_screenshot` and `capture_scene` screenshot data and `read_text_visually` results carry a `warning` starting with `region_uniform:` when the captured area is a single color (sampled), prompting a check of the application (stale or disconnected session) before concluding nothing is displayed. A legitimately blank window also triggers it. Two warnings are joined with `" | "`.

### Changed

- **Breaking:** `observe_scene` elements no longer carry a `window` object. Each element has `windowHandle` (omitted for DOM elements) and window bounds are sent once in `windows[]`. Clients reading `element.window` must read `windows[]` through `windowHandle`.
- **Breaking:** `list_windows` without `processId` (desktop-wide mode) now returns `title: ""` and `titleWithheld: true` for every process other than the session target, also when no target is set. Pass `includeTitles=true` to get all titles, or discover targets by `processName`, `className`, and bounds. Per-process mode is unchanged. The `winsight://windows` resource follows the same default and has no opt-in.
- **Breaking:** `get_focused_element` (and results embedding the focus information, such as `send_key_chord` and clipboard results) returns `topLevelWindowTitle: ""` with `titleWithheld: true` when the focused window does not belong to the target; the focused element `name` is also returned empty then (a focused browser tab or chat item exposes the page title or message text).
- `observe_scene(actionableOnly=true)` is now strict: an element is kept only if it has at least one action other than `drag_to`, `right_click`, and `double_click`, and is visible or supports `scroll_into_view`. Separators, static texts, and invisible menu items are dropped. The same rule drives the `surfaceHint` sparse-UIA detection and the labels drawn by `capture_scene`. `any_change` verification under `actionableOnly` is unchanged and still sees static text changes.

### Fixed

- `captureSource="screen"` copies only the DWM extended frame of the window, so the invisible resize-border strips stay transparent instead of showing foreign pixels; the image keeps the window-rectangle size, so pixels still equal window-relative coordinates. Falls back to the full rectangle when the DWM frame is unavailable or not inside the window rectangle. Known limit: Windows 11 rounded corners can still show a few foreign pixels.

## [0.4.0] - 2026-11-20

### Added

- Popup and menu capture via `captureSource="screen"` (default `"window"`) on `capture_screenshot`, `capture_scene`, and `observe(includeScreenshot=true)`: captures the DWM-composited desktop rectangle matching the target window bounds (`BitBlt` with `SRCCOPY | CAPTUREBLT`) so floating same-process menus, dropdowns, and tooltips appear while keeping window-relative coordinates for `click_point`. Refuses hidden/minimized/cloaked targets and targets occluded by another process window unless `allowOccluded=true`.
- Configurable UIA traversal time budget (`WINSIGHT_SCENE_TRAVERSAL_BUDGET_MS`, default `5000` ms, range `100..120000`) shared by `observe_scene`, `inspect_ui_tree`, and `snapshot_ui`, returning `truncated=true` with `truncationReason="time_budget"` (as well as `"node_limit"`, `"max_results"`, `"com_error"`) when hit.
- Non-blocking `hint` (`code="any_change_unrelated"`) on `act_verified` whenever `expect.kind=any_change` verifies only through elements unrelated to the target (not the target, not its descendants, and not a new window), recommending strict postcondition kinds (`window_open`, `selected`, `value`, `toggle`, ...) while keeping `any_change` semantics unchanged; documented in `get_winsight_guide` and verified-control docs.
- Opt-in hardware mouse jiggler filter (`WINSIGHT_IGNORE_PERIODIC_MICRO_MOVES=1`, threshold `WINSIGHT_PERIODIC_MICRO_MOVE_MAX_PX`, default `15` px, tolerance `WINSIGHT_PERIODIC_MICRO_MOVE_TOLERANCE_PCT`, default `25`%): regular small mouse-only movements are classified as `lastInputSource="periodic_micro_move"` and do not reset `humanIdleMs`, while keyboard input, clicks, and larger or irregular moves always count as `human`. Learned period persists across Winsight injections and human interruptions so classification resumes on the first matching micro-move. Exposed in `get_user_activity` diagnostics (`periodicMicroMoveFilterEnabled`, `periodicMicroMoveThresholdPx`, `lastInputIgnoredAsPeriodicMicroMove`).
- Lightweight background activity sampler (`WINSIGHT_ACTIVITY_SAMPLE_MS`, default `250` ms, `0` disables) polling `GetLastInputInfo` and cursor position between tool calls so `recentInputs` and the periodic micro-move detector capture true input timing without requiring `WINSIGHT_INPUT_HOOK=1`.
- `sessionLockSource` (`wts`, `input_desktop`, `unknown`) on `get_user_activity` so callers can confirm which method produced `sessionLocked`.
- Locked-session input guard (`session_locked`): all UI input tools (`click_*`, `hover_*`, `scroll_point`, `drag`, `type_*`, `send_key_chord`, `act_verified`, `run_steps`, `run_verified_steps`, etc.) refuse with `session_locked` while `sessionLocked` is `true`, while read-only tools keep working; documented in `get_winsight_guide` (`errorPolicy`).
- Low-level input hook resilience (`WINSIGHT_INPUT_HOOK=1`): lock-free minimal hook callbacks, pump-thread heartbeat timer, and silent-removal detection (`LowLevelHooksTimeout`), reporting `inputHookActive=false` plus `inputHookWarning` in `get_user_activity` diagnostics instead of silently degrading.
- `launch_app` stub-launcher / app execution alias handoff (for example `C:\Windows\System32\notepad.exe` on Windows 11): when the launched process exits with code `0` before showing a window, polls for a newly appeared top-level window in another process (matching `waitForWindowTitle` or the executable name when possible), binds to it, and reports `handedOffToProcessId`.

### Fixed

- `observe_scene`, `inspect_ui_tree`, `snapshot_ui`, and `ElementStateReader` now read all optional UIA properties (`IsOffscreen`, `BoundingRectangle`, `IsEnabled`, `ExpandCollapseState`, `ToggleState`, `IsSelected`, etc.) through safe `TryGetValue` accessors, preventing `PropertyNotSupportedException` failures on WinUI/XAML (Notepad) and Chromium (Brave) providers.

## [0.3.0] - 2026-11-20

### Added

- Per-Monitor V2 DPI awareness at startup (`ProcessDpiAwareness.TryEnablePerMonitorV2`) and multi-monitor virtual-desktop coordinate normalization (`SM_XVIRTUALSCREEN`/`SM_YVIRTUALSCREEN`/`SM_CXVIRTUALSCREEN`/`SM_CYVIRTUALSCREEN` with `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`).
- Process integrity level inspection (`GetTokenInformation(TokenIntegrityLevel)`): `set_target` and `switch_target` return `integrity` and `integrityWarning`, `observe_scene` attaches `integrityWarning`, and input tools fail fast with `uipi_blocked` when target integrity is strictly higher than Winsight (returning `"unknown"` and allowing input on `ERROR_ACCESS_DENIED`).
- `diagnose_pointer` MCP tool (`ReadOnly=false`, `Idempotent=false`, guarded by `ToolAccessPolicy.GuardMutation`, honoring `WINSIGHT_DRY_RUN=1`) to test all four Windows pointer movement methods (`send_input_virtual_desk`, `send_input_primary_absolute`, `send_input_relative`, `set_cursor_pos`) with immediate, 50 ms, and 150 ms `GetCursorPos`/`GetPhysicalCursorPos` readings, cursor restore, `ClipCursor`, `GetCursorInfo`, mouse buttons, mouse acceleration, foreground process, and read-only `inputContext`.
- Read-only `inputContext` diagnostics in `get_user_activity`, `diagnose_pointer`, and input failure paths (session vs console session, window station visibility, thread vs input desktop, job object `JOB_OBJECT_UILIMIT_*` flags, token flags, parent process chain) with `input_context_blocked` classification.
- Configurable foreground rejecting-host policy via `WINSIGHT_SYNTHETIC_INPUT_REJECTING_HOSTS` (default `vmware,vmware-vmx,vmplayer`), reporting `foreground_rejects_synthetic_input` on rejected pointer moves (for example VMware Workstation while a powered-on VM is shown in the active tab; VMware host UI with no running VM in the active tab accepts synthetic input) and attaching non-blocking `warning` (`foreground_may_reject_synthetic_input`) on keyboard tools (`send_key_chord`, `type_keys`, `type_text` keyboard fallback).
- Recovery hints for `foreground_rejects_synthetic_input`, `move_not_applied`, and `uipi_blocked` in `get_winsight_guide` (`errorPolicy`).
- Structured stderr logging (`[winsight] ...`) for `uipi_blocked`, `input_context_blocked`, and `foreground_rejects_synthetic_input`.

### Fixed

- `PointerDispatch` now distinguishes `move_not_applied` (cursor stayed at start position) and `foreground_rejects_synthetic_input` (single `SetCursorPos` failure probe returned `FALSE` with error 0 or configured rejecting foreground host) from `cursor_moved` (cursor moved elsewhere than both start and target).
- `drag` and `hover_element` now route pointer moves through `NativePointerDriver` (`SendInput` with virtual-desktop normalization) instead of FlaUI `Mouse.Position`/`Mouse.MoveTo`.
- `send_key_chord`, `type_keys`, and `type_text` keyboard fallback now report `SendInput` count and Win32 error in structured failures, re-verify foreground window before chord dispatch, and compute keyboard warnings after focus.

## [0.2.2] - 2026-11-19

### Fixed

- `act_verified` with `expect.kind=any_change` (as well as `observeAfter=true` and recovery captures) now reuses the source observation's exact filters (`windowTitle`, `maxResults`, `actionableOnly`, `filterText`, `includeOcr`), eliminating false positives when a filtered observation is followed by a no-op action.
- `observe_scene` now applies `filterText` and `actionableOnly` before capping to `maxResults`, so `truncated` is `true` only when matching results were actually cut by `maxResults` or UIA traversal hit its node limit.
- `act_verified` with `action=activate` on a `MenuItem` and `expect.kind=any_change` now requires actual menu activation evidence (new window/popup, new `Menu`/`MenuItem`/`MenuBar` elements, or `Expanded`/`PartiallyExpanded` state) and returns `dispatched_unconfirmed` with a `click_point` bounds-center hint when Win32 custom menubars (such as VMware `wui.unibar`) ignore UIA `Invoke`.

## [0.2.1] - 2026-11-19

### Added

- `get_user_activity` now returns a `diagnostics` payload (`inputKind`, `cursorMoved`, `cursorPosition`, `inputHookActive`, `lastInputInjectedByOther`, and a bounded `recentInputs` sample list with inter-event deltas) to help diagnose phantom desktop input.
- Opt-in low-level input hook (`WINSIGHT_INPUT_HOOK=1`, off by default) using `LLMHF_INJECTED` / `LLKHF_INJECTED` flags to distinguish real hardware mouse/keyboard input from input injected by other processes (`lastInputSource: "other_injected"`).
- Host-configured, logged per-session yield override (`WINSIGHT_YIELD_OVERRIDE=1`), enabled exclusively through server environment configuration (never via a tool argument) and surfaced in `get_user_activity` (`yieldOverridden`) and `get_agent_state` (`guardrails.yieldOverrideEnabled`).
- Documented the `wait_for_user_idle` -> `observe_scene` -> act recovery loop in `get_winsight_guide` (`yieldRecoveryLoop`), `AgentRecipeCatalog`, tool descriptions, and server instructions.

### Fixed

- `wait_for_user_idle` no longer slices waits into 1000 ms chunks that returned only the final slice's `waitedMs` and timeout message; a single end-to-end async wait loop now honors the full `timeoutMs`, reports total `waitedMs`, emits progress notifications, and validates bounds.
- `get_user_activity` and `wait_for_user_idle` no longer report `sessionLocked=true` on unlocked 64-bit Windows sessions (`WTSINFOEXW.Data.WTSInfoExLevel1.SessionFlags` is now read at its aligned offset 16 instead of offset 12, with an `OpenInputDesktop` / `UOI_NAME` fallback when WTS returns unknown).

## [0.2.0] - 2026-11-19

### Added

- MCP protocol autonomy features:
  - `ServerInstructions` configured at initialization so AI clients receive the preferred control loop and recovery rules without burning a discovery call.
  - Explicit MCP tool annotations (`ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld`) on all 70 tools so clients can auto-approve read-only perception tools.
  - Read-only MCP resources (`winsight://guide`, `winsight://state`, `winsight://windows`, `winsight://scene/latest`) and reusable MCP prompts (`explore_app`, `verify_flow`, `diagnose_ui_bug`).
  - `WINSIGHT_TOOL_PROFILE=compact` to expose a focused 33-tool autonomous subset while keeping `full` (70 tools) as the default.
  - MCP `notifications/progress` ticks on `run_verified_steps`, `run_app_profile`, `wait_any`, and `wait_for_user_idle`.
  - `winsight-configure` accepts `--compact` and `--yield-ms <ms>` to persist `WINSIGHT_TOOL_PROFILE=compact` and `WINSIGHT_YIELD_TO_USER_MS` in client configs (JSON and TOML).
- Autonomous control loop evolutions:
  - `wait_any` races 1..10 competing conditions (`element_exists`, `element_not_exists`, `value_contains`, `window_open`, `window_closed`, `ocr_contains`, `visual_change`) and returns as soon as the first branch matches.
  - `save_verified_history_as_profile` promotes recorded strict `act_verified` steps from the current session into a version-pinned application profile in one call.
  - `capture_scene` supports targeted element zoom/cropping (`targetRef` and `paddingPx`) alongside inline Set-of-Mark PNG output and optional `windowHandle`.
  - `backgroundOnly=true` on `act_verified` and `run_verified_steps` restricts actions to background-safe UIA patterns (`Invoke`, `Toggle`, `SetValue`, `Select`, `Expand`, `Collapse`, `Scroll`) so agents can drive apps without stealing desktop focus or tripping `user_active`.
  - Named multi-app targets: `set_target` accepts an optional `alias`, `switch_target(alias)` switches the active target without juggling PIDs, and `clear_target(alias)` removes a single alias.
  - `watch_ui_events` subscribes to native UIA events (`window_opened`, `menu_opened`, `invoked`, `structure_*`) for a bounded duration to catch transient popups and menus.
  - `audit_ui_accessibility` inspects a window's UIA tree for missing accessible names, missing `AutomationId`s, zero-size visible controls, non-focusable interactive elements, and overlapping bounding boxes.
  - `capture_scene` returns the Set-of-Mark annotated PNG inline as an MCP `ImageContentBlock` by default (`includeImage=true`) and makes `windowHandle` optional.
  - `observe_scene` adds `actionableOnly`, `filterText`, `includeOcr=true` (synthesizing clickable `tN` OCR targets dispatched by `act_verified`), automatic `surfaceHint` (WebView2 port or custom-drawn hint), and automatic `matchingProfiles` discovery.
  - `act_verified` adds `observeAfter=true` (returning a fresh `nextObservation` and structural `diff` in the same call), exploratory postconditions (`value_contains`, `name_contains`, `any_change`, `visual_change`), and structured recovery `hint` payloads with consecutive-failure loop detection.
  - `run_verified_steps` executes 1..20 semantic verified steps in one round trip with per-step observation/verification and automatic `recovery` on failure.
  - `select_file_dialog` provides a fast-path for standard Windows Open/Save (`#32770`) file dialogs.
  - `list_app_profiles` and `delete_app_profile` for discovering and managing saved profiles.
  - `launch_app` automatically binds the launched process as the session default target (`setAsTarget=true`) and surfaces `matchingProfiles`; `close_app` clears the session target when closing it.

### Fixed

- `ManagedStateReader` now reads `System.String` fields before the generic `IsObjectReference` branch (previously returning `"System.String @ 0x..."`).
- `act_verified` now enforces `UserActivityPolicy.CheckYield()` (`WINSIGHT_YIELD_TO_USER_MS`) before consuming an observation or dispatching input.
- `click_and_wait` and `type_and_assert` no longer leak primary locator fields into a separate wait/assert target.
- `DomSceneTool` dispatches asynchronously without blocking on `.GetAwaiter().GetResult()` under `DesktopInputLease`, and avoids repeating WMI/TCP endpoint verification on every 100 ms poll tick.
- `ToolAccessPolicy` and `AppLauncher` properly dispose `Process` handles; verified scene tools share DI-managed singletons via `SceneServices`; `UiaSceneDriver` validates `key_chord` before consuming an observation and tolerates transient `COMException` during resolution.
- `replay_steps` restricts `tracePath` to `.json` files inside the configured trace directory.

### Earlier Unreleased Changes

- French documentation: `README.fr.md` and `docs/fr/` mirror the README and
  every document under `docs/` except the release notes, each version linking
  to the other at the top. A test keeps the French files free of typographic
  stand-ins for ASCII characters and proves it reaches `docs/fr/`.
- `get_user_activity`: time since the last human input, with the input of every
  Winsight process in the session excluded through a shared input ledger, the
  foreground window (its title only for the target process) and whether the
  session is locked. `wait_for_user_idle` waits for a quiet period.
- `WINSIGHT_YIELD_TO_USER_MS`: every screen-changing tool and `run_steps` step
  fails with `user_active` while a person used the desktop more recently.
- `run_steps` stops with `foreground_changed` before a screen-changing step when
  the foreground moved to a window outside the batch's processes
  (`stopOnForegroundChange`, default true); dialogs of the target are tolerated.
- `click_point` and its `run_steps` step accept `focusWindow`, which raises and
  verifies the target window before the point is validated, and `settleMs`.
- `samples/WpfLabApp --pointer-probe`: a raw Win32 window that resolves hover
  once per frame from the last `WM_MOUSEMOVE`, like iced on winit. It also takes
  text through `WM_CHAR` (Ctrl+V pastes), draws its own status line and animates
  after each click.
- `type_keys`: text into the target's foreground window without a UIA element,
  as Unicode keystrokes (Enter and Tab as keys), stopping if the window loses the
  foreground; `mode=paste` saves the clipboard, pastes, and restores every
  memory-block format it saved (`restored`: full, partial or failed).
- `set_clipboard_text` and `read_clipboard_text`, off unless
  `WINSIGHT_ALLOW_CLIPBOARD` is set. Text Winsight writes is kept out of the
  clipboard history, the cloud clipboard and clipboard monitors; traces record
  the size of results, never their content.
- `wait_for_visual_change` and `wait_for_visual_stable`, and the matching
  `run_steps` steps, judged by the SSIM criteria of `compare_visual_baseline` on
  in-memory captures. A `wait_visual_change` step compares against a capture
  taken before the action that precedes it.
- `read_text_visually` and the `run_steps` assert operator `text_visible`,
  through the offline Windows OCR engine, with rectangles in window coordinates.
- `scroll_point` and `hover_point`, and the matching `run_steps` steps: wheel
  notches (vertical or horizontal) and a pointer rest at a coordinate, with the
  coordinate spaces, window checks, `focusWindow` and settle of `click_point`.
- `get_winsight_guide` reports the server build (version with commit, file
  time, process start), the exact registered tools, and `newerBuildAvailable`
  when `tools\run-mcp.ps1` launched it from a source output that has since been
  rebuilt. The server announces `notifications/tools/list_changed` after
  initialization, and the smoke test requires it.
- Codex CLI decision-source evaluation, exact-target/action-bounded external
  suites, a disposable PuTTY port scenario, and structured trial sidecars.
- Independent protected-state grading with explicit unknown coverage, retained
  transient violations, executable hashes and CLI/token provenance.
- Expiring UIA observations and single-use `act_verified` references with explicit
  postconditions, bounded recovery and separate dispatch/verification outcomes.
- Annotated scene captures, authorized WebView2 DOM scenes and a vision fallback
  guarded by unchanged window identity and screenshot bytes.
- Pattern-based scrolling, expansion, multi-selection, focus, chords and drag
  through the verified action surface.
- Executable-version/SHA256-pinned application profiles with runtime parameters,
  preview and stop-on-unverified execution.
- A model-neutral twenty-scenario evaluation runner with independent UI grading.
- `drag`: element-or-point drag paced for WPF OLE drag and drop (hold, drag
  threshold exit, stepped moves, settle on target, configurable delays).
- `get_focused_element`: the element that owns keyboard focus, its process, and
  WebView2 detection with the evidence that decided it.
- `manage_window`: move, resize, set bounds, maximize, minimize and restore,
  reporting the settled state and whether the application constrained it.
- `select_item` accepts `itemNameContains` or `itemNamePattern` for names that
  carry live data, and returns the selected item's full name.
- `wait_for_element` conditions `name_equals`, `name_contains`, `name_matches`
  and `child_count` (with `expectedCount` and `childControlType`).
- `inspect_ui_tree` and `capture_screenshot` accept `mainWindow`.
- `samples/WpfLabApp`: WPF lab with a low-level keyboard hook probe, draggable
  tabs, a tree with time-stamped row names and a tooltip distinct from its HelpText.

### Changed

- `docs/architecture.md` covers the user activity, clipboard and OCR modules,
  the pointer settle, the foreground guard and the visual waits. The README
  tool table lists all 61 tools, and the README layout includes `WpfLabApp`,
  `Winsight.Setup` and `Winsight.Eval`. The two French evaluation documents
  moved from `docs/*.fr.md` to `docs/fr/`.
- Winsight.Core, Winsight.Mcp, the evaluation runner and the tests target
  `net10.0-windows10.0.19041.0` for `Windows.Media.Ocr`. Build outputs moved to
  `bin\<configuration>\net10.0-windows10.0.19041.0`; `run-mcp.ps1`, the smoke
  test and CI follow. No package reference was added.
- `type_text` and `send_key_chord` refuse synthetic keyboard input into a
  WebView2 surface unless `allowWebView2Input=true`; such input has crashed ctfmon.
- `hover_element` reports `tooltip.source` (`popup` or `helpText`) and the
  popup window handle, and searches only the target process's overlay windows.

### Fixed

- Coordinate clicks raced the pointer move: `click_point`, physical clicks on
  elements, `find_element_visually` and verified clicks sent the move and the
  press together, so immediate-mode UIs (iced on winit) acted on the previous
  pointer position: a hover instead of a click, a list closing instead of
  selecting. The pointer now moves, rests 150 ms (`settleMs`), is checked to
  still be there (`cursor_moved` otherwise) and presses without moving again.
- Serialize native desktop input across Winsight processes in the same Windows session.
- Avoid duplicate owned-dialog matches under parent UIA roots.
- Require a unique managed instance for verified managed-state predicates.
- `send_key_chord` lost Shift on extended keys: PageUp, PageDown, Home, End,
  Insert, Delete and the arrows were sent without scan code or extended flag,
  read as keypad keys, and Windows injected a fake Shift release around them
  (Ctrl+Shift+PageUp arrived as Ctrl+PageUp). Chords now carry scan codes and
  `KEYEVENTF_EXTENDEDKEY`, with paced transitions for low-level hooks.
- `hover_element` returned the element's HelpText on the first poll, before the
  real tooltip popup opened; the popup now wins and HelpText is a fallback.
- `inspect_ui_tree`, `capture_screenshot`, `send_key_chord` focus and
  window-relative coordinates targeted an open tooltip or menu when it was the
  topmost window; transient overlays are now skipped.
- `click_point`, `drag` and verified point actions refused any point above a
  WebView2 surface as occluded: the surface is a `Chrome_WidgetWin_1` child owned
  by `msedgewebview2.exe`. A cross-process child whose root ancestor is the
  target window is now accepted; other windows and processes on top still refuse.

See [verified control](docs/verified-control.md) for contracts and validation limits.

## [0.1.0] - 2026-09-05

### Fixed

- Reject conflicting process IDs in `act_and_observe` before dispatch.
- Preserve applications that refuse graceful closure; termination requires `force=true`.
- Respect global dry-run before all visual baseline writes and captures.
- Refuse coordinate clicks on an occluding process or an unexpected window.
- Bind visual grounding to its captured HWND and geometry; reject stale captures
  and propagate refused clicks as outer failures.
- Isolate trace/replay filesystem failures from tool outcomes and allocate unique
  screenshot/replay filenames for rapid and concurrent requests.
- Report missing vision fixtures as skipped tests, with a separately selectable
  `Category=Vision` gate and retained CI test reports.

### Added

- Windows x64 per-user setup with an English/French wizard, bundled runtime,
  client selection for Codex, Claude Desktop, Claude Code and Gemini CLI,
  configuration backups, conflict protection and scoped unregistration.
- Reproducible setup/ZIP build script and CI installer artifacts.

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

#### Phase 1: UIA + CLRMD foundation

- `Winsight.Core` engine targeting `net10.0-windows`, x64.
- `Winsight.Mcp` server speaking MCP over stdio, built on
  ModelContextProtocol 1.0.0 and `Microsoft.Extensions.Hosting`.
- `samples/SampleWpfApp`: a deliberately minimal WPF counter app used as
  the integration target.
- 5 tools:
  - `list_windows`: top-level window enumeration via FlaUI UIA3.
  - `inspect_ui_tree`: bounded BFS of the UIA tree (max depth 20).
  - `click_element`: Invoke / Toggle / mouse fallback.
  - `type_text`: Value pattern / keyboard fallback.
  - `read_managed_state`: CLRMD non-suspending attach + heap walk.
- Solution layout: `Directory.Build.props` (treat warnings as errors),
  `Directory.Packages.props` (central package management), `.slnx`
  solution file (.NET 10 format).

#### Phase 2: perception, oracle, vision

- `WindowCapture`: PrintWindow with `PW_RENDERFULLCONTENT` so
  DirectX-composed WPF surfaces capture without going black.
- `capture_screenshot` and `find_element_visually` can target modal dialogs,
  popups, and secondary windows with `windowTitle`; default capture now uses
  visible top-level Win32 windows in z-order instead of UIA desktop children.
- `WebView2Detector` + `WebView2CommandLineParser`: single-WMI-query
  walk of the host's process tree, finds `msedgewebview2.exe`
  descendants, parses `--remote-debugging-port`, cross-references
  `IPGlobalProperties.GetActiveTcpListeners()`. Returns an actionable
  hint (in tool output) when no usable CDP endpoint is found.
- `DiagnosticsObserver.ObserveCounters`: EventPipe-based EventCounter
  aggregator. Uses `Microsoft.Diagnostics.NETCore.Client` and
  `Microsoft.Diagnostics.Tracing.TraceEvent`, the same channel as
  `dotnet-counters` / `dotnet-trace`, so it is invisible to enterprise
  EDR products that flag CLRMD attach.
- `DiagnosticsObserver.CaptureEvents`: captures arbitrary EventPipe
  events from one or more providers, normalizes payloads via
  `PayloadNormalizer` (depth-capped recursion, JSON-safe types), bounded
  by configurable `maxEvents` and `durationMs`.
- `IElementGrounder` interface + `AnthropicVisionGrounder`
  implementation: calls the Anthropic Messages API with the screenshot
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

#### Phase 2.1: WebView2 CDP driving

- `Cdp` module (`Winsight.Core/Modules/Cdp`): dependency-free Chrome
  DevTools Protocol client over a raw `ClientWebSocket` (no Playwright /
  Puppeteer). `CdpTargetDiscovery` lists debuggable targets via
  `/json/list`; `CdpClient` attaches to a page target, correlates command
  ids, skips event frames, and times out per command
  (`WINSIGHT_CDP_TIMEOUT_MS`, default 10000 ms).
- `CdpKeyEventFactory`: expands text into `Input.dispatchKeyEvent`
  keyDown/keyUp sequences (`\n`/`\r\n` → Enter with
  `windowsVirtualKeyCode` 13, `\t` → Tab). Real key events are what make
  typing reach keydown-driven surfaces such as xterm.js terminals hosted
  in `HwndHost`/WebView2, the exact case where UIA `type_text` fails.
- 3 additional tools (10 → 13):
  - `list_cdp_targets`: targets of a CDP endpoint, with a hint when no
    `page` target is available yet.
  - `evaluate_javascript`: `Runtime.evaluate` with `returnByValue` +
    `awaitPromise`; script exceptions surface as structured failures.
  - `type_into_webview`: types text as real key events, with an optional
    `focusSelector` that fails explicitly when nothing matches.
- 15 new unit tests (target parsing, key event sequences, response/
  exception parsing). `smoke-mcp.ps1` expected tool count bumped to 13.

#### Phase 2.2: UIA test ergonomics

- `ElementLocator` extended with `controlType` (case-insensitive),
  `nameContains` (case-insensitive substring), and `index` (explicit pick
  among multiple matches). Matching logic extracted into a pure,
  unit-tested `LocatorMatcher`.
- **Strict locator resolution**: when several elements match and no
  `index` is given, the call fails and reports the candidates
  (id/name/controlType/window) in the error message and the `data`
  payload, instead of silently acting on the first BFS hit. When nothing
  matches, near-miss suggestions are returned.
- **Multi-window + modal dialog support**: element searches now cover
  every visible top-level window of the process, enumerated through Win32
  `EnumWindows` and rooted at their handles (`NativeWindowEnumerator`).
  This is required because UIA reparents owned windows (modal dialogs)
  under their owner and then omits them from both desktop and owner child
  listings, making them unreachable through tree walks. A new
  `windowTitle` parameter scopes any UIA tool to one window.
- `get_element_state` tool + `ElementStateReader`: UI-level oracle
  returning name, Value-pattern content, toggle state, selection
  (item + container), enabled/visible/focus flags, and geometry.
- `wait_for_element` tool + `ElementWaiter`: condition-based
  synchronization (`exists`, `not_exists`, `enabled`, `visible`) with
  configurable timeout (`WINSIGHT_UIA_WAIT_TIMEOUT_MS`, capped at
  120000 ms) and poll interval; returns elapsed time, match count, and
  the final element state.
- `select_item` tool: selects an item by visible text in a ComboBox,
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
  sequentially via `DisableTestParallelization`: concurrent scenarios
  race on physical input and the live desktop tree).

#### Phase 2.3: UIA focus, semantic matching, and stability

- `send_key_chord` tool: parses chords such as `Ctrl+Shift+N`, focuses
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
- `hover_element` tool: moves the mouse to an element center and captures
  newly appearing nearby tooltips, filtering tooltip snapshots that were
  already present before the hover.
- `SampleWpfApp` now covers a global keyboard shortcut, tooltip hover,
  and semantic descendant-text locators for deterministic E2E validation.
- 2 additional tools (16 → 18); `smoke-mcp.ps1` expected tool count
  bumped to 18.

#### Phase 2.4: configurable capture output

- `capture_screenshot` and `find_element_visually` resolve their default
  capture path through a shared `ScreenshotOutput` helper that honors the
  `WINSIGHT_OUTPUT_DIR` environment variable (fallback `%TEMP%\winsight`), so a
  supervisor agent can read captures from a known or shared directory. An
  explicit `outputPath` argument still takes precedence; the directory is
  created on first write. Tool count unchanged (18).

#### Phase 2.5: local ONNX vision grounder

- `OnnxLocalGrounder`: fully local `IElementGrounder` selected with
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

#### Phase 2.6: visual regression baselines

- `VisualRegression` module (`Winsight.Core/Modules/VisualRegression`):
  baseline-based UI screenshot comparison in pure .NET: global SSIM (Gaussian
  11x11, sigma 1.5) plus a worst-block local SSIM (catches small localized
  changes the global mean misses), plus a pixelmatch-style YIQ pixel-diff with
  anti-aliased-edge detection (robust to ClearType subpixel rendering). Ignore
  rectangles exclude dynamic regions; diff heatmaps are emitted as PNGs.
- `WindowCapture.CaptureClientArea`: captures the window client area only (no
  title bar/borders), so OS chrome variance (theme/DPI/focus) does not produce
  false diffs. The visual baseline tool uses it.
- `compare_visual_baseline` MCP tool: captures the target window client area,
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

#### Phase 2.7: coordinate clicks, closing the perception-to-action loop

- `click_point` MCP tool: clicks a coordinate without UIA element location, the
  action partner of `find_element_visually` and the escape hatch for surfaces
  UIA cannot address (custom Canvas, SkiaSharp, DirectX). `coordinateSpace`
  defaults to `window` (same origin as a full-window screenshot, so a grounded
  `x,y` passes straight through) or `screen` for absolute desktop coordinates;
  out-of-bounds window-relative points fail explicitly with the window size.
  Returns the absolute `screenX/screenY` clicked.
- `ActionInvoker.ClickPoint`: physical mouse click (single/double/right) at a
  window-relative or screen point, backed by `WindowGeometry` (resolves the
  topmost/title-scoped window origin, mirroring `WindowCapture.Capture`) and the
  pure `WindowPointMapper` (window→screen translation + bounds check).
- `find_element_visually` now enriches `GroundedElement` with `screenX/screenY`
  (window origin added server-side, so no manual coordinate math) and accepts an
  opt-in `act=single|double|right` to click the grounded point in the same call,
  returning both the grounding and the click result.
- 20 total tools; `smoke-mcp.ps1` expected tool count bumped to 20.

#### Phase 2.8: multi-window discoverability

- `list_windows` now enumerates through Win32 instead of UIA desktop children,
  so owned surfaces that UIA hides (modal dialogs, MessageBoxes, dropdown
  menus, flyouts, tooltips) are discoverable, not merely reachable. Each
  `WindowDescriptor` carries `kind` (main/dialog/popup/menu/tooltip/other),
  `className`, `ownerHandle`, and `isModal`; windows are returned in z-order
  (topmost first), so the leading entry is the active overlay.
- `list_windows` gains an optional `processId`: with it, the listing is
  exhaustive (including untitled dialogs, popups, and menus of that process),
  giving an agent the full window stack of the app under test; without it,
  desktop-wide discovery keeps the named-windows-only contract to avoid noise.
- `WindowClassifier`: pure, unit-tested classification from Win32 class name,
  caption/popup styles, and ownership; `isModal` is derived from an owned window
  whose owner is disabled.

#### Phase 2.9: application lifecycle

- `launch_app` MCP tool: starts an executable and blocks until its window is
  present (main window, or one matching `waitForWindowTitle`), returning the new
  `processId` and a classified window descriptor ready to drive. Removes the
  shell-out-then-poll-`list_windows` startup race. Fails explicitly when the
  executable is missing, the process exits during startup, or no window appears
  within the timeout (default 15s, max 120s).
- `close_app` MCP tool: graceful shutdown (`CloseMainWindow`) with a kill
  fallback, or `force=true` to kill the process tree immediately; an
  already-gone process is reported as success.
- `AppLauncher` (`Winsight.Core/Modules/Lifecycle`) backs both, reusing
  `WindowDiscovery` to detect the ready window.
- 22 total tools; `smoke-mcp.ps1` expected tool count bumped to 22.

#### Phase 2.10: batch step execution

- `run_steps` MCP tool: runs a sequence of UI steps in one call instead of one
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

#### Phase 2.11: inline image screenshots

- `capture_screenshot` now returns a `CallToolResult` with a text block (the
  unchanged `OperationResult`/`ScreenshotResult` JSON, so existing parsers keep
  working) plus an inline PNG image block, so a supervising agent sees the UI
  directly instead of reading the file from disk. `includeImage=false` opts back
  into text-only.
- `compare_visual_baseline` likewise attaches the diff heatmap PNG inline when a
  comparison runs, so the agent sees where the UI changed.
- `ToolSafety.SafeInvokeContent`: content-returning variant of the boundary
  guard: a non-fatal throw becomes an error `CallToolResult` whose text block
  carries the same `OperationResult.Failure` JSON, so the error shape is
  identical whether a tool returns a string or rich content.
- No new tools (23 total); the two capture tools changed return type only.

#### Phase 2.12: causal exception oracle

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

#### Phase 2.16: first-class assertions

- `assert_element` MCP tool and an `assert` step in `run_steps`: assert a
  condition on an element and get a clear pass/fail with expected-vs-actual
  detail, instead of abusing `wait_for_element`. Operators: equals, not_equals,
  contains, not_contains (on value, else name); greater_than, less_than
  (numeric); enabled, disabled, visible, hidden; checked, unchecked; selected;
  exists. A failing assertion returns `success: false` (and fails the batch step).
- `AssertionEvaluator` computes the verdict as a pure, unit-tested function.
- 30 total tools; `smoke-mcp.ps1` expected tool count bumped to 30.

#### Phase 2.15: change perception (snapshot_ui / diff_ui)

- `snapshot_ui` and `diff_ui` MCP tools, the feedback sense: capture a
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

#### Phase 2.14: affordance perception (describe_ui)

- `describe_ui` MCP tool: answers "what can I do here?" by returning a window's
  actionable elements as a compact, position-ordered list (top-to-bottom,
  left-to-right) instead of a full UIA tree, so an agent orients before acting.
  Each `UiInteractable` carries control type, label, value/toggle/selection and
  enabled/visible state, bounds, and a `recommendedLocator`, the smallest
  criterion (unique automationId, else unique name, else controlType+index) ready
  to feed click_element / type_text / select_item.
- `AffordanceScanner` (`Winsight.Core/Modules/UiAutomation`) walks the process
  windows breadth-first, keeps the interactable control types, and reuses
  `ElementStateReader.BuildState`; `AffordanceRecommender` computes the locators
  as a pure, unit-tested function.
- 27 total tools; `smoke-mcp.ps1` expected tool count bumped to 27.

#### Phase 2.13: session target

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

- `ToolSafety.SafeInvoke` / `SafeInvokeAsync`: universal outer guard at
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
- `WindowCapture`: `PrintWindow` failure now includes the Win32
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
  `Win11Forge` (elevated app: UIA/CLRMD failures now return clean
  structured errors instead of generic `"An error occurred invoking 'X'"`,
  and the transport survives every failure).

#### Tooling

- `.mcp.json` at the repository root for project-scoped Claude Code
  registration.
- `tools/smoke-mcp.ps1`: JSON-RPC smoke test that spawns the server,
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
- The default cloud backend of `find_element_visually` requires
  `ANTHROPIC_API_KEY` and transmits the screenshot to the configured API.
  The local ONNX backend requires provisioned model files instead. The other
  tools work without a vision API key.
- Windows UAC / UIPI: a winsight host running at standard-user level
  cannot enumerate, inspect, or attach to processes that require
  administrator (apps shipping `<requestedExecutionLevel
  level="requireAdministrator" .../>`). The tool calls now return clean
  structured failures in that case rather than generic errors, but
  driving such a target requires running winsight at the same or higher
  integrity level.
