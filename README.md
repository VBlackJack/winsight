# Winsight

*[Version française](README.fr.md)*

> A Windows-native testing framework for AI agents.

Winsight is a [Model Context Protocol](https://modelcontextprotocol.io) server
and SDK that lets AI agents (Claude Code, Cursor, Windsurf, custom agents)
inspect, drive, and assert against Windows desktop applications under
development, going beyond what UI Automation alone can do.

## Get started in 60 seconds

**Recommended:** download `Winsight-Setup-0.2.2-win-x64.exe` from
[v0.2.2](https://github.com/VBlackJack/winsight/releases/tag/v0.2.2) and follow the
wizard. Select **Codex, Claude Desktop, Claude Code, and/or Gemini CLI**, then
restart them. No terminal command or .NET installation is needed. The clients
themselves must already be installed. When updating, setup closes any running
Winsight servers itself, so files in use no longer block it; restart the client
applications that started them afterwards.
See the [installation guide](docs/installation.html).

For the portable Windows x64 build, download `winsight-0.2.2-win-x64.zip` and
`SHA256SUMS.txt` from [v0.2.2](https://github.com/VBlackJack/winsight/releases/tag/v0.2.2).
Verify the archive with `Get-FileHash -Algorithm SHA256`, extract it, and register
the absolute path to `winsight-mcp.exe` as your MCP client's stdio command.
The archive includes the .NET runtime; no SDK installation is required.
Vision model weights and API credentials are configured separately. See the
[release notes](docs/release-notes-v0.2.2.md) for configuration and validation limits.

To build from source:

```powershell
git clone https://github.com/VBlackJack/winsight.git
cd winsight
dotnet build -c Release
.\tools\smoke-mcp.ps1                  # confirms the server speaks MCP and lists 71 tools (count derived from the sources)
```

Then in Claude Code, the `.mcp.json` at the repository root registers the
server automatically when you open the project. Run `/mcp` in Claude Code
to confirm `winsight` shows as connected, and try a prompt like:

> Launch `samples\SampleWpfApp\bin\Release\net10.0-windows\SampleWpfApp.exe`,
> click the Increment button five times, then read `_internalCount` on
> `Winsight.Samples.SimpleCounter.ViewModels.CounterViewModel` to confirm
> it equals 5.

The agent should compose `list_windows` → `click_element` → `read_managed_state`
on its own.

## Verified control

Use `observe_scene` and `act_verified` for expiring targets and explicit result verification.
Native UIA, authorized WebView2 DOM, vision fallback, application profiles and a twenty-scenario evaluation runner are documented in [verified control](docs/verified-control.md).

## Why

Game bots have driven characters in DirectX worlds for two decades. Testing
bots for desktop applications have lagged behind, stuck on top of the UI
Automation tree. Winsight closes that gap by combining four orthogonal
capabilities under a single tool surface that an LLM can compose:

| Layer       | What it offers                                                                | Backed by                                       |
| ----------- | ----------------------------------------------------------------------------- | ----------------------------------------------- |
| Lifecycle   | Launch an app and wait for its window, close it down                          | `Process` + window-ready polling                |
| Orchestration | Run a whole scenario (act, sync, assert, capture) in one batch call         | `StepRunner` over the action/sync/oracle layers |
| Discovery   | Window enumeration (classified: main/dialog/popup/menu/tooltip, owner, modal), UIA tree inspection (filterable, value-aware), affordance summary (actionable elements + recommended locators) | FlaUI (UIA3) + Win32 window enumeration         |
| Action      | Strict element location, click styles, coordinate clicks, type, chords, hover, item selection | FlaUI patterns + SendInput / Win32 fallback      |
| Sync        | Condition-based waits (exists / not_exists / enabled / visible / value / stable) | UIA polling with structured outcomes          |
| Oracle (UI) | Element state snapshots (value, toggle, selection, focus, geometry), first-class assertions | FlaUI patterns                                  |
| Feedback    | UI change diff after an action (windows opened/closed, elements added/removed/changed) | Snapshot + pure structural diff          |
| Oracle      | Read private fields and managed state without `InternalsVisibleTo`            | Microsoft.Diagnostics.Runtime (CLRMD)           |
| Diagnostics | Subscribe to EventCounters, capture EventPipe events (exceptions, telemetry), capture exceptions causally during a batch | Microsoft.Diagnostics.NETCore.Client + TraceEvent |
| Capture     | Window screenshot via `PrintWindow` + `PW_RENDERFULLCONTENT`, returned inline as an MCP image | Win32 `PrintWindow`                             |
| WebView2    | Locate embedded WebView2 instances, surface their CDP debugging endpoint      | WMI process tree + TCP listener cross-reference |
| Perception  | Visual grounding from a natural-language description (cloud or fully local)   | Anthropic Messages API / local Qwen2.5-VL ONNX  |

## Status

Phase 1 + Phase 2 + tool boundary hardening + UIA test ergonomics + visual
regression v0 + coordinate clicks + multi-window discoverability + app
lifecycle + verified control + surfaces without UI Automation (iced, winit,
egui) + shared-desktop awareness: fully bootable, end-to-end validated against
`SampleWpfApp` (golden path), `WpfLabApp` (WPF shell input paths and a raw
Win32 probe that behaves like iced on winit) and a real WPF application
(`Win11Forge`). Current coverage:
Core/MCP unit tests and E2E scenarios that drive a live interactive Windows
desktop. See the [audit and remediation evidence](docs/audit-2026-09-05.md)
for measured results. Vision integration tests are explicitly skipped when
their model/image/reference fixtures are absent.

| Capability                      | Phase 1 | Phase 2 | Phase 3 |
| ------------------------------- | ------- | ------- | ------- |
| Window discovery                | yes     |         |         |
| Classified windows (dialog/popup/menu/tooltip, owner, modal) |   | yes     |         |
| Affordance summary (`describe_ui`: actionable elements + locators) |   | yes     |         |
| Change perception (`snapshot_ui` / `diff_ui`: what an action changed) |   | yes     |         |
| App lifecycle (launch + wait for window, close) |   | yes     |         |
| Session target (set_target: omit processId per call) |   | yes     |         |
| UIA tree inspection             | yes     |         |         |
| Click / type via UIA            | yes     |         |         |
| Coordinate clicks (window/screen space) |   | yes     |         |
| Batch step execution (`run_steps`, one round trip) |   | yes     |         |
| First-class assertions (`assert_element` + assert step, clear pass/fail) |   | yes     |         |
| Causal exception oracle (`run_steps captureExceptions`) |   | yes     |         |
| Strict locators + modal dialogs |         | yes     |         |
| Element state oracle + waits    |         | yes     |         |
| Focused key chords + tooltip hover |      | yes     |         |
| Item selection (combo/list/tab) |         | yes     |         |
| Read managed field via CLRMD    | yes     |         |         |
| Window screenshot (PrintWindow) |         | yes     |         |
| WebView2 CDP endpoint discovery |         | yes     |         |
| WebView2 CDP drive (eval / type) |        | yes     |         |
| EventPipe counter observer      |         | yes     |         |
| EventPipe arbitrary event capture |       | yes     |         |
| Vision grounding (Claude API)   |         | yes     |         |
| Vision grounding (local Qwen2.5-VL ONNX) |  | yes     |         |
| Visual regression (SSIM + pixel diff) |   | yes     |         |
| Visually grounded click (find + act) |    | yes     |         |
| Verified control (`observe_scene` + `act_verified`, scenes, profiles) |  | yes |  |
| Pointer settle for immediate-mode UIs, coordinate wheel and hover |  | yes |  |
| Text input without a UIA element (`type_keys`, clipboard) |  | yes |  |
| Visual waits (change, stable) and OCR text reading |  | yes |  |
| Shared-desktop awareness (user activity, yield, foreground guard) |  | yes |  |
| Exploratory agent (Sonnet 4.6)  |         |         | yes     |

## Repository layout

```
winsight/
├── src/
│   ├── Winsight.Core/      Engine: UIA, CLRMD, capture, diagnostics, vision, OCR, user activity, models
│   ├── Winsight.Mcp/       MCP server executable (stdio) + tool boundary guard
│   └── Winsight.Setup/     Per-user installer helper (client registration)
├── samples/
│   ├── SampleWpfApp/       Counter app used as the integration target
│   └── WpfLabApp/          WPF shell lab; --pointer-probe opens a raw Win32 window that behaves like iced
├── tests/
│   ├── Winsight.Core.Tests xUnit unit tests for the engine layer
│   ├── Winsight.Mcp.Tests  xUnit unit tests for the MCP tool boundary (ToolSafety, wrappers)
│   └── Winsight.E2E        End-to-end scenarios on SampleWpfApp and WpfLabApp
├── tools/
│   ├── Winsight.Eval/      Model-neutral evaluation runner
│   ├── release-preflight.ps1 Pre-release checks (clean tree, notes, tag, CI)
│   ├── run-mcp.ps1         Runs a copy of the built server, so the build stays unlocked
│   └── smoke-mcp.ps1       JSON-RPC smoke test for the stdio server
└── docs/                   English documentation; docs/fr/ holds the French versions
    ├── architecture.md
    ├── local-onnx-grounder.md
    ├── runbook.md
    ├── tools-reference.md
    └── verified-control.md
```

## Build

Requires the .NET 10 SDK. The engine, server and tests target
`net10.0-windows10.0.19041.0` (Windows 10 version 2004 or later) for the
Windows OCR API; restore downloads the matching Windows SDK reference pack.

```powershell
dotnet restore
dotnet build
dotnet test tests\Winsight.Core.Tests -c Release         # engine unit tests
dotnet test tests\Winsight.Mcp.Tests -c Release          # MCP boundary unit tests
dotnet test tests\Winsight.E2E -c Release                # E2E (drives the sample app on the live desktop)
```

The E2E suite launches `SampleWpfApp.exe`, exercises the desktop-facing
workflow, and asserts the results. It requires an unlocked interactive desktop
with focus available to the test process; Windows foreground-window restrictions
can make focus-dependent cases fail in background or remote sessions.
`FieldReportScenarioTests` covers the 0.5.0 field-report fixes on live windows:
the screen-capture border mask, the OCR language fallback, title withholding,
strict `actionableOnly`, and `region_uniform`.

### Release checklist

Before tagging, run `tools\release-preflight.ps1` from the repository root. It
changes nothing and checks that the working tree is clean, `HEAD` equals
`origin/main`, the release notes and a dated changelog entry exist for the
version in `Directory.Build.props`, the tag is absent locally and on `origin`,
and the CI run for `HEAD` succeeded. Exit code `0` means ready, `2` means a
prerequisite (`git` or `gh`) is missing, `3` means a check failed.

## Running the MCP server

The server speaks MCP over stdio. Build it once, then register the wrapper
script with your client:

```powershell
dotnet build src\Winsight.Mcp -c Release
```

### Recommended client config

```json
{
  "mcpServers": {
    "winsight": {
      "command": "powershell",
      "args": [
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        "G:\\_Projects\\winsight\\tools\\run-mcp.ps1"
      ]
    }
  }
}
```

`tools\run-mcp.ps1` copies the built MCP server to `%TEMP%\winsight-mcp-runtime`
before launching it. That keeps `src\Winsight.Mcp\bin` rebuildable while a client
is connected; direct launches of `src\Winsight.Mcp\bin\...\winsight-mcp.dll`
hold a file lock and can block `dotnet build`.

### Where to put the config

| Client      | Config file                                              |
| ----------- | -------------------------------------------------------- |
| Claude Code | `%USERPROFILE%\.claude.json` (per-user) or `.mcp.json` at the repo root |
| Cursor      | `~/.cursor/mcp.json`                                     |
| Windsurf    | `~/.codeium/windsurf/mcp_config.json`                    |

After registration, in Claude Code run `/mcp` to confirm the `winsight`
server is connected and lists all 71 tools (set `WINSIGHT_TOOL_PROFILE=compact` for a focused 33-tool autonomous subset).
After rebuilding Winsight, restart the client session: a session keeps the
server process, and the tool list, it started with. `get_winsight_guide`
reports the build a session runs, its exact tool list and, when launched
through `tools\run-mcp.ps1`, whether a newer build is waiting
(`newerBuildAvailable`).

### Smoke check from the shell

To confirm the server starts cleanly (independent of any client):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\run-mcp.ps1
```

The process should remain open waiting for stdio input. Press
`Ctrl+C` to exit. Logs go to stderr; stdout is reserved for MCP
protocol traffic.

To run the JSON-RPC smoke test, use `tools\smoke-mcp.ps1`. It derives the
expected tool count from the sources; pass `-ExpectedToolCount N` to override it.

### Optional: enable visual grounding

The `find_element_visually` tool needs a vision backend. The default backend
calls the Anthropic Claude vision API.

| Variable                    | Required | Default                                  |
| --------------------------- | -------- | ---------------------------------------- |
| `WINSIGHT_VISION_BACKEND`   | no       | `anthropic` (`anthropic` or `onnx`)      |
| `ANTHROPIC_API_KEY`         | yes, for `anthropic` | -                           |
| `WINSIGHT_VISION_MODEL`     | no       | `claude-sonnet-4-6`                      |
| `WINSIGHT_VISION_API_URL`   | no       | `https://api.anthropic.com/v1/messages`  |
| `WINSIGHT_VISION_MIN_CONFIDENCE` | no  | `0.5` (minimum confidence required before `find_element_visually` executes `act`) |
| `WINSIGHT_VISION_ONNX_PATH` | yes, for `onnx` | -                                |
| `WINSIGHT_VISION_EP`        | no, for `onnx` | `cpu` (DirectML is not enabled in this build) |

> **Privacy note**: with the `anthropic` backend, the screenshot is sent to
> the configured API. `WINSIGHT_VISION_BACKEND=onnx` runs a local Qwen2.5-VL
> model fully on-host (see
> [docs/local-onnx-grounder.md](docs/local-onnx-grounder.md) for provisioning)
> and never falls back to cloud. If the selected backend is not configured,
> every other tool keeps working; only `find_element_visually` returns an error
> when invoked.

### Optional: redirect capture output

By default `capture_screenshot` and intermediate captures/diff maps taken by
`find_element_visually` and `compare_visual_baseline` write PNGs under
`%TEMP%\winsight`. `capture_screenshot` (and the `compare_visual_baseline` diff)
also return the PNG inline as an MCP image, so an in-process agent sees the UI
without touching disk; the file path still matters for an out-of-process
supervisor. Set the `WINSIGHT_OUTPUT_DIR` environment variable to
redirect captures into a directory a supervisor agent can read, for example a
folder shared with the agent host. The directory is created on first write;
an explicit `outputPath` argument to `capture_screenshot` always takes
precedence.

| Variable             | Required | Default            |
| -------------------- | -------- | ------------------ |
| `WINSIGHT_OUTPUT_DIR`| no       | `%TEMP%\winsight`  |

## Recommended agent loop

For AI agents, prefer the high-level tools first and fall back to primitives only
when needed:

1. `launch_app` or `list_windows`, then `set_target`.
2. `observe` to get windows, actionable elements, recommended locators, likely
   actions, session memory, and optionally a screenshot.
3. `plan_steps` with the user goal.
4. `run_steps` with `dryRun=true`.
5. `act_and_observe` or `run_steps` with `dryRun=false`.
6. `repair_locator` if a locator fails or becomes ambiguous.

`get_winsight_guide` exposes this playbook directly through MCP so an agent can
recover even when external docs are not in context.

### Optional: visual regression baselines

`compare_visual_baseline` captures the target window and compares it to a
baseline PNG using SSIM plus pixel diff. Baselines are stored outside the repo.
If a baseline does not exist, or `updateBaseline=true`, the current capture is
copied as the baseline and the result verdict is `baseline_created` or
`baseline_updated`.

| Variable                          | Required | Default                                      |
| --------------------------------- | -------- | -------------------------------------------- |
| `WINSIGHT_VISUAL_BASELINE_DIR`    | no       | `%LOCALAPPDATA%\winsight\visual-baselines`   |
| `WINSIGHT_VISUAL_SSIM_THRESHOLD`  | no       | `0.98`                                       |
| `WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD` | no  | `0.85`                                       |
| `WINSIGHT_VISUAL_PIXEL_THRESHOLD` | no       | `0.005`                                      |
| `WINSIGHT_VISUAL_COLOR_THRESHOLD` | no       | `0.1`                                        |

## Tools exposed

See [docs/tools-reference.md](docs/tools-reference.md) for the full schema.

For measured model trials and application-specific suites, see
[model evaluation](docs/model-evaluation.md)
([français](docs/fr/model-evaluation.md)).

| Tool                          | Purpose                                                |
| ----------------------------- | ------------------------------------------------------ |
| `set_target` / `get_target` / `clear_target` | Set/read/clear the session default process so process-scoped tools can omit `processId` |
| `observe`                     | One-call orientation: windows, actionable elements, recommended locators, likely actions, memory, optional screenshot |
| `plan_steps`                  | Draft a conservative `run_steps` plan from a natural-language goal |
| `repair_locator`              | Rank replacement locators from the current UI and session memory |
| `run_steps`                   | Run a sequence of UI steps (act/sync/assert/capture) in one call, with a per-step journal, dry-run mode, trace replay, and optional causal exception capture |
| `replay_steps`                | Re-run a `run_steps` replay artifact, dry-run by default |
| `act_and_observe`             | Execute one step and return before/after UI diff plus current affordances |
| `click_and_wait`              | Click and synchronize in one call, returning action/wait/diff feedback |
| `type_and_assert`             | Type and immediately assert observable state |
| `get_agent_state`             | Compact target, guardrail, memory, last-failure, and embedded recipe context |
| `get_session_memory` / `clear_session_memory` | Inspect or reset observed locators and recent repairable failures |
| `get_winsight_guide`          | Embedded agent playbook for the preferred loop and repair policy, plus the running build and its registered tools |
| `observe_scene`               | Expiring observation with `eN` target references, windows, focus, states and supported actions |
| `act_verified`                | Act once on an `observe_scene` reference and verify an explicit postcondition |
| `capture_scene`               | Capture a window of an observation, annotated with its `eN` references |
| `observe_dom_scene`           | Observe an authorized WebView2 page with the same observation contract |
| `locate_scene_target`         | Vision fallback that adds a located target to a new observation |
| `save_app_profile` / `run_app_profile` | Save and run a recipe pinned to the executable version and SHA256 |
| `launch_app`                  | Start an app and wait until its window is ready (returns a classified descriptor) |
| `close_app`                   | Request graceful closure, or explicitly force termination |
| `list_windows`                | Enumerate top-level windows (classified; per-process surfaces dialogs/popups/menus) |
| `inspect_ui_tree`             | Return the UIA tree for a process (filterable, value-aware) |
| `describe_ui`                 | List a window's actionable elements with ready-to-use locators ("what can I do here?") |
| `snapshot_ui` / `diff_ui`     | Capture a UI baseline and report what an action changed (windows, elements, field values) |
| `click_element`               | Click a located element (single/double/right, strict locators) |
| `click_point`                 | Click a window-relative or bounded screen coordinate (acts on a visually grounded point); the pointer settles before the press, and `focusWindow` raises the target first |
| `type_text`                   | Type into a located element (refuses WebView2 targets) |
| `send_key_chord`              | Focus a window and send a keyboard chord with scan codes and extended-key flags |
| `get_focused_element`         | Report the element that owns keyboard focus, and whether it is inside WebView2 |
| `get_user_activity`           | Report whether a person is using the desktop: time since the last human input (Winsight's own input excluded), foreground window, locked session |
| `wait_for_user_idle`          | Wait until nobody has used the mouse or keyboard for a given time |
| `scroll_point` / `hover_point` | Turn the mouse wheel, or rest the pointer, at a coordinate, with the checks of `click_point` |
| `type_keys`                   | Type text into a window without a UIA element to type into (Unicode keystrokes, or a paste that restores the clipboard) |
| `set_clipboard_text` / `read_clipboard_text` | Write or read the clipboard text; off unless `WINSIGHT_ALLOW_CLIPBOARD` is set |
| `wait_for_visual_change` / `wait_for_visual_stable` | Wait until a window or region looks different, or stops changing, instead of sleeping |
| `read_text_visually`          | Read the text a window draws with the offline Windows OCR engine, with rectangles in window coordinates |
| `hover_element`               | Hover an element and capture/assert its tooltip popup (or HelpText fallback) |
| `select_item`                 | Select an item by exact name, substring or pattern in a ComboBox/ListBox/Tab/Tree |
| `drag`                        | Drag between elements or points, paced for OLE drag and drop |
| `manage_window`               | Move, resize, maximize, minimize or restore a window    |
| `get_element_state`           | Read an element's state (value, toggle, selection, focus) |
| `wait_for_element`            | Wait until an element exists/disappears/enables/shows, value or name matches, child count reached, or state is stable |
| `assert_element`              | Assert a condition on an element (equals/contains/greater_than/checked/enabled/...) with clear pass/fail |
| `read_managed_state`          | Read a managed field via CLRMD attach                  |
| `capture_screenshot`          | PNG capture of a topmost or title-scoped window, returned inline as an MCP image (PrintWindow PW_RENDERFULLCONTENT) |
| `compare_visual_baseline`     | Compare a captured window to a baseline PNG (SSIM + pixel diff) |
| `detect_webview2_endpoints`   | Find WebView2 children and report any CDP endpoint     |
| `list_cdp_targets`            | List debuggable targets exposed by an authorized WebView2 CDP endpoint |
| `evaluate_javascript`         | Evaluate JavaScript inside an authorized WebView2 page over CDP |
| `type_into_webview`           | Type into authorized WebView2 content using CDP key events |
| `observe_counters`            | Subscribe to EventCounters via EventPipe (non-intrusive oracle) |
| `capture_events`              | Capture arbitrary EventPipe events (exceptions, custom EventSource) |
| `find_element_visually`       | Locate an element from a natural-language query using a vision model |

## Architecture

See [docs/architecture.md](docs/architecture.md).

## Security model

Winsight is a **developer tool for the machine it runs on**. By design it grants
the connected agent broad capability over that desktop session:

- `launch_app` starts an arbitrary executable; `close_app` terminates a process tree.
- `evaluate_javascript` runs arbitrary JavaScript inside an embedded WebView2 page.
  CDP tools only connect to ports reported by `detect_webview2_endpoints` for
  the authorized session target or explicit host process.
- `read_managed_state` attaches to a managed process and reads its memory (CLRMD);
  `observe_counters` / `capture_events` open EventPipe sessions on it.
- `capture_screenshot` writes to any `outputPath` you pass (it is **not** sandboxed
  to a directory: the agent already has broad file access, so confining this one
  tool would be theater rather than a boundary). `compare_visual_baseline` is the
  exception: its `baselineKey` is confined to the baseline directory.

Run the server only on a development machine you control, under the same user
account as the agent, against applications you are developing or testing. It runs
as your user: it grants no privilege you do not already have, and it should not
be exposed to an untrusted client. EventPipe-based oracles
(`observe_counters`, `capture_events`) are the EDR-friendly path; the CLRMD attach
in `read_managed_state` uses patterns some endpoint products flag.

Optional guardrails:

| Variable | Purpose |
| -------- | ------- |
| `WINSIGHT_ALLOWED_PROCESS_IDS` | Comma/semicolon allowlist of process ids every process-scoped tool must pass |
| `WINSIGHT_ALLOWED_PROCESS_NAMES` | Comma/semicolon allowlist of process names every process-scoped tool must pass |
| `WINSIGHT_ALLOWED_EXECUTABLES` | Comma/semicolon allowlist for `launch_app` executable paths |
| `WINSIGHT_DRY_RUN` | `1`/`true`/`yes` blocks mutating tools and `run_steps` screenshot writes, returning what would have run |
| `WINSIGHT_ALLOW_CLIPBOARD` | `1`/`true`/`yes` enables the clipboard tools and `type_keys mode=paste`; off by default because the clipboard may hold secrets |
| `WINSIGHT_YIELD_TO_USER_MS` | Milliseconds: every screen-changing action fails with `user_active` while a human input is more recent than this. Unset or `0` disables it |
| `WINSIGHT_TRACE` / `WINSIGHT_TRACE_DIR` | Write tool JSONL traces and `run_steps` replay artifacts |

## License

Apache License 2.0, see [LICENSE](LICENSE).
