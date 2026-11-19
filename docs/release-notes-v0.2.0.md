# Winsight v0.2.0 - 2026-11-19

Second public release of the Windows x64 MCP server, expanding the tool surface
from 41 to 70 tools (with an optional 33-tool compact profile) and adding
verified scene control, shared-desktop awareness, custom-drawn UI support, and
MCP protocol autonomy features.

## Highlights

### MCP protocol autonomy
- Startup `ServerInstructions` guide clients through the verified control loop
  and error recovery policy on initialize without an extra discovery call.
- Explicit MCP tool annotations (`ReadOnly`, `Destructive`, `Idempotent`,
  `OpenWorld`) on all 70 tools allow clients to auto-approve read-only
  perception tools.
- Four read-only MCP resources (`winsight://guide`, `winsight://state`,
  `winsight://windows`, `winsight://scene/latest`) and three reusable MCP
  prompts (`explore_app`, `verify_flow`, `diagnose_ui_bug`).
- `WINSIGHT_TOOL_PROFILE=compact` exposes a focused 33-tool autonomous subset
  while keeping `full` (70 tools) as the default.
- MCP `notifications/progress` ticks on `run_verified_steps`, `run_app_profile`,
  `wait_any`, and `wait_for_user_idle`.
- `winsight-configure` accepts `--compact` and `--yield-ms <ms>` to persist
  `WINSIGHT_TOOL_PROFILE=compact` and `WINSIGHT_YIELD_TO_USER_MS` in JSON and
  TOML client configurations.

### Verified control, hybrid perception, and workflow tools
- `observe_scene` and `act_verified` provide expiring single-use target
  references with explicit postconditions, `observeAfter=true` (returning
  `nextObservation` and structural `diff` in one round trip), exploratory
  predicates (`value_contains`, `name_contains`, `any_change`, `visual_change`),
  `backgroundOnly=true` (UIA patterns without stealing focus or mouse), and
  consecutive-failure loop detection hints.
- `observe_scene` supports `actionableOnly`, `filterText`, `includeOcr=true`
  (synthesizing clickable `tN` OCR targets), automatic `surfaceHint` (WebView2
  CDP port or custom-drawn hint), and automatic `matchingProfiles` discovery.
- `capture_scene` returns inline Set-of-Mark PNG images by default
  (`includeImage=true`), makes `windowHandle` optional, and supports targeted
  cropping via `targetRef` and `paddingPx`.
- `run_verified_steps` executes 1..20 semantic verified steps in one call with
  automatic `recovery` on failure.
- `wait_any` races 1..10 competing conditions (`element_exists`,
  `element_not_exists`, `value_contains`, `window_open`, `window_closed`,
  `ocr_contains`, `visual_change`) and returns as soon as the first holds.
- `select_file_dialog` provides a fast-path for standard Windows `#32770`
  Open/Save dialogs.
- Named multi-app targets via `set_target(processId, alias)` and
  `switch_target(alias)`, plus auto-target binding on `launch_app` and
  auto-clear on `close_app`.
- Real-time UIA event hooks (`watch_ui_events`), deterministic UIA accessibility
  and QA auditing (`audit_ui_accessibility`), and profile management
  (`list_app_profiles`, `save_verified_history_as_profile`, `delete_app_profile`).
- Shared-desktop awareness (`get_user_activity`, `wait_for_user_idle`,
  `WINSIGHT_YIELD_TO_USER_MS`, `stopOnForegroundChange`), GPU/drawn surface
  input and OCR (`type_keys`, `scroll_point`, `hover_point`,
  `read_text_visually`, `wait_for_visual_change`, `wait_for_visual_stable`), and
  complete French documentation mirrors under `README.fr.md` and `docs/fr/`.

### Fixes
- `ManagedStateReader` now reads `System.String` fields instead of returning
  object address placeholders.
- `act_verified` enforces `WINSIGHT_YIELD_TO_USER_MS` before consuming an
  observation or dispatching input (unless `backgroundOnly=true`).
- `click_and_wait` and `type_and_assert` isolate secondary target locators from
  primary target fields.
- `DomSceneTool` dispatches asynchronously without blocking under
  `DesktopInputLease` and avoids per-tick WMI/TCP re-scans during polling.
- `ToolAccessPolicy` and `AppLauncher` dispose `Process` handles; verified scene
  tools share DI-managed singletons via `SceneServices`; `replay_steps`
  restricts `tracePath` to `.json` files inside the trace directory.

## Install

**Recommended for desktop users:** download and run
`Winsight-Setup-0.2.0-win-x64.exe`. The English/French wizard installs for the
current Windows user without administrator rights and configures Codex, Claude
Desktop, Claude Code, and/or Gemini CLI.

For the portable archive, download `winsight-0.2.0-win-x64.zip` and
`SHA256SUMS.txt`, verify the SHA-256 hash with `Get-FileHash -Algorithm SHA256`,
extract all files into a dedicated directory, and point your MCP client to
`winsight-mcp.exe`.

## Validation

- Release build: 0 warnings, 0 errors across all 9 projects (`net10.0-windows10.0.19041.0`).
- Unit tests: 536 passed, 0 failed (2 ONNX real-model integration tests skipped
  when local model fixtures are absent).
- CI Windows runner: Core tests, MCP tests, live desktop E2E suite, 70-tool
  stdio smoke test, 20-scenario `Winsight.Eval` verified control regression, and
  Inno Setup installer build all passed.

---

[Notes de version en francais](fr/release-notes/v0.2.0.md)
