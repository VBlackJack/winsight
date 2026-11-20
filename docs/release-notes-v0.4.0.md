# Winsight v0.4.0 - 2026-11-20

Minor release strengthening shared UI automation reliability: desktop-composited popup capture, UIA traversal time budgets, exploratory postcondition guidance, opt-in mouse jiggler filtering, session lock source reporting, and low-level input hook resilience.

## Highlights

- **Popup and menu capture (`captureSource="screen"`)**: `capture_screenshot`, `capture_scene`, and `observe(includeScreenshot=true)` now accept `captureSource="window"` (default, `PrintWindow` on one HWND) or `captureSource="screen"` (DWM-composited desktop rectangle of the target window bounds using `BitBlt` with `SRCCOPY | CAPTUREBLT`). Floating same-process menus, dropdowns, and tooltips appear while keeping window-relative coordinates for `click_point`. Non-visible targets and targets occluded by another process window are refused unless `allowOccluded=true`.
- **UIA traversal time budget (`WINSIGHT_SCENE_TRAVERSAL_BUDGET_MS`)**: Configurable wall-clock budget (default `5000` ms, range `100..120000`) shared by `observe_scene`, `inspect_ui_tree`, and `snapshot_ui`, returning `truncated=true` with `truncationReason="time_budget"` (or `node_limit`, `max_results`, `com_error`) instead of stalling on large UIA trees.
- **`any_change` guidance hint**: `act_verified` with `expect.kind=any_change` keeps its exploratory semantics for non-menu controls and attaches a non-blocking `hint` (`code="any_change_unrelated"`) recommending strict kinds (`window_open`, `selected`, `value`, `toggle`, ...) whenever verification matched only elements unrelated to the target (not the target, not its descendants, and not a new window).
- **Opt-in hardware mouse jiggler filter (`WINSIGHT_IGNORE_PERIODIC_MICRO_MOVES=1`)**: Small mouse-only movements under a configurable displacement threshold (`WINSIGHT_PERIODIC_MICRO_MOVE_MAX_PX`, default `15` px) occurring at regular intervals are classified as `lastInputSource="periodic_micro_move"` and do not reset `humanIdleMs`. Keyboard input, clicks, and larger or irregular mouse movements always count as `human`. Classification details are exposed in `get_user_activity` diagnostics.
- **Session lock source (`sessionLockSource`)**: `get_user_activity` now reports `sessionLockSource` (`wts`, `input_desktop`, or `unknown`) alongside `sessionLocked`.
- **Low-level input hook resilience (`WINSIGHT_INPUT_HOOK=1`)**: Lock-free minimal hook callbacks, pump-thread heartbeat timer, and silent OS removal detection (`LowLevelHooksTimeout`), reporting `inputHookActive=false` plus `inputHookWarning` in `get_user_activity` diagnostics instead of silently degrading.

## Install

**Recommended for desktop users:** download and run `Winsight-Setup-0.4.0-win-x64.exe`. The English/French wizard installs for the current Windows user without administrator rights and configures supported MCP clients.

For the portable archive, download `winsight-0.4.0-win-x64.zip` and `SHA256SUMS.txt`, verify the SHA-256 hash with `Get-FileHash -Algorithm SHA256`, extract all files into a dedicated directory, and point your MCP client to `winsight-mcp.exe`.
