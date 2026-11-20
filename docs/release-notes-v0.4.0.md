# Winsight v0.4.0 - 2026-11-20

Minor release strengthening shared UI automation reliability: desktop-composited popup capture, UIA traversal time budgets, exploratory postcondition guidance, opt-in mouse jiggler filtering with background sampling, session lock enforcement and source reporting, low-level input hook resilience, stub-launcher handoff, and safe optional UIA property access.

## Highlights

- **Popup and menu capture (`captureSource="screen"`)**: `capture_screenshot`, `capture_scene`, and `observe(includeScreenshot=true)` now accept `captureSource="window"` (default, `PrintWindow` on one HWND) or `captureSource="screen"` (DWM-composited desktop rectangle of the target window bounds using `BitBlt` with `SRCCOPY | CAPTUREBLT`). Floating same-process menus, dropdowns, and tooltips appear while keeping window-relative coordinates for `click_point`. Non-visible targets and targets occluded by another process window are refused unless `allowOccluded=true`.
- **UIA traversal time budget (`WINSIGHT_SCENE_TRAVERSAL_BUDGET_MS`)**: Configurable wall-clock budget (default `5000` ms, range `100..120000`) shared by `observe_scene`, `inspect_ui_tree`, and `snapshot_ui`, returning `truncated=true` with `truncationReason="time_budget"` (or `node_limit`, `max_results`, `com_error`) instead of stalling on large UIA trees.
- **`any_change` guidance hint**: `act_verified` with `expect.kind=any_change` keeps its exploratory semantics for non-menu controls and attaches a non-blocking `hint` (`code="any_change_unrelated"`) recommending strict kinds (`window_open`, `selected`, `value`, `toggle`, ...) whenever verification matched only elements unrelated to the target (not the target, not its descendants, and not a new window).
- **Opt-in hardware mouse jiggler filter and background sampler**: Enable `WINSIGHT_IGNORE_PERIODIC_MICRO_MOVES=1` (`WINSIGHT_PERIODIC_MICRO_MOVE_MAX_PX`, default `15` px; `WINSIGHT_PERIODIC_MICRO_MOVE_TOLERANCE_PCT`, default `25`%) so regular small mouse-only movements classify as `lastInputSource="periodic_micro_move"` and do not reset `humanIdleMs`, preserving the learned period across Winsight injections and human interruptions. Keyboard input, clicks, and larger or irregular moves always count as `human`. A lightweight background sampler (`WINSIGHT_ACTIVITY_SAMPLE_MS`, default `250` ms, `0` disables) feeds the detector and `recentInputs` between tool calls.
- **Session lock guard and source (`session_locked`, `sessionLockSource`)**: `get_user_activity` reports `sessionLockSource` (`wts`, `input_desktop`, or `unknown`) alongside `sessionLocked`. All UI input tools (`click_*`, `hover_*`, `scroll_point`, `drag`, `type_*`, `send_key_chord`, `act_verified`, `run_steps`, `run_verified_steps`) refuse with `session_locked` while the desktop session is locked; read-only tools keep working.
- **Low-level input hook resilience (`WINSIGHT_INPUT_HOOK=1`)**: Lock-free minimal hook callbacks, pump-thread heartbeat timer, and silent OS removal detection (`LowLevelHooksTimeout`), reporting `inputHookActive=false` plus `inputHookWarning` in `get_user_activity` diagnostics instead of silently degrading.
- **Stub-launcher handoff (`launch_app`)**: When a stub launcher or app execution alias (such as `C:\Windows\System32\notepad.exe` on Windows 11) exits with code `0` before showing a window, `launch_app` binds to the newly appeared top-level window in the target process and reports `handedOffToProcessId`.
- **Safe optional UIA property access**: `observe_scene`, `inspect_ui_tree`, `snapshot_ui`, and `ElementStateReader` read optional UIA properties (`IsOffscreen`, `BoundingRectangle`, `ExpandCollapseState`, etc.) through safe `TryGetValue` accessors, avoiding `PropertyNotSupportedException` on WinUI/XAML (Notepad) and Chromium (Brave) providers.

## Install

**Recommended for desktop users:** download and run `Winsight-Setup-0.4.0-win-x64.exe`. The English/French wizard installs for the current Windows user without administrator rights and configures supported MCP clients.

For the portable archive, download `winsight-0.4.0-win-x64.zip` and `SHA256SUMS.txt`, verify the SHA-256 hash with `Get-FileHash -Algorithm SHA256`, extract all files into a dedicated directory, and point your MCP client to `winsight-mcp.exe`.

## Validation

- Release build: 0 warnings, 0 errors across all 9 projects (`net10.0-windows10.0.19041.0`).
- Unit tests: 711 passed, 0 failed (`516` Core, `195` MCP, including documentation typography guards; 2 ONNX real-model integration tests skipped when local model fixtures are absent).
- Live desktop validation on Windows 11: `captureSource="screen"` captures WinUI Notepad menus; `observe_scene` succeeds on WinUI Notepad and Chromium Brave; `launch_app` follows the `notepad.exe` handoff (`handedOffToProcessId`); USB jiggler filtered across consecutive actions without `user_active`; `sessionLocked` transitions cleanly on `Win+L` and unlock (`sessionLockSource="wts"`).

---

[Notes de version en francais](fr/release-notes/v0.4.0.md)
