# Winsight UIA Runbook

## Preferred AI Loop

Use the high-level agent tools first:

1. `launch_app` or `list_windows`, then `set_target`.
2. `observe` to get windows, actionable elements, recommended locators, likely
   actions, recent memory, and an optional screenshot path.
3. `plan_steps` with the user goal.
4. `run_steps` with `dryRun=true`.
5. `act_and_observe` or `run_steps` with `dryRun=false`.
6. `repair_locator` when an element is not found or a locator is ambiguous.

`get_winsight_guide` returns this playbook through MCP. Use it when an agent
needs to recover without external documentation in context.

`plan_steps` is intentionally conservative for selection controls. If the goal
looks like "select" or the best candidate is a combo/list/tab, the draft clicks
the candidate and sets `requiresReview=true`; observe the opened options, then
use `select_item` or a `run_steps` `select` step with an explicit `itemName`.

## Drive UIA Through MCP

Do not script UIA by nesting ad-hoc PowerShell inside an agent session.
Use the Winsight MCP server instead. Nested PowerShell UIA scripts hide
failures behind process boundaries, race desktop focus, and bypass the
tool boundary that converts COM/UIA failures into recoverable
`OperationResult` JSON.

For low-level debugging, use `inspect_ui_tree` to discover locators, then drive
the target with the MCP tools:

```json
{
  "tool": "send_key_chord",
  "arguments": {
    "processId": 14820,
    "chord": "Ctrl+Shift+N",
    "windowTitle": "Winsight Sample"
  }
}
```

```json
{
  "tool": "type_text",
  "arguments": {
    "processId": 14820,
    "automationId": "NoteInput",
    "text": "hello from UIA",
    "windowTitle": "Note",
    "focusWindow": true
  }
}
```

```json
{
  "tool": "wait_for_element",
  "arguments": {
    "processId": 14820,
    "automationId": "NoteInput",
    "condition": "value_equals",
    "expectedValue": "hello from UIA",
    "windowTitle": "Note"
  }
}
```

```json
{
  "tool": "wait_for_element",
  "arguments": {
    "processId": 14820,
    "automationId": "CountLabel",
    "condition": "stable_for",
    "stableForMs": 350,
    "pollIntervalMs": 50
  }
}
```

```json
{
  "tool": "hover_element",
  "arguments": {
    "processId": 14820,
    "automationId": "IncrementButton",
    "expectedTooltipText": "Increment counter by one"
  }
}
```

For WPF controls where the parent `Name` is a view-model type name and
the visible label is inside a child element, use `descendantText` with a
normal narrowing criterion:

```json
{
  "tool": "get_element_state",
  "arguments": {
    "processId": 14820,
    "controlType": "Button",
    "descendantText": "Semantic Child Label"
  }
}
```

## Safety audit regression and vision gates

For end-user installation, use `Winsight-Setup-0.1.0-win-x64.exe` and the
English/French guide in `docs/installation.html`. The setup runs per user.
For packaging, install Inno Setup 6 and run `tools/build-setup.ps1` from Windows
PowerShell 5.1 or later. Use a fresh `-OutputDirectory` for each build. Output
includes the setup EXE, portable ZIP, `BUILD-COMMIT.txt` and `SHA256SUMS.txt`.

Client configuration locations:

| Client | User configuration |
|---|---|
| Codex | `%USERPROFILE%\.codex\config.toml` (or `CODEX_HOME`) |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` |
| Claude Code | `%USERPROFILE%\.claude.json` (or `CLAUDE_CONFIG_DIR`) |
| Gemini CLI | `%USERPROFILE%\.gemini\settings.json` (or `%GEMINI_CLI_HOME%\.gemini\settings.json`) |

Setup preserves other server entries and client settings, but may normalize
formatting. Close clients before changes. It validates all selected files before
writing, saves sibling backups and records successful paths for uninstall.
Registration failure is reported in the wizard and returns exit code 3 in silent
mode; logs are under `%LOCALAPPDATA%\Winsight\logs`. A conflicting Winsight
registration must be removed in the client before retrying. Uninstall acts after
confirmation, refuses configuration errors and preserves entries repointed elsewhere.

Configuration formats were checked against the official
[Codex documentation](https://developers.openai.com/codex/mcp),
[Claude Code documentation](https://code.claude.com/docs/en/mcp),
[Claude Desktop setup](https://modelcontextprotocol.io/docs/develop/connect-local-servers)
and [Gemini CLI documentation](https://geminicli.com/docs/tools/mcp-server/).

Run `dotnet test Winsight.slnx -c Release` on an interactive Windows desktop.
`AuditSafetyRegressionTests` and `AuditSafetyScenarioTests` cover process policy,
non-forced closure, dry-run baselines, trace failures, unique artifacts, occlusion,
and stale visual captures. The sample's `--refuse-close` flag is a test fixture
that cancels closing; cleanup explicitly terminates only test-owned processes.

When active MCP hosts lock the normal outputs, build and test with the same
`--artifacts-path` directory. Set `WINSIGHT_SAMPLE_APP_PATH` to the freshly built
`SampleWpfApp.exe` in that directory so E2E exercises the matching sample.

Real vision integration tests carry `Category=Vision`. Without their fixtures,
they report **Skipped**, never **Passed**. To validate the model, provision:

- `WINSIGHT_VISION_ONNX_PATH`: exported local model directory.
- `WINSIGHT_VISION_TEST_IMAGE`: reference screenshot.
- `WINSIGHT_VISION_REF_DIR`: reference processor output, including `pixel_values.f32`.

Then run `dotnet test Winsight.slnx -c Release --filter Category=Vision --logger trx`.
Accept this model gate only when all three selected tests pass with zero skips.
The ordinary CI run retains TRX artifacts even when green so absent fixtures
remain visible. Stub-grounder safety tests do not establish model precision.

`find_element_visually` retains the captured HWND, owning PID, and rectangle.
A moved, resized, disabled, hidden, or destroyed window requires a new capture.
A refused click returns `success: false` with both grounding and click details.
Use `windowTitle` to select the intended main window or modal explicitly.
`act_and_observe` rejects conflicting outer and step process IDs.

Trace and replay writes are best effort: storage failure preserves the original
tool outcome, and an unavailable replay has no `tracePath`. Screenshot filenames
include a unique suffix so rapid batch captures do not overwrite each other.
