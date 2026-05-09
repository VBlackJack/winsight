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
.\tools\smoke-mcp.ps1                  # confirms the server speaks MCP and lists 10 tools
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

| Layer       | What it offers                                                       | Backed by                          |
| ----------- | -------------------------------------------------------------------- | ---------------------------------- |
| Discovery   | Window enumeration, UIA tree inspection                              | FlaUI (UIA3)                       |
| Action      | Element location, click, type, focus                                 | FlaUI patterns + fallback          |
| Oracle      | Read private fields and managed state without `InternalsVisibleTo`   | Microsoft.Diagnostics.Runtime      |
| Perception  | (Phase 2) Vision grounding, visual regression                        | ONNX runtime + ShowUI / OmniParser |

## Status

Phase 1 — bootable scaffold. Not production-ready.

| Capability                      | Phase 1 | Phase 2 | Phase 3 |
| ------------------------------- | ------- | ------- | ------- |
| Window discovery                | yes     |         |         |
| UIA tree inspection             | yes     |         |         |
| Click / type via UIA            | yes     |         |         |
| Read managed field via CLRMD    | yes     |         |         |
| Window screenshot (PrintWindow) |         | yes     |         |
| WebView2 CDP endpoint discovery |         | yes     |         |
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
│   ├── Winsight.Core/      Engine: UIA, CLRMD, models
│   └── Winsight.Mcp/       MCP server executable (stdio)
├── samples/
│   └── SampleWpfApp/       Counter app used as test target
├── tests/
│   ├── Winsight.Core.Tests xUnit unit tests
│   └── Winsight.E2E        End-to-end agent-driven scenarios
└── docs/
    ├── architecture.md
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
server is connected and lists 9 tools.

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

## Tools exposed

See [docs/tools-reference.md](docs/tools-reference.md) for the full schema.

| Tool                          | Purpose                                                |
| ----------------------------- | ------------------------------------------------------ |
| `list_windows`                | Enumerate top-level windows                            |
| `inspect_ui_tree`             | Return the UIA tree for a process                      |
| `click_element`               | Click on a located element                             |
| `type_text`                   | Type into a located element                            |
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
