# Tools reference

This document describes the MCP tools exposed by `Winsight.Mcp` to AI agents.
Every tool returns a JSON string. Errors — whether from a known failure mode
or an unexpected exception inside the tool body — are uniformly surfaced
inside an `OperationResult` shape (`{ "success": false, "error": "..." }`)
rather than as MCP exceptions, so an agent can read and recover from them.
A universal `ToolSafety` guard at the tool boundary catches anything the
implementation throws and converts it into that shape, so a single rogue
exception cannot tear down the host process or kill the rest of the agent's
session.

## `launch_app`

Start an application and wait until its window is actually present before
returning, so an agent never races a still-initializing UI. The returned window
is already classified (see [`list_windows`](#list_windows)), ready to drive
without a follow-up `list_windows`/poll.

**Parameters**

| Name                 | Type    | Required | Description                                                            |
| -------------------- | ------- | -------- | --------------------------------------------------------------------- |
| `path`               | string  | yes      | Absolute path to the executable to launch                             |
| `arguments`          | string? | no       | Command-line arguments                                                |
| `workingDirectory`   | string? | no       | Working directory; defaults to the executable's own directory         |
| `waitForWindowTitle` | string? | no       | Case-insensitive substring of the window title to wait for. When omitted, waits for the process's main window |
| `timeoutMs`          | int     | no       | Max wait for the window. Default 15000; max 120000                    |

**Returns** — `OperationResult` wrapping a `LaunchResult`:

```json
{
  "success": true,
  "data": {
    "processId": 14820,
    "window": {
      "processId": 14820,
      "windowHandle": 1247868,
      "title": "Winsight Sample - Counter",
      "processName": "SampleWpfApp",
      "isForeground": true,
      "x": 750, "y": 400, "width": 420, "height": 280,
      "kind": "main", "className": "HwndWrapper[...]", "ownerHandle": 0, "isModal": false
    }
  }
}
```

The call fails explicitly when the executable is missing, the process exits
during startup (the exit code is reported), or no matching window appears within
`timeoutMs`.

## `close_app`

Close a process by id. By default this requests a graceful shutdown
(`CloseMainWindow`) and falls back to a kill if the process does not exit within
the grace window; `force=true` kills the process tree immediately.

**Parameters**

| Name        | Type | Required | Description                                            |
| ----------- | ---- | -------- | ------------------------------------------------------ |
| `processId` | int  | yes      | Process id to close (from `launch_app` or `list_windows`) |
| `force`     | bool | no       | Kill the process tree immediately                      |

**Returns** — `OperationResult` whose `data` reports `processId` and whether the
process was force-killed; a process that is already gone is reported as success
(`alreadyExited: true`).

## `list_windows`

Enumerate top-level windows, in z-order (topmost first), through Win32 — so
owned surfaces that UIA desktop enumeration hides (modal dialogs, MessageBoxes,
dropdown menus, flyouts, tooltips) are discoverable, not just reachable.

**Parameters**

| Name        | Type | Default | Description                                                                 |
| ----------- | ---- | ------- | --------------------------------------------------------------------------- |
| `processId` | int? | null    | When set, return that process's **full** window stack exhaustively (including untitled dialogs, popups, and menus). When omitted, return every **named** top-level window on the desktop (discovery; avoids helper-window noise). |

**Returns** — JSON array of `WindowDescriptor`:

```json
[
  {
    "processId": 14820,
    "windowHandle": 1247868,
    "title": "Winsight Sample - Counter",
    "processName": "SampleWpfApp",
    "isForeground": true,
    "x": 750, "y": 400, "width": 420, "height": 280,
    "kind": "main",
    "className": "HwndWrapper[SampleWpfApp;;...]",
    "ownerHandle": 0,
    "isModal": false
  }
]
```

`kind` is one of `main`, `dialog` (owned captioned window or Win32 `#32770`,
including MessageBox), `popup` (captionless overlay — dropdown, flyout, context
menu), `menu` (Win32 `#32768`), `tooltip`, or `other`. `ownerHandle` is the
owning window's handle (`0` when none), and `isModal` is true when the window
owns a disabled parent (the platform's modal-dialog signal). The classification
is Win32-style/ownership based and deterministic.

**Typical flow** — call `list_windows` with the target `processId` after an
action that may open a dialog or menu; pick the entry whose `kind` is `dialog`
(or `popup`/`menu`), then scope `click_element`, `type_text`, `wait_for_element`,
`capture_screenshot`, etc. to it with `windowTitle`. The topmost overlay is the
first matching entry, since the list is ordered front-to-back.

## Locator semantics (shared by the UIA tools)

`click_element`, `type_text`, `select_item`, `get_element_state`,
`wait_for_element`, and `hover_element` share the same locator contract:

| Criterion      | Match                                                     |
| -------------- | --------------------------------------------------------- |
| `automationId` | Exact, case-sensitive                                      |
| `name`         | Exact, case-sensitive                                      |
| `className`    | Exact, case-sensitive                                      |
| `controlType`  | Exact, case-insensitive (`Button`, `Edit`, `CheckBox`, …)  |
| `nameContains` | Substring, case-insensitive                                |
| `descendantText` | Substring, case-insensitive, in a bounded descendant Name or Value |
| `index`        | Zero-based pick when several elements match the rest       |

All provided criteria combine with AND. At least one criterion other than
`index` must be set.

- **Multi-window** — the search covers every visible top-level window of
  the process (including owned modal dialogs, which UIA hides from desktop
  enumeration; Winsight reaches them through Win32 handles). Use
  `windowTitle` (case-insensitive substring of the window title) to scope
  the search to one window, e.g. a modal dialog.
- **Strict mode** — when several elements match and no `index` is given,
  the call fails and lists the candidates (`index`, `automationId`,
  `name`, `controlType`, `windowTitle`) in both the error message and
  `data`, so an agent can refine its locator in one round trip.
- **Near misses** — when nothing matches, elements whose id or name
  partially overlaps the requested terms are suggested in the error.
- **Descendant text** — `descendantText` checks descendants up to depth 5.
  It targets WPF controls whose parent exposes a non-human `Name`
  (for example a view-model type name) while the useful label is carried
  by a child `TextBlock` or value-bearing descendant.
- **Off-screen elements** are scrolled into view (ScrollItem pattern)
  before any physical mouse action.

## `inspect_ui_tree`

Walk the UI Automation tree of a process window.

**Parameters**

| Name           | Type    | Default | Description                                                |
| -------------- | ------- | ------- | ----------------------------------------------------------- |
| `processId`    | int     | —       | Process id obtained from list_windows                       |
| `maxDepth`     | int     | 5       | Tree depth, capped at 20                                    |
| `windowTitle`  | string? | null    | Inspect the first window whose title contains this text     |
| `controlType`  | string? | null    | Keep only nodes of this control type (plus their ancestors) |
| `nameContains` | string? | null    | Keep only nodes whose Name contains this text (plus ancestors) |

**Returns** — JSON `UiElementNode` (recursive). `value` carries the current
Value-pattern content (text boxes, combo boxes) and is null when the
element does not support the pattern, so the tree doubles as a read-only
oracle:

```json
{
  "automationId": "MainWindow",
  "name": "Winsight Sample - Counter",
  "controlType": "Window",
  "className": "Window",
  "isEnabled": true,
  "isVisible": true,
  "x": 750, "y": 400, "width": 420, "height": 280,
  "children": [
    {
      "automationId": "IncrementButton",
      "name": "Increment",
      "controlType": "Button",
      "...": "..."
    }
  ]
}
```

## `click_element`

Locate an element (see [Locator semantics](#locator-semantics-shared-by-the-uia-tools))
and click it.

**Parameters**

| Name           | Type    | Required | Description                                        |
| -------------- | ------- | -------- | -------------------------------------------------- |
| `processId`    | int     | yes      | Target process id                                  |
| `automationId` | string? | locator  | UIA AutomationId                                   |
| `name`         | string? | locator  | UIA Name                                           |
| `className`    | string? | locator  | UIA ClassName                                      |
| `controlType`  | string? | locator  | UIA control type                                   |
| `nameContains` | string? | locator  | Name substring                                     |
| `descendantText` | string? | locator | Descendant Name/Value substring                    |
| `index`        | int?    | no       | Pick among multiple matches                        |
| `windowTitle`  | string? | no       | Scope to windows whose title contains this text    |
| `clickStyle`   | string? | no       | `single` (default), `double`, or `right`           |

**Resolution order** (`single`): Invoke pattern → Toggle pattern → mouse
click on center. `double` and `right` are always physical mouse actions.

**Returns** — `OperationResult`:

```json
{ "success": true }
```

or, on ambiguity:

```json
{
  "success": false,
  "error": "Locator ... matched 3 elements ... Candidates: [0] automationId=\"IncrementButton\" ...",
  "data": [ { "index": 0, "automationId": "IncrementButton", "name": "Increment", "controlType": "Button", "className": "Button", "windowTitle": "Winsight Sample - Counter" } ]
}
```

## `click_point`

Click at a coordinate without locating an element through UI Automation. This
is the action partner of [`find_element_visually`](#find_element_visually) and
the escape hatch for surfaces UIA cannot address (custom WPF Canvas, SkiaSharp,
DirectX). The target window should be visible and unoccluded so the click lands
on it.

**Parameters**

| Name              | Type    | Required | Description                                                        |
| ----------------- | ------- | -------- | ------------------------------------------------------------------ |
| `processId`       | int     | yes      | Target process id                                                  |
| `x`               | int     | yes      | X coordinate of the click (window-relative by default)             |
| `y`               | int     | yes      | Y coordinate of the click (window-relative by default)             |
| `clickStyle`      | string? | no       | `single` (default), `double`, or `right`                           |
| `windowTitle`     | string? | no       | Restrict the target window to those whose title contains this text |
| `coordinateSpace` | string? | no       | `window` (default) or `screen`                                     |

`coordinateSpace=window` interprets `x,y` in the target window's coordinate
space — the **same origin as a full-window screenshot**, so the `x,y` returned
by `find_element_visually` can be passed straight through. The tool resolves the
window origin (topmost visible window, or the one scoped by `windowTitle`),
verifies the point falls inside the window bounds, and translates it to screen
space. `coordinateSpace=screen` treats `x,y` as absolute desktop coordinates and
clicks them directly.

Unlike `click_element`, there is no UIA pattern resolution: every style is a
physical mouse action at the computed point.

**Returns** — `OperationResult` whose `data` reports the absolute screen point
that was clicked:

```json
{ "success": true, "data": { "screenX": 846, "screenY": 432 } }
```

A window-relative point outside the resolved window bounds is an explicit
failure that reports the window size, so the agent can correct the coordinate.

## `type_text`

Locate an element and write text into it. Same locator parameters as
`click_element` (minus `clickStyle`), plus:

| Name   | Type   | Required | Description  |
| ------ | ------ | -------- | ------------ |
| `text` | string | yes      | Text to send |
| `focusWindow` | bool | no | Bring the matched top-level window to the foreground before writing; fails if `GetForegroundWindow` does not match |

**Resolution order**: Value pattern (when writable) → focus + keyboard.Type.

**Returns** — `OperationResult` (same shape as `click_element`).

## `send_key_chord`

Focus a target top-level window, verify it became the foreground window,
then send a keyboard chord through FlaUI's SendInput-backed keyboard
primitive. This tool does not use `SendKeys`.

| Name          | Type    | Required | Description                                      |
| ------------- | ------- | -------- | ------------------------------------------------ |
| `processId`   | int     | yes      | Target process id                                |
| `chord`       | string  | yes      | Chord such as `Ctrl+Shift+N`, `Alt+F4`, `Ctrl+Enter` |
| `windowTitle` | string? | no       | Scope focus to windows whose title contains this text |

The focus step restores the selected window, calls `SetForegroundWindow`,
then polls `GetForegroundWindow` for up to 1000 ms. If the foreground
handle does not become the selected window, the tool returns
`success: false` with the expected and actual window handles.

**Returns**

```json
{
  "success": true,
  "data": {
    "chord": "Ctrl+Shift+N",
    "windowTitle": "Winsight Sample - Counter",
    "windowHandle": 1247868
  }
}
```

## `hover_element`

Locate an element, move the mouse to its center, and capture a newly
appearing nearby tooltip. Tooltips already present before the hover are
filtered out, and candidates are filtered by proximity to the hover point.

Uses the shared locator parameters, plus:

| Name                  | Type    | Required | Description                                      |
| --------------------- | ------- | -------- | ------------------------------------------------ |
| `expectedTooltipText` | string? | no       | Case-insensitive substring assertion on tooltip text |
| `timeoutMs`           | int?    | no       | Tooltip wait timeout; default 1500, max 10000    |

When `expectedTooltipText` is provided, absence of a matching tooltip is
an explicit failure. Without an expected text, the hover succeeds and
returns the captured tooltip when one appears, or `tooltip: null`.

**Returns**

```json
{
  "success": true,
  "data": {
    "x": 960,
    "y": 540,
    "tooltip": {
      "text": "Increment counter by one",
      "x": 912,
      "y": 566,
      "width": 176,
      "height": 32
    }
  }
}
```

## `select_item`

Select an item by its visible text inside a located container. The
container locator uses the shared locator parameters.

| Name       | Type   | Required | Description                        |
| ---------- | ------ | -------- | ---------------------------------- |
| `itemName` | string | yes      | Visible text of the item to select |

**Container handling**

| Container control type | Strategy                                       |
| ---------------------- | ----------------------------------------------- |
| `ComboBox`             | FlaUI ComboBox select + collapse                |
| `List`                 | FlaUI ListBox select                            |
| `Tab`                  | Tab item select                                 |
| anything else          | Descendant search for a SelectionItem element   |

On a missing item, the error lists the available item names.

**Returns** — `OperationResult`.

## `get_element_state`

Read the observable state of a located element — the lightweight UI-level
oracle for assertions. Prefer it over `read_managed_state` when the
on-screen state is enough. Uses the shared locator parameters.

**Returns** — `OperationResult` wrapping an `ElementState`:

```json
{
  "success": true,
  "data": {
    "automationId": "FeatureToggle",
    "name": "Enable feature",
    "controlType": "CheckBox",
    "className": "CheckBox",
    "windowTitle": "Winsight Sample - Counter",
    "isEnabled": true,
    "isVisible": true,
    "hasKeyboardFocus": false,
    "value": null,
    "toggleState": "On",
    "isSelected": null,
    "selectedItem": null,
    "x": 766, "y": 612, "width": 102, "height": 15
  }
}
```

Pattern-backed fields are null when the element does not support the
corresponding pattern: `value` (Value pattern), `toggleState` (Toggle),
`isSelected` (SelectionItem, e.g. a list item), `selectedItem` (Selection —
the name of the first selected child of a container such as a ComboBox).

## `wait_for_element`

Poll until a condition holds for a locator, or fail after a timeout. This
is the synchronization primitive: wait before clicking (element appears /
enables) and after acting (dialog closes). Uses the shared locator
parameters, plus:

| Name             | Type    | Default | Description                                                  |
| ---------------- | ------- | ------- | ------------------------------------------------------------ |
| `condition`      | string  | —       | `exists`, `not_exists`, `enabled`, `visible`, `value_equals`, `value_contains`, or `stable_for` |
| `expectedValue`  | string? | null    | Required for `value_equals` and `value_contains`              |
| `timeoutMs`      | int?    | 10000   | Max wait; overridable globally via `WINSIGHT_UIA_WAIT_TIMEOUT_MS`; capped at 120000 |
| `pollIntervalMs` | int?    | 250     | Delay between polls; min 50                                   |
| `stableForMs`    | int?    | null    | Required unchanged-state duration for `stable_for`            |

`exists` is satisfied by one or more matches; `not_exists` by zero.
`enabled`, `visible`, `value_equals`, `value_contains`, and `stable_for`
require a unique match (use `index` to disambiguate) and fail fast with
candidates when several elements match. `value_equals` compares the
Value-pattern content exactly; `value_contains` is a case-insensitive
substring check. `stable_for` passes only after the full `ElementState`
snapshot remains unchanged for `stableForMs`, which is useful for
anti-debounce assertions after server-filtered UI updates.

**Returns** — `OperationResult` wrapping a `WaitOutcome` (also attached to
timeout failures):

```json
{
  "success": true,
  "data": {
    "condition": "Exists",
    "conditionMet": true,
    "elapsedMs": 312,
    "matchCount": 1,
    "state": { "automationId": "NoteInput", "...": "..." }
  }
}
```

## `read_managed_state`

Attach to a managed process and read a field on the first heap instance of
the requested type.

**Parameters**

| Name        | Type   | Description                                                    |
| ----------- | ------ | -------------------------------------------------------------- |
| `processId` | int    | Target process id                                              |
| `typeName`  | string | Fully qualified .NET type name (namespace + class)             |
| `fieldName` | string | Field name on the type, including backing-field naming         |

**Returns** — `OperationResult` with `data` carrying the field value:

```json
{ "success": true, "data": 42 }
```

For reference fields, `data` is a string of the form
`"Type.Name @ 0xADDRESS"` (a hex pointer). Primitive value types come back
as native JSON numbers / booleans / strings.

**Important constraints**

- The MCP server process **must** match the target's bitness (x64 here).
- Attach is non-suspending. The target keeps running during inspection, so
  the field's value is read at one point in time and may have been mutated
  by the time the agent sees the result.
- ClrMD does not evaluate getters or properties. To read `MyVm.Count` where
  `Count` is `get => _internalCount;`, query the backing field
  `_internalCount` directly.

## `capture_screenshot`

Capture a PNG of the target process's topmost visible window using
`PrintWindow` with `PW_RENDERFULLCONTENT`, or of the first visible window whose
title contains `windowTitle`. This flag makes DirectX-composed WPF windows and
other accelerated surfaces render correctly into the bitmap; without it the
result is often a black rectangle.

**Parameters**

| Name          | Type   | Required | Description                                            |
| ------------- | ------ | -------- | ------------------------------------------------------ |
| `processId`   | int    | yes      | Target process id                                      |
| `outputPath`  | string | no       | Absolute file path. Default: a timestamped `screenshot-{utc-timestamp}.png` under `WINSIGHT_OUTPUT_DIR` (default `%TEMP%\winsight`) |
| `windowTitle` | string | no       | Case-insensitive substring of the target window title. Use this to capture modal dialogs, popups, or secondary windows. |

> **Output directory** — when `outputPath` is omitted, the capture is written
> under the directory named by the `WINSIGHT_OUTPUT_DIR` environment variable,
> falling back to `%TEMP%\winsight`. Set it to redirect captures into a folder a
> supervisor agent can read (e.g. a shared mount). The directory is created on
> first write.

**Returns** — `OperationResult` wrapping a `ScreenshotResult` payload:

```json
{
  "success": true,
  "data": {
    "path": "C:\\Users\\me\\AppData\\Local\\Temp\\winsight\\screenshot-20260509-143012-457.png",
    "width": 420,
    "height": 280,
    "capturedAtUtc": "2026-05-09T14:30:12.4571234Z"
  }
}
```

On failure (window minimized, `PrintWindow` returned false, output path
not writable, target requires administrator and the host does not, …):

```json
{
  "success": false,
  "error": "PrintWindow returned false (Win32 error 0x00000005=5). Window may be minimized, occluded, or use a composition surface that does not support PrintWindow (e.g. WinUI3 with DesktopChildSiteBridge)."
}
```

**Notes**

- Rendering occurs synchronously. Latency is dominated by the window's
  redraw cost (a few ms for typical WPF windows).
- The capture covers the window's bounding rectangle including non-client
  area (title bar, borders).
- Window selection uses visible top-level Win32 windows in z-order. When
  `windowTitle` is omitted, a visible modal dialog or popup owned by the
  process is usually captured before the main window. Pass `windowTitle` for
  deterministic dialog capture.
- Minimized windows, occluded windows, and modern WinUI3 composition
  surfaces (Win11 Notepad, recent Settings panes) may fail `PrintWindow`.
  The error message includes the Win32 error code so the agent can decide
  whether to retry, restore the window, or fall back to a different
  capture strategy.

## `compare_visual_baseline`

Capture the target process window **client area** (no chrome, for determinism)
and compare it to an approved PNG baseline. The comparison is baseline-based and
deterministic: SSIM (Gaussian 11x11, sigma 1.5, grayscale) as a global mean and
a worst-block local minimum, plus a pixelmatch-style YIQ pixel-diff ratio with
anti-aliased-edge detection. No ML model, GPU, or Python runtime is used.

**Parameters**

| Name                 | Type     | Required | Description                                      |
| -------------------- | -------- | -------- | ------------------------------------------------ |
| `processId`           | int      | yes      | Target process id                                |
| `baselineKey`         | string   | yes      | Relative logical key under the baseline directory, e.g. `counter-initial.png`; `.png` is added when omitted |
| `updateBaseline`      | bool     | no       | Replace the baseline with the current capture and return `verdict: "baseline_updated"` |
| `ssimThreshold`       | double?  | no       | Override `WINSIGHT_VISUAL_SSIM_THRESHOLD`        |
| `localSsimThreshold`  | double?  | no       | Override `WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD` (0..1) |
| `pixelDiffThreshold`  | double?  | no       | Override `WINSIGHT_VISUAL_PIXEL_THRESHOLD`       |
| `colorThreshold`      | double?  | no       | Override `WINSIGHT_VISUAL_COLOR_THRESHOLD` (0..1) |
| `ignoreRegions`       | object[] | no       | Rectangles `{ "x": 0, "y": 0, "width": 100, "height": 20 }` excluded from both metrics |

**Configuration**

| Variable                          | Default                                      | Purpose                 |
| --------------------------------- | -------------------------------------------- | ----------------------- |
| `WINSIGHT_VISUAL_BASELINE_DIR`    | `%LOCALAPPDATA%\winsight\visual-baselines`   | Approved baseline store |
| `WINSIGHT_VISUAL_SSIM_THRESHOLD`  | `0.98`                                       | Minimum passing global SSIM |
| `WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD` | `0.85`                                  | Minimum passing worst-block SSIM |
| `WINSIGHT_VISUAL_PIXEL_THRESHOLD` | `0.005`                                      | Maximum passing diff ratio |
| `WINSIGHT_VISUAL_COLOR_THRESHOLD` | `0.1`                                        | pixelmatch color sensitivity (0..1) |
| `WINSIGHT_OUTPUT_DIR`             | `%TEMP%\winsight`                            | Candidate captures and heatmaps |

Baselines live outside the repository by default. `baselineKey` must be a
relative `.png` path and cannot escape the configured baseline directory.

**Returns** — `OperationResult` wrapping a `VisualDiffResult` payload:

```json
{
  "success": true,
  "data": {
    "passed": false,
    "ssim": 0.972134,
    "pixelDiffRatio": 0.01125,
    "diffMapPath": "C:\\Users\\me\\AppData\\Local\\Temp\\winsight\\visual-diff-20260614-181233-118.png",
    "reasoning": "SSIM 0.972134 < 0.980000; pixel diff ratio 0.011250 > 0.005000.",
    "verdict": "fail",
    "baselinePath": "C:\\Users\\me\\AppData\\Local\\winsight\\visual-baselines\\counter-initial.png",
    "candidatePath": "C:\\Users\\me\\AppData\\Local\\Temp\\winsight\\visual-candidate-20260614-181233-063.png"
  }
}
```

If the baseline does not exist, the tool captures the window, writes that
capture as the new baseline, and returns a creation verdict instead of a
pass/fail comparison:

```json
{
  "success": true,
  "data": {
    "passed": true,
    "ssim": 1,
    "pixelDiffRatio": 0,
    "reasoning": "Visual baseline created at C:\\Users\\me\\AppData\\Local\\winsight\\visual-baselines\\counter-initial.png.",
    "verdict": "baseline_created",
    "baselinePath": "C:\\Users\\me\\AppData\\Local\\winsight\\visual-baselines\\counter-initial.png",
    "candidatePath": "C:\\Users\\me\\AppData\\Local\\Temp\\winsight\\visual-candidate-20260614-181233-063.png"
  }
}
```

**Notes**

- Images must have identical dimensions. Different dimensions are an explicit
  failure and produce a full-red heatmap when a diff map path is requested.
- Ignore regions are for dynamic UI such as clocks, cursors, caret blinks, or
  animations. Ignored pixels are excluded from both SSIM and pixel-diff.
- The verdict passes only when `ssim >= threshold` and
  `pixelDiffRatio <= threshold`.

## `detect_webview2_endpoints`

Walk the descendant process tree of the host pid, find every
`msedgewebview2.exe`, and report whether each one was launched with the
`--remote-debugging-port` flag (Chrome DevTools Protocol). When a port is
exposed and listening, an MCP-driving agent can then attach Playwright or
Puppeteer to `http://localhost:{port}` to drive the embedded web content
directly.

**Parameters**

| Name            | Type | Required | Description                          |
| --------------- | ---- | -------- | ------------------------------------ |
| `hostProcessId` | int  | yes      | The WPF/Win32 host pid (from list_windows) |

**Returns** — JSON object:

```json
{
  "endpoints": [
    {
      "hostProcessId": 14820,
      "webView2ProcessId": 17240,
      "remoteDebuggingPort": 9222,
      "isPortListening": true,
      "commandLine": "\"...\\msedgewebview2.exe\" --type=... --remote-debugging-port=9222 ..."
    }
  ],
  "hint": null
}
```

When no usable endpoint is found, `hint` carries an actionable message:

| Situation                           | Hint                                                                 |
| ----------------------------------- | -------------------------------------------------------------------- |
| No `msedgewebview2.exe` descendant  | App may not embed WebView2 yet, or it is still initializing.         |
| Found but no `--remote-debugging-port` | The host app must set `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222` (or `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`) before `EnsureCoreWebView2Async`. |
| Flag present but port not listening | Renderer may have crashed or not finished startup.                   |

**Notes**

- Detection uses a single WMI query (`Win32_Process`) and an
  `IPGlobalProperties.GetActiveTcpListeners()` snapshot — fast (~50 ms) and
  safe (no probe traffic on the target ports).
- Listening detection looks at any local interface, not only loopback —
  this matches WebView2's binding behavior.

## `list_cdp_targets`

List the debuggable targets of a Chrome DevTools Protocol endpoint
(`GET /json/list`). Use the port reported by `detect_webview2_endpoints`.

**Parameters**

| Name   | Type | Default | Description                                  |
| ------ | ---- | ------- | -------------------------------------------- |
| `port` | int  | —       | CDP debugging port (`remoteDebuggingPort`).   |

**Returns**

```json
{
  "targets": [
    {
      "id": "AB12",
      "title": "Terminal",
      "url": "https://appassets.example/index.html",
      "type": "page",
      "webSocketDebuggerUrl": "ws://127.0.0.1:9222/devtools/page/AB12"
    }
  ],
  "hint": null
}
```

`hint` is set when the endpoint answers but exposes no `page` target yet
(content still navigating — retry after the app finishes loading). Pass a
`page` target `id` to `evaluate_javascript` or `type_into_webview`.

## `evaluate_javascript`

Evaluate a JavaScript expression inside a WebView2 page over CDP
(`Runtime.evaluate` with `returnByValue` + `awaitPromise`). Use it to read
DOM state, extract visible text (`document.body.innerText`), or perform
ad-hoc actions UIA cannot reach.

**Parameters**

| Name         | Type    | Default | Description                                              |
| ------------ | ------- | ------- | -------------------------------------------------------- |
| `port`       | int     | —       | CDP debugging port.                                       |
| `targetId`   | string  | —       | Page target id from `list_cdp_targets`.                   |
| `expression` | string  | —       | JavaScript expression evaluated in the page context.      |
| `timeoutMs`  | int?    | null    | Per-command timeout; defaults to `WINSIGHT_CDP_TIMEOUT_MS` or 10000. |

**Returns** — a `CdpEvaluationResult`:

```json
{
  "success": true,
  "valueJson": "\"$ ls\\nREADME.md\"",
  "valueType": "string"
}
```

Script exceptions come back as `{ "success": false, "exceptionMessage":
"ReferenceError: ..." }` instead of a tool error, so the agent can read
and recover.

## `type_into_webview`

Type text into a WebView2 page as real key events
(`Input.dispatchKeyEvent` keyDown/keyUp pairs). Unlike the UIA-based
`type_text`, this reaches keydown-driven surfaces such as xterm.js
terminals hosted in `HwndHost` — the documented failure case for UIA
typing against embedded WebView2 content.

**Parameters**

| Name            | Type    | Default | Description                                                       |
| --------------- | ------- | ------- | ----------------------------------------------------------------- |
| `port`          | int     | —       | CDP debugging port.                                                |
| `targetId`      | string  | —       | Page target id from `list_cdp_targets`.                            |
| `text`          | string  | —       | Text to type. `\n` / `\r\n` become Enter, `\t` becomes Tab.        |
| `focusSelector` | string? | null    | CSS selector focused before typing (e.g. `.xterm-helper-textarea`). Fails explicitly when nothing matches. |
| `timeoutMs`     | int?    | null    | Per-command timeout; defaults to `WINSIGHT_CDP_TIMEOUT_MS` or 10000. |

**Returns**

```json
{ "success": true, "data": { "dispatchedEvents": 6 } }
```

**Notes**

- Enter is dispatched with `windowsVirtualKeyCode` 13 and text `\r`,
  matching what terminal emulators expect from a physical key press.
- A 10 ms delay separates successive key events so fast input is not
  coalesced by the page.

## `observe_counters`

Subscribe to .NET `EventCounters` from a managed process via EventPipe (the
official Microsoft diagnostic IPC, the same channel `dotnet-counters` and
`dotnet-trace` use). Use this as a **non-intrusive oracle**: read counter
values before and after an action to assert behavior without touching the
target process's memory or code.

**Why prefer this over `read_managed_state` in enterprise environments**:
EventPipe IPC is invisible to EDR products (CrowdStrike, SentinelOne,
Microsoft Defender for Endpoint). CLRMD attach uses
`CreateRemoteThread`/`WriteProcessMemory` patterns that match malware
heuristics.

**Parameters**

| Name           | Type   | Default            | Description                              |
| -------------- | ------ | ------------------ | ---------------------------------------- |
| `processId`    | int    | —                  | Managed target process id                |
| `providerName` | string | `System.Runtime`   | EventCounter provider                    |
| `durationMs`   | int    | 2000               | Observation window. Min 500, max 30000   |

**Returns** — JSON `CounterSnapshot`:

```json
{
  "processId": 14820,
  "providerName": "System.Runtime",
  "durationMs": 2000,
  "samplesObserved": 38,
  "counters": {
    "cpu-usage": 1.4,
    "working-set": 134217728,
    "gc-heap-size": 8388608,
    "gen-0-gc-count": 0,
    "exception-count": 0,
    "threadpool-thread-count": 12,
    "monitor-lock-contention-count": 0,
    "active-timer-count": 3,
    "alloc-rate": 152384
  }
}
```

**Common providers**

| Provider                            | Counters of interest                                      |
| ----------------------------------- | --------------------------------------------------------- |
| `System.Runtime` (default)          | cpu-usage, gc-heap-size, alloc-rate, exception-count, threadpool-thread-count |
| `Microsoft.AspNetCore.Hosting`      | requests-per-second, current-requests, failed-requests    |
| `System.Net.Http`                   | requests-started-rate, requests-failed-rate, http3.0-connections-current-total |
| `Microsoft-AspNetCore-Server-Kestrel` | connections-per-second, total-connections                |
| any custom `EventSource`            | counters declared via `EventCounter` / `IncrementingEventCounter` |

**Constraints**

- Target must be .NET Core 3.0+ / .NET 5+ / .NET 6+. Not supported on .NET
  Framework 4.x.
- Same user account as the target (or admin).
- The first ~1 second of the window may return empty counters because the
  runtime needs one interval to emit them. Default 2000 ms window is the
  minimum that reliably yields a full set.
- The `Counters` map records the **last** observed value per counter
  during the window. `samplesObserved` reports how many counter events
  arrived in total, useful to detect a process that died mid-collection.

## `capture_events`

Capture EventPipe events from one or more providers in a managed process.
Useful for observing every exception thrown, every custom `EventSource`
event, or any other diagnostic stream — without injecting code into the
target.

**Parameters**

| Name              | Type     | Default | Description                                              |
| ----------------- | -------- | ------- | -------------------------------------------------------- |
| `processId`       | int      | —       | Managed target process id                                |
| `providers`       | string[] | —       | EventSource provider names                               |
| `durationMs`      | int      | 3000    | Capture window. Min 500, max 30000                       |
| `maxEvents`       | int      | 500     | Hard cap on events kept (1..10000). Sets `truncated` if hit |
| `eventNameFilter` | string?  | null    | Exact-match (case-sensitive) event name filter           |

**Returns** — JSON `EventCaptureResult`:

```json
{
  "processId": 14820,
  "providers": ["Microsoft-Windows-DotNETRuntime"],
  "durationMs": 3000,
  "totalCaptured": 2,
  "truncated": false,
  "events": [
    {
      "timestampUtc": "2026-05-09T14:30:12.4571234Z",
      "providerName": "Microsoft-Windows-DotNETRuntime",
      "eventName": "ExceptionThrown_V1",
      "eventId": 80,
      "level": "Warning",
      "payload": {
        "ExceptionType": "System.InvalidOperationException",
        "ExceptionMessage": "Sequence contains no elements",
        "ExceptionEIP": "0x7ffabc123456",
        "ExceptionHRESULT": -2147467261,
        "ExceptionFlags": 16,
        "ClrInstanceID": 9
      }
    }
  ]
}
```

**Common patterns**

| Goal | Providers | eventNameFilter |
| ---- | --------- | --------------- |
| Every exception thrown | `["Microsoft-Windows-DotNETRuntime"]` | `"ExceptionThrown_V1"` |
| Task lifecycle events | `["System.Threading.Tasks.TplEventSource"]` | (none) |
| HTTP client requests | `["System.Net.Http"]` | (none) |
| Custom app telemetry | `["MyCompany.MyApp"]` | (specific event name) |

**Notes**

- Payloads are normalized to JSON-safe types: primitives pass through,
  `DateTime` is converted to UTC, `Guid` and enums become strings, byte
  arrays become hex, and dictionaries/enumerables recurse with a depth
  cap of 4.
- High-rate providers (`Microsoft-Windows-DotNETRuntime` with all
  keywords) can flood the cap quickly. Use `eventNameFilter` to narrow
  the stream.
- Same .NET Core 3.0+ / process-bitness constraints as `observe_counters`.

## `find_element_visually`

Locate a UI element from a natural-language description. The tool
captures a screenshot of the target window, submits it together with the
query to a vision model, and returns the bounding box. Use this when
`inspect_ui_tree` returns no useful AutomationId (custom Canvas,
SkiaSharp, DirectX surfaces, or any content UIA cannot see).

**Parameters**

| Name          | Type    | Required | Description                                      |
| ------------- | ------- | -------- | ------------------------------------------------ |
| `processId`   | int     | yes      | Target process id (from list_windows)            |
| `query`       | string  | yes      | Natural-language description of the target       |
| `windowTitle` | string? | no       | Case-insensitive substring of the target window title. Use this to capture modal dialogs, popups, or secondary windows before visual grounding. |
| `act`         | string? | no       | `single`, `double`, or `right` to click the grounded point in the same call (via `click_point`). Omitted by default — the tool only locates. |

**Returns** — JSON `GroundedElement`:

```json
{
  "x": 245,
  "y": 178,
  "width": 96,
  "height": 32,
  "confidence": 0.94,
  "reasoning": "Button labeled 'Increment' centered in the lower stack panel.",
  "source": "claude-sonnet-4-6",
  "screenX": 995,
  "screenY": 578
}
```

`x` and `y` are the **center** of the bounding box, in **screenshot
pixel space** (origin at the top-left of the captured window). `screenX`
and `screenY` are the same point already translated to **absolute screen
coordinates** (the window origin is added server-side), so they can be fed
straight to `click_point` with `coordinateSpace=screen`, or the window-relative
`x,y` to `click_point` with the default `coordinateSpace=window`. `screenX` /
`screenY` are null when the window origin could not be resolved.

**Acting in one call** — when `act` is set, the grounded point is clicked
through the same path as `click_point` (window-relative) and the result wraps
both the grounding and the click outcome:

```json
{
  "success": true,
  "data": {
    "grounded": { "x": 245, "y": 178, "...": "...", "screenX": 995, "screenY": 578 },
    "click": { "success": true, "data": { "screenX": 995, "screenY": 578 } }
  }
}
```

If the call fails — missing API key, network error, or unparseable model
output — the tool returns an `OperationResult` failure object instead:

```json
{ "success": false, "error": "ANTHROPIC_API_KEY environment variable is not set. ..." }
```

**Configuration** (environment variables)

| Variable                    | Default                                  | Purpose                          |
| --------------------------- | ---------------------------------------- | -------------------------------- |
| `WINSIGHT_VISION_BACKEND`   | `anthropic`                              | `anthropic` or `onnx`            |
| `ANTHROPIC_API_KEY`         | —                                        | Required for `anthropic`         |
| `WINSIGHT_VISION_MODEL`     | `claude-sonnet-4-6`                      | Anthropic model override         |
| `WINSIGHT_VISION_API_URL`   | `https://api.anthropic.com/v1/messages`  | Anthropic endpoint override      |
| `WINSIGHT_VISION_ONNX_PATH` | —                                        | Local Qwen2.5-VL ONNX model directory |
| `WINSIGHT_VISION_EP`        | `cpu`                                    | `cpu` or `dml` (CPU-only in this build) |
| `WINSIGHT_OUTPUT_DIR`       | `%TEMP%\winsight`                        | Directory for the intermediate `vision-{timestamp}.png` capture |

**Privacy** — screenshots are transmitted to the configured API when using
`WINSIGHT_VISION_BACKEND=anthropic`. `WINSIGHT_VISION_BACKEND=onnx` runs a local
Qwen2.5-VL model fully on-host and never falls back to Anthropic; see
[docs/local-onnx-grounder.md](local-onnx-grounder.md) for provisioning.
