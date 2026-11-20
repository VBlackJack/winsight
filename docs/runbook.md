# Winsight UIA Runbook

*[Version française](fr/runbook.md)*

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

## Shared Desktop

The screen may be in use by a person or by another agent session.

1. `get_user_activity` before taking the mouse or keyboard; `humanIdleMs` is the
   time since the last human input, Winsight's own input excluded, and
   `diagnostics` reports `inputKind`, `cursorMoved`, and `recentInputs`.
2. `wait_for_user_idle` with a real timeout (for example `idleMs=3000`,
   `timeoutMs=30000`) before a batch or after `user_active`, then
   `observe_scene` before acting.
3. Keep `run_steps stopOnForegroundChange=true` (the default): a batch stops with
   `foreground_changed` before touching the screen again when someone takes the
   foreground.
4. Set `WINSIGHT_YIELD_TO_USER_MS` (for example `5000`) in the server
   environment to make every screen-changing tool fail with `user_active` while
   a person is active.
5. If phantom input keeps resetting `humanIdleMs`, set `WINSIGHT_INPUT_HOOK=1`
   in the server environment to distinguish real hardware input from
   `other_injected` input via `LLMHF_INJECTED` / `LLKHF_INJECTED`. For an
   explicit logged per-session bypass, only the human can set
   `WINSIGHT_YIELD_OVERRIDE=1` in host configuration (never via a tool
   argument).

For GPU-drawn UIs without an automation tree (iced, winit, egui), click with
`click_point focusWindow=true`. The pointer rests on the point before the press
(`settleMs`, default 150 ms); raise it if the application renders slower. Type
with `type_keys`, synchronize with `wait_visual_change` and `wait_visual_stable`
steps instead of sleeping, and check text with `read_text_visually` or an
`assert` step with operator `text_visible`.

After rebuilding Winsight, restart the client session. A running session keeps
the server process it started with, and its tool list, until it reconnects.
`get_winsight_guide` shows it: `server.registeredTools` is what this session can
call, and `server.newerBuildAvailable` is true when `tools\run-mcp.ps1` started
it from an output that has been rebuilt since. A tool named in the docs but
absent from `registeredTools` means the same thing.

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

## Known hosts that reject synthetic input

Some Windows host applications cause Windows to reject synthetic Win32 pointer
moves (`SendInput` and `SetCursorPos`, where `SetCursorPos` returns `FALSE`
with `GetLastError() == 0`) while a powered-on VM is shown in the foreground
window, regardless of how it became foreground or whether the cursor is inside
or outside that window, even at matching Medium integrity levels.

The host list is configured via `WINSIGHT_SYNTHETIC_INPUT_REJECTING_HOSTS`
(comma-separated process names without `.exe`, default
`vmware,vmware-vmx,vmplayer`; set to an empty value to disable the default
list).

| Process | Observed behavior | Recommended workarounds |
|---|---|---|
| `vmware`, `vmware-vmx`, `vmplayer` (VMware Workstation / Player) | While a powered-on VM is shown in the active tab of the foreground window, all synthetic pointer moves fail (`SetCursorPos` returns `FALSE`, `err=0`) and pointer tools report `foreground_rejects_synthetic_input`. VMware host UI with no running VM in the active tab accepts synthetic input. No known VMware preference fixes the running-VM case. Keyboard tools (`send_key_chord`, `type_keys`, `type_text` fallback) still dispatch normally and attach a `foreground_may_reject_synthetic_input` warning. | Switch to another tab (without a running VM) or bring another window to the foreground. Use UI Automation patterns (`click_element`, `select_item`, `act_verified` with UIA actions) for VMware host UI, and `vmsight` for guest VM input. |

Use `diagnose_pointer` to compare all four pointer move methods against the
current foreground window, `ClipCursor`, `GetCursorInfo`, and process input
context.

