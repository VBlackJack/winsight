# Winsight v0.3.0 - Unreleased

Minor release hardening multi-monitor pointer dispatch, process integrity (UIPI) detection, input-context diagnostics, and foreground host rejection reporting.

## Highlights

- **Multi-monitor `SendInput` and Per-Monitor V2 DPI awareness**: `Winsight.Mcp` enables Per-Monitor V2 DPI awareness at startup and normalizes absolute pointer moves across the full virtual desktop (`SM_XVIRTUALSCREEN`/`SM_CXVIRTUALSCREEN` with `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`). `drag` and `hover_element` use the same native pointer driver.
- **Accurate pointer failure attribution**: Distinguishes `move_not_applied` (cursor remained at pre-move start), `foreground_rejects_synthetic_input` (single `SetCursorPos` failure probe returned `FALSE` with error 0 or foreground process is listed in `WINSIGHT_SYNTHETIC_INPUT_REJECTING_HOSTS`), and `input_context_blocked` (non-interactive window station or job UI restrictions) from `cursor_moved`.
- **Proactive UIPI integrity reporting**: `set_target`, `switch_target`, and `observe_scene` report process integrity levels and `integrityWarning` (`uipi_blocked`); input tools fail fast when target integrity is strictly higher than Winsight.
- **New `diagnose_pointer` tool**: Tests all four Windows pointer movement methods in order, restores the cursor after each attempt, reports `ClipCursor`, `GetCursorInfo`, mouse buttons, mouse acceleration, foreground process, and read-only `inputContext`, and honors `WINSIGHT_DRY_RUN=1`.
- **Keyboard diagnostics and warnings**: `send_key_chord`, `type_keys`, and `type_text` keyboard fallback report `SendInput` count/Win32 errors on failure and attach a non-blocking `warning` (`foreground_may_reject_synthetic_input`) when the foreground process is listed in `WINSIGHT_SYNTHETIC_INPUT_REJECTING_HOSTS`.
