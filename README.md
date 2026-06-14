# Winsight

> A Windows-native testing framework for AI agents.

Winsight is a [Model Context Protocol](https://modelcontextprotocol.io) server
and SDK that lets AI agents (Claude Code, Cursor, Windsurf, custom agents)
inspect, drive, and assert against Windows desktop applications under
development — going beyond what UI Automation alone can do.

## Get started in 60 seconds

```powershell
git clone <this-repo> winsight
cd winsight
dotnet build -c Release
.\tools\smoke-mcp.ps1                  # confirms the server speaks MCP and lists 18 tools
```

Then in Claude Code, the `.mcp.json` at the repository root registers the
server automatically when you open the project. Run `/mcp` in Claude Code
to confirm `winsight` shows as connected, and try a prompt like:

> Launch `samples\SampleWpfApp\bin\Release\net10.0-windows\SampleWpfApp.exe`,
> click the Increment button five times, then read `_internalCount` on
> `Winsight.Samples.SimpleCounter.ViewModels.CounterViewModel` to confirm
> it equals 5.

The agent should compose `list_windows` → `click_element` → `read_managed_state`
on its own.

## Why

Game bots have driven characters in DirectX worlds for two decades. Testing
bots for desktop applications have lagged behind, stuck on top of the UI
Automation tree. Winsight closes that gap by combining four orthogonal
capabilities under a single tool surface that an LLM can compose:

| Layer       | What it offers                                                                | Backed by                                       |
| ----------- | ----------------------------------------------------------------------------- | ----------------------------------------------- |
| Discovery   | Window enumeration, UIA tree inspection (filterable, value-aware)             | FlaUI (UIA3) + Win32 window enumeration         |
| Action      | Strict element location, click styles, type, chords, hover, item selection     | FlaUI patterns + SendInput / Win32 fallback      |
| Sync        | Condition-based waits (exists / not_exists / enabled / visible / value / stable) | UIA polling with structured outcomes          |
| Oracle (UI) | Element state snapshots (value, toggle, selection, focus, geometry)           | FlaUI patterns                                  |
| Oracle      | Read private fields and managed state without `InternalsVisibleTo`            | Microsoft.Diagnostics.Runtime (CLRMD)           |
| Diagnostics | Subscribe to EventCounters, capture EventPipe events (exceptions, telemetry) | Microsoft.Diagnostics.NETCore.Client + TraceEvent |
| Capture     | Window screenshot via `PrintWindow` + `PW_RENDERFULLCONTENT`                  | Win32 `PrintWindow`                             |
| WebView2    | Locate embedded WebView2 instances, surface their CDP debugging endpoint      | WMI process tree + TCP listener cross-reference |
| Perception  | Visual grounding from a natural-language description (cloud), local ONNX backlog | Anthropic Messages API today / ShowUI-π planned |

## Status

Phase 1 + Phase 2 + tool boundary hardening + UIA test ergonomics — fully
bootable, end-to-end validated against `SampleWpfApp` (golden path) and a
real WPF application (`Win11Forge`). 111 unit tests + 3 E2E scenarios green.
Local ONNX vision and visual regression are still backlog (see capability
matrix below).

| Capability                      | Phase 1 | Phase 2 | Phase 3 |
| ------------------------------- | ------- | ------- | ------- |
| Window discovery                | yes     |         |         |
| UIA tree inspection             | yes     |         |         |
| Click / type via UIA            | yes     |         |         |
| Strict locators + modal dialogs |         | yes     |         |
| Element state oracle + waits    |         | yes     |         |
| Focused key chords + tooltip hover |      | yes     |         |
| Item selection (combo/list/tab) |         | yes     |         |
| Read managed field via CLRMD    | yes     |         |         |
| Window screenshot (PrintWindow) |         | yes     |         |
| WebView2 CDP endpoint discovery |         | yes     |         |
| WebView2 CDP drive (eval / type) |        | yes     |         |
| EventPipe counter observer      |         | yes     |         |
| EventPipe arbitrary event capture |       | yes     |         |
| Vision grounding (Claude API)   |         | yes     |         |
| Vision grounding (ShowUI ONNX)  |         | wip     |         |
| Visual regression (Anomalib)    |         | wip     |         |
| Exploratory agent (Sonnet 4.6)  |         |         | yes     |

## Repository layout

```
winsight/
├── src/
│   ├── Winsight.Core/      Engine: UIA, CLRMD, capture, diagnostics, vision, models
│   └── Winsight.Mcp/       MCP server executable (stdio) + tool boundary guard
├── samples/
│   └── SampleWpfApp/       Counter app used as the integration target
├── tests/
│   ├── Winsight.Core.Tests xUnit unit tests for the engine layer
│   ├── Winsight.Mcp.Tests  xUnit unit tests for the MCP tool boundary (ToolSafety, wrappers)
│   └── Winsight.E2E        End-to-end agent-driven scenarios on SampleWpfApp
├── tools/
│   └── smoke-mcp.ps1       JSON-RPC smoke test for the stdio server
└── docs/
    ├── architecture.md
    ├── runbook.md
    └── tools-reference.md
```

## Build

Requires the .NET 10 SDK.

```powershell
dotnet restore
dotnet build
dotnet test                                              # unit tests
dotnet test tests\Winsight.E2E -c Release                # E2E (drives the sample app)
```

The E2E suite launches `SampleWpfApp.exe`, exercises every tool against
it, and asserts the results — useful as a single command to confirm the
framework end-to-end.

## Running the MCP server

The server speaks MCP over stdio. There are two ways to wire it into a
client.

### Development (rebuild on every launch)

```json
{
  "mcpServers": {
    "winsight": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "G:\\_Projects\\winsight\\src\\Winsight.Mcp",
        "-c", "Release",
        "--no-build"
      ]
    }
  }
}
```

Pair this with running `dotnet build -c Release` manually whenever the
source changes. Adding `--no-build` keeps client startup fast.

### Production (run the compiled binary)

```powershell
dotnet build src\Winsight.Mcp -c Release
```

```json
{
  "mcpServers": {
    "winsight": {
      "command": "dotnet",
      "args": [
        "G:\\_Projects\\winsight\\src\\Winsight.Mcp\\bin\\Release\\net10.0-windows\\winsight-mcp.dll"
      ]
    }
  }
}
```

### Where to put the config

| Client      | Config file                                              |
| ----------- | -------------------------------------------------------- |
| Claude Code | `%USERPROFILE%\.claude.json` (per-user) or `.mcp.json` at the repo root |
| Cursor      | `~/.cursor/mcp.json`                                     |
| Windsurf    | `~/.codeium/windsurf/mcp_config.json`                    |

After registration, in Claude Code run `/mcp` to confirm the `winsight`
server is connected and lists all 18 tools.

### Smoke check from the shell

To confirm the server starts cleanly (independent of any client):

```powershell
dotnet run --project src\Winsight.Mcp -c Release --no-build
```

The process should remain open waiting for stdio input. Press
`Ctrl+C` to exit. Logs go to stderr; stdout is reserved for MCP
protocol traffic.

### Optional: enable visual grounding

The `find_element_visually` tool needs a vision backend. The default
implementation calls the Anthropic Claude vision API.

| Variable                    | Required | Default                                  |
| --------------------------- | -------- | ---------------------------------------- |
| `ANTHROPIC_API_KEY`         | yes      | —                                        |
| `WINSIGHT_VISION_MODEL`     | no       | `claude-sonnet-4-6`                      |
| `WINSIGHT_VISION_API_URL`   | no       | `https://api.anthropic.com/v1/messages`  |

> **Privacy note** — the screenshot is sent to the configured API. Do not
> use this tool against apps that display regulated or sensitive content
> until a local ONNX backend (ShowUI-π) is shipped. If `ANTHROPIC_API_KEY`
> is not set, every other tool keeps working; only `find_element_visually`
> returns an error when invoked.

### Optional: redirect capture output

By default `capture_screenshot` (and the intermediate capture taken by
`find_element_visually`) writes PNGs under `%TEMP%\winsight`. Set the
`WINSIGHT_OUTPUT_DIR` environment variable to redirect them into a directory a
supervisor agent can read — for example a folder shared with the agent host.
The directory is created on first write; an explicit `outputPath` argument to
`capture_screenshot` always takes precedence.

| Variable             | Required | Default            |
| -------------------- | -------- | ------------------ |
| `WINSIGHT_OUTPUT_DIR`| no       | `%TEMP%\winsight`  |

## Tools exposed

See [docs/tools-reference.md](docs/tools-reference.md) for the full schema.

| Tool                          | Purpose                                                |
| ----------------------------- | ------------------------------------------------------ |
| `list_windows`                | Enumerate top-level windows                            |
| `inspect_ui_tree`             | Return the UIA tree for a process (filterable, value-aware) |
| `click_element`               | Click a located element (single/double/right, strict locators) |
| `type_text`                   | Type into a located element                            |
| `send_key_chord`              | Focus a window and send a verified keyboard chord      |
| `hover_element`               | Hover an element and capture/assert its tooltip        |
| `select_item`                 | Select an item by text in a ComboBox/ListBox/Tab       |
| `get_element_state`           | Read an element's state (value, toggle, selection, focus) |
| `wait_for_element`            | Wait until an element exists/disappears/enables/shows, value matches, or state is stable |
| `read_managed_state`          | Read a managed field via CLRMD attach                  |
| `capture_screenshot`          | PNG capture of a window (PrintWindow PW_RENDERFULLCONTENT) |
| `detect_webview2_endpoints`   | Find WebView2 children and report any CDP endpoint     |
| `observe_counters`            | Subscribe to EventCounters via EventPipe (non-intrusive oracle) |
| `capture_events`              | Capture arbitrary EventPipe events (exceptions, custom EventSource) |
| `find_element_visually`       | Locate an element from a natural-language query using a vision model |

## Architecture

See [docs/architecture.md](docs/architecture.md).

## License

Apache License 2.0 — see [LICENSE](LICENSE).
