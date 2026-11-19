# Winsight v0.2.1 - 2026-11-19

Patch release addressing shared-desktop user-activity defects observed during
multi-monitor Windows 11 field testing with VMware Workstation.

## Fixes

- `wait_for_user_idle` honors the full `timeoutMs` via a single end-to-end async
  wait loop instead of slicing waits into 1000 ms chunks that returned only the
  final slice's `waitedMs` and timeout message. Progress notifications,
  cancellation, and bounds validation now run on the unified loop.
- `get_user_activity` and `wait_for_user_idle` no longer report
  `sessionLocked=true` on unlocked 64-bit Windows sessions.
  `WTSINFOEXW.Data.WTSInfoExLevel1.SessionFlags` is now read at its aligned
  offset 16 (rather than offset 12, which read `SessionState`), with a read-only
  `OpenInputDesktop` / `UOI_NAME` fallback when WTS returns unknown.

## Additions

- `get_user_activity` returns a `diagnostics` object (`inputKind`,
  `cursorMoved`, `cursorPosition`, `inputHookActive`,
  `lastInputInjectedByOther`, and up to 8 `recentInputs` entries with `ageMs`
  and `deltaFromPreviousMs`) to diagnose phantom desktop input.
- Opt-in low-level input hook (`WINSIGHT_INPUT_HOOK=1`, off by default) using
  `LLMHF_INJECTED` and `LLKHF_INJECTED` flags without recording key codes or
  text, separating real hardware mouse/keyboard input from input injected by
  other processes (`lastInputSource: "other_injected"`).
- Host-configured, logged per-session yield override
  (`WINSIGHT_YIELD_OVERRIDE=1`), enabled exclusively by the human through server
  environment configuration (never via a tool argument) and surfaced in
  `get_user_activity` (`yieldOverridden`) and `get_agent_state`
  (`guardrails.yieldOverrideEnabled`).
- Updated `get_winsight_guide` (`yieldRecoveryLoop`), `AgentRecipeCatalog`, tool
  descriptions, server instructions, tools reference, and runbook with the
  `wait_for_user_idle` -> `observe_scene` -> act recovery loop.

## Install

**Recommended for desktop users:** download and run
`Winsight-Setup-0.2.1-win-x64.exe`. The English/French wizard installs for the
current Windows user without administrator rights and configures Codex, Claude
Desktop, Claude Code, and/or Gemini CLI.

For the portable archive, download `winsight-0.2.1-win-x64.zip` and
`SHA256SUMS.txt`, verify the SHA-256 hash with `Get-FileHash -Algorithm SHA256`,
extract all files into a dedicated directory, and point your MCP client to
`winsight-mcp.exe`.

## Validation

- Solution build: 0 warnings, 0 errors across all 9 projects (`net10.0-windows10.0.19041.0`).
- Unit tests: 563 passed, 0 failed (`389` Core, `174` MCP; 2 ONNX real-model
  integration tests skipped when local model fixtures are absent).
- Verified live `get_user_activity` on unlocked Windows 11 host returns
  `sessionLocked: false` with the new `diagnostics` payload.

---

[Notes de version en francais](fr/release-notes/v0.2.1.md)
