# Winsight v0.2.2 - 2026-11-19

Patch release fixing scene filter diffing, `truncated` reporting, and custom
Win32 menubar activation observed during field testing against VMware
Workstation (`vmware.exe`, `wui.unibar`).

## Fixes

- `act_verified` with `expect.kind=any_change` (as well as `observeAfter=true`
  and recovery captures) now reuses the source observation's exact filters
  (`windowTitle`, `maxResults`, `actionableOnly`, `filterText`, `includeOcr`).
  A filtered observation followed by a no-op action no longer produces phantom
  `elementsAdded` or false `verified` outcomes.
- `observe_scene` now applies `filterText` and `actionableOnly` across the full
  traversed UIA tree before capping to `maxResults`. `truncated` is `true` only
  when matching results were actually cut by `maxResults` or UIA traversal hit
  its node limit (`10000`) or a `COMException`.
- `act_verified` with `action=activate` on a `MenuItem` and
  `expect.kind=any_change` now requires actual menu activation evidence (a new
  window/popup, new `Menu`/`MenuItem`/`MenuBar` elements not in the pre-action
  unfiltered baseline, or the target `MenuItem` transitioning to `Expanded` /
  `PartiallyExpanded`). When no menu opens before timeout (common on Win32
  custom menubars such as `wui.unibar` that ignore UIA `Invoke`),
  `act_verified` returns `dispatched_unconfirmed` with a structured
  `click_point` hint centered on the element's bounds.

## Install

**Recommended for desktop users:** download and run
`Winsight-Setup-0.2.2-win-x64.exe`. The English/French wizard installs for the
current Windows user without administrator rights and configures Codex, Claude
Desktop, Claude Code, and/or Gemini CLI.

For the portable archive, download `winsight-0.2.2-win-x64.zip` and
`SHA256SUMS.txt`, verify the SHA-256 hash with `Get-FileHash -Algorithm SHA256`,
extract all files into a dedicated directory, and point your MCP client to
`winsight-mcp.exe`.

## Validation

- Release build: 0 warnings, 0 errors across all 9 projects (`net10.0-windows10.0.19041.0`).
- Unit tests: 567 passed, 0 failed (`389` Core, `178` MCP; 2 ONNX real-model
  integration tests skipped when local model fixtures are absent).

---

[Notes de version en francais](fr/release-notes/v0.2.2.md)
