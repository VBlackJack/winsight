# Changelog

All notable changes to Winsight are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

#### Phase 1 — UIA + CLRMD foundation

- `Winsight.Core` engine targeting `net10.0-windows`, x64.
- `Winsight.Mcp` server speaking MCP over stdio, built on
  ModelContextProtocol 1.0.0 and `Microsoft.Extensions.Hosting`.
- `samples/SampleWpfApp` — a deliberately minimal WPF counter app used as
  the integration target.
- 5 tools:
  - `list_windows` — top-level window enumeration via FlaUI UIA3.
  - `inspect_ui_tree` — bounded BFS of the UIA tree (max depth 20).
  - `click_element` — Invoke / Toggle / mouse fallback.
  - `type_text` — Value pattern / keyboard fallback.
  - `read_managed_state` — CLRMD non-suspending attach + heap walk.
- Solution layout: `Directory.Build.props` (treat warnings as errors),
  `Directory.Packages.props` (central package management), `.slnx`
  solution file (.NET 10 format).

#### Phase 2 — perception, oracle, vision

- `WindowCapture` — PrintWindow with `PW_RENDERFULLCONTENT` so
  DirectX-composed WPF surfaces capture without going black.
- `WebView2Detector` + `WebView2CommandLineParser` — single-WMI-query
  walk of the host's process tree, finds `msedgewebview2.exe`
  descendants, parses `--remote-debugging-port`, cross-references
  `IPGlobalProperties.GetActiveTcpListeners()`. Returns an actionable
  hint (in tool output) when no usable CDP endpoint is found.
- `DiagnosticsObserver.ObserveCounters` — EventPipe-based EventCounter
  aggregator. Uses `Microsoft.Diagnostics.NETCore.Client` and
  `Microsoft.Diagnostics.Tracing.TraceEvent` — same channel as
  `dotnet-counters` / `dotnet-trace`, so it is invisible to enterprise
  EDR products that flag CLRMD attach.
- `DiagnosticsObserver.CaptureEvents` — captures arbitrary EventPipe
  events from one or more providers, normalizes payloads via
  `PayloadNormalizer` (depth-capped recursion, JSON-safe types), bounded
  by configurable `maxEvents` and `durationMs`.
- `IElementGrounder` interface + `AnthropicVisionGrounder`
  implementation — calls the Anthropic Messages API with the screenshot
  encoded as base64 and a tightly-constrained prompt, parses the
  response via `AnthropicResponseParser` (robust to wrapping prose, code
  fences, numeric strings, embedded braces). Configured via
  `ANTHROPIC_API_KEY`, `WINSIGHT_VISION_MODEL`,
  `WINSIGHT_VISION_API_URL` environment variables.
- 5 additional tools:
  - `capture_screenshot`
  - `detect_webview2_endpoints`
  - `observe_counters`
  - `capture_events`
  - `find_element_visually`

#### Tool boundary hardening

- `ToolSafety.SafeInvoke` / `SafeInvokeAsync` — universal outer guard at
  the MCP tool boundary. Catches every non-fatal exception thrown inside
  a tool body and converts it into an `OperationResult { success: false,
  error: "<message>" }` JSON. Truly fatal exceptions (`OutOfMemoryException`,
  `StackOverflowException`, `ThreadAbortException`) are deliberately left
  to propagate.
- The guard is applied to all 10 tool methods. Before, an unhandled
  exception inside any tool (e.g. a `COMException` from FlaUI/UIA, or a
  CLRMD attach failure on an inaccessible process) would terminate the
  `.NET` host, close the stdio transport, and render every other tool
  permanently dead for the rest of the agent session. After the guard,
  exceptions surface as structured, actionable JSON and the host stays up.
- `WindowCapture` — `PrintWindow` failure now includes the Win32
  `GetLastError` code and a hint that the surface may be a WinUI3
  composition that does not support `PrintWindow`.

#### Tests

- 47 unit tests covering record contracts, the CDP command-line parser,
  the EventPipe payload normalizer, the Anthropic response parser, the
  capture-screenshot tool wrapper, and the universal `ToolSafety` guard.
- New `Winsight.Mcp.Tests` project (with `InternalsVisibleTo`) so the
  tool boundary itself can be unit-tested without touching live processes.
- 2 end-to-end tests that drive `SampleWpfApp` from spawn to teardown,
  exercising every Phase 1 and Phase 2 module.

#### Validation

- End-to-end dogfood validated against `SampleWpfApp` (golden path: UIA
  click + CLRMD field read + PrintWindow capture all succeed) and against
  `Win11Forge` (elevated app — UIA/CLRMD failures now return clean
  structured errors instead of generic `"An error occurred invoking 'X'"`,
  and the transport survives every failure).

#### Tooling

- `.mcp.json` at the repository root for project-scoped Claude Code
  registration.
- `tools/smoke-mcp.ps1` — JSON-RPC smoke test that spawns the server,
  performs `initialize` + `tools/list`, and asserts the tool count.

### Compatibility notes

- Target processes for CLRMD and EventPipe must be .NET Core 3.0+ /
  .NET 5+. `.NET Framework 4.8` is not supported.
- The MCP server process must match the target process bitness. The
  default platform target throughout the solution is x64.
- `find_element_visually` requires `ANTHROPIC_API_KEY` to be set and
  transmits the screenshot to the configured API. The other 9 tools
  work without any API key.
- Windows UAC / UIPI: a winsight host running at standard-user level
  cannot enumerate, inspect, or attach to processes that require
  administrator (apps shipping `<requestedExecutionLevel
  level="requireAdministrator" .../>`). The tool calls now return clean
  structured failures in that case rather than generic errors, but
  driving such a target requires running winsight at the same or higher
  integrity level.
