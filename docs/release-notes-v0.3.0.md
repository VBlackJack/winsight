# Winsight v0.3.0 - 2026-11-20

Minor release hardening multi-monitor pointer dispatch, process integrity (UIPI) detection, read-only input-context diagnostics, and foreground host rejection reporting.

## Highlights

- **Multi-monitor `SendInput` and Per-Monitor V2 DPI awareness**: `Winsight.Mcp` enables Per-Monitor V2 DPI awareness at startup and normalizes absolute pointer moves across the full virtual desktop (`SM_XVIRTUALSCREEN`/`SM_YVIRTUALSCREEN`/`SM_CXVIRTUALSCREEN`/`SM_CYVIRTUALSCREEN` with `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`). `drag` and `hover_element` now use the same native pointer driver instead of FlaUI `Mouse.Position`/`Mouse.MoveTo`.
- **Accurate pointer failure attribution**: Distinguishes `move_not_applied` (cursor remained at its pre-move starting position), `foreground_rejects_synthetic_input` (single `SetCursorPos` failure probe returned `FALSE` with `GetLastError() == 0` or the foreground process is listed in `WINSIGHT_SYNTHETIC_INPUT_REJECTING_HOSTS`), and `input_context_blocked` (non-interactive window station or job object `JOB_OBJECT_UILIMIT_*` restrictions) from `cursor_moved` (cursor moved elsewhere than both start and target).
- **Foreground host policy (`WINSIGHT_SYNTHETIC_INPUT_REJECTING_HOSTS`)**: Configurable comma-separated list of process names (default `vmware,vmware-vmx,vmplayer`). VMware Workstation rejects synthetic pointer input while a powered-on VM is shown in the active tab of the foreground window; VMware host UI with no running VM in the active tab accepts synthetic input. Workarounds: switch to another tab without a running VM or bring another window to the foreground, use UI Automation patterns (`click_element`, `select_item`, `act_verified` with UIA actions) for host UI, and use `vmsight` for guest input.
- **Proactive UIPI integrity reporting**: `set_target`, `switch_target`, and `observe_scene` report process integrity levels (`integrity`, `integrityWarning`) using `GetTokenInformation(TokenIntegrityLevel)`. Input tools fail fast with `uipi_blocked` when target integrity is strictly higher than Winsight, and report `"unknown"` while allowing input when querying the target token returns `ERROR_ACCESS_DENIED`.
- **New `diagnose_pointer` tool**: Mutating diagnostic tool (`ReadOnly=false`, `Idempotent=false`, guarded by `ToolAccessPolicy.GuardMutation`) honoring `WINSIGHT_DRY_RUN=1` (reports what would be tried without moving the cursor) and the yield-to-user guard. Tests all four Windows pointer movement methods (`send_input_virtual_desk`, `send_input_primary_absolute`, `send_input_relative`, `set_cursor_pos`) in order, measures `GetCursorPos` and `GetPhysicalCursorPos` immediately, after 50 ms, and after 150 ms, restores the original cursor position after each attempt, and reports `ClipCursor`, `GetCursorInfo`, mouse buttons, mouse acceleration (`SPI_GETMOUSE`, `SPI_GETMOUSESPEED`), foreground process, and read-only `inputContext`.
- **Read-only `inputContext` diagnostics and structured stderr logging**: `get_user_activity`, `diagnose_pointer`, and input failure paths report session vs active console session, window station visibility, thread vs input desktop (read-only), job object UI restrictions, token flags, and parent process chain. `uipi_blocked`, `input_context_blocked`, and `foreground_rejects_synthetic_input` emit one structured stderr line each and have one-line recovery hints in `get_winsight_guide` (`errorPolicy`).
- **Keyboard diagnostics and warnings**: `send_key_chord`, `type_keys`, and `type_text` keyboard fallback report `SendInput` count and Win32 errors on failure, re-verify foreground window before chord dispatch, and attach a non-blocking `warning` (`foreground_may_reject_synthetic_input`) when the foreground process is listed in `WINSIGHT_SYNTHETIC_INPUT_REJECTING_HOSTS`.

## Install

**Recommended for desktop users:** download and run `Winsight-Setup-0.3.0-win-x64.exe`. The English/French wizard installs for the current Windows user without administrator rights and configures supported MCP clients.

For the portable archive, download `winsight-0.3.0-win-x64.zip` and `SHA256SUMS.txt`, verify the SHA-256 hash with `Get-FileHash -Algorithm SHA256`, extract all files into a dedicated directory, and point your MCP client to `winsight-mcp.exe`.

## Validation

- Release build: 0 warnings, 0 errors across all 9 projects (`net10.0-windows10.0.19041.0`).
- Unit tests: 616 passed, 0 failed (`431` Core, `185` MCP, including documentation typography guards; 2 ONNX real-model integration tests skipped when local model fixtures are absent).

---

[Notes de version en francais](fr/release-notes/v0.3.0.md)
