# Winsight v0.6.0 - 2026-11-23

Minor release hardening the 0.5.0 changes: the 0.5.0 fixes are now exercised on live windows, tool output is compact JSON, the installer can update while Winsight servers are running, and the release chain gains a preflight check. One change to tool output formatting is described in the upgrade notes.

## Highlights

- **Compact JSON output**: tool results and resources are serialized without indentation and without a space after `:`. Field names and null omission are unchanged. This reduces the size of every response, on top of the lighter scenes introduced in 0.5.0.
- **Installer updates while servers run**: setup now closes running Winsight servers itself (`CloseApplications=force`), so files in use no longer block an update. Restart the client applications afterwards.
- **No window title in the occlusion refusal**: when `captureSource="screen"` is refused because another application covers the target, the message names the occluding window by handle and process id and no longer includes its title. Titles of other processes are no longer read on that path.
- **Live-window coverage of the 0.5.0 fixes**: new end-to-end scenarios exercise the screen-capture border mask, the OCR language fallback and the `ocr_language_invalid` error, title withholding in `list_windows` and `get_focused_element`, strict `actionableOnly`, and the `region_uniform` warning.
- **One record per line in the trace**: the daily `.jsonl` trace wrote each record across several lines; each record is now a single line. The `run_steps` replay file stays indented for reading.
- **Release tooling**: `tools/smoke-mcp.ps1` derives the expected tool count from the sources instead of a fixed number, a unit test guards the tool counts quoted in the documentation, and `tools/release-preflight.ps1` checks the tree, the branch, the notes, the tag, and the CI result before a release.
- **`ISceneDriver` seam**: scene tools reach UI Automation through an interface, so `any_change` verification under `actionableOnly` is now tested through the real check path with a fake driver.

## Upgrade notes

- Clients that parse tool output as JSON are unaffected. Anything that matched serialized text literally (for example `"success": true` with a space) must match the compact form (`"success":true`) or, better, parse the JSON.
- Tools that read the daily `.jsonl` trace can now rely on one JSON record per line.
- After updating with the installer, restart the MCP client applications: setup closes the running servers and the clients do not always restart them on their own.

## Known limits

- With `captureSource="screen"`, Windows 11 rounded corners can still show a few pixels of a neighboring window inside the DWM frame.
- `region_uniform` also fires on a legitimately blank window. On a full-window read the title bar usually prevents it; pass a `region` to check a specific pane.
- The installer's close-on-update behaviour was checked by compiling the setup script, not by updating a machine with running servers.

## Install

**Recommended for desktop users:** download and run `Winsight-Setup-0.6.0-win-x64.exe`. The English/French wizard installs for the current Windows user without administrator rights and configures supported MCP clients.

For the portable archive, download `winsight-0.6.0-win-x64.zip` and `SHA256SUMS.txt`, verify the SHA-256 hash with `Get-FileHash -Algorithm SHA256`, extract all files into a dedicated directory, and point your MCP client to `winsight-mcp.exe`.

## Validation

- Release build: 0 warnings, 0 errors (`net10.0-windows10.0.19041.0`).
- Unit tests: 766 passed, 0 failed (`545` Core, `221` MCP, including documentation typography and tool-count guards; 2 ONNX real-model integration tests skipped when local model fixtures are absent).
- End-to-end tests on a Windows CI runner: the field-report scenarios ran and passed on live windows. The focus-withholding scenario needs the runner to give keyboard focus to a second application and is skipped, not failed, when it does not; the vision-grounding test is skipped without a model.
- MCP smoke check: 71 tools derived from the sources and discovered.

---

[Notes de version en francais](fr/release-notes/v0.6.0.md)
