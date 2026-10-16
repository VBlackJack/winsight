# Tools reference

This document describes the MCP tools exposed by `Winsight.Mcp` to AI agents.
For the preferred verified workflow, see [verified control](verified-control.md).
`act_verified` returns explicit `status`, `dispatched` and `verified` fields;
profile execution also reports its verified/stopped state. The OperationResult
convention below describes the original tools and observation/configuration calls.
Every tool returns a JSON string. Errors — whether from a known failure mode
or an unexpected exception inside the tool body — are uniformly surfaced
inside an `OperationResult` shape (`{ "success": false, "error": "..." }`)
rather than as MCP exceptions, so an agent can read and recover from them.
A universal `ToolSafety` guard at the tool boundary catches anything the
implementation throws and converts it into that shape, so a single rogue
exception cannot tear down the host process or kill the rest of the agent's
session.

## Session target (`set_target` / `get_target` / `clear_target`)

Every process-scoped tool takes `processId`, but you do not have to repeat it on
every call. `set_target` stores a **session default process**; afterwards any
tool that omits its own `processId` uses it. An explicit `processId` on a call
still overrides the default. This is the ad-hoc counterpart to the batch-level
`processId` of [`run_steps`](#run_steps).

| Tool           | Purpose                                                                    |
| -------------- | -------------------------------------------------------------------------- |
| `set_target`   | Set the session default process. Param `processId` (int). Returns `{ processId, windowCount }` — `windowCount` is how many top-level windows the process currently exposes (0 is allowed; it may still be starting). Call it right after `launch_app` or `list_windows`. |
| `get_target`   | Return `{ processId }`, or `{ processId: null }` when none is set.          |
| `clear_target` | Clear the default; subsequent calls must pass `processId` again.            |

When a tool's `processId` is omitted and no session target is set, the call
fails with a clear message telling you to pass `processId` or call `set_target`.
Throughout this reference, `processId` on a process-scoped tool is therefore
**optional** with this fallback, even where the parameter tables mark it required
for the underlying engine call.

## Agent-first workflow

The preferred tools are `observe_scene`, `act_verified`, `capture_scene`,
`observe_dom_scene`, `locate_scene_target`, `save_app_profile` and `run_app_profile`.
Their [contracts and examples](verified-control.md) distinguish action dispatch
from verified outcomes. Existing workflows listed below remain compatible.

These original convenience tools remain available. The lower-level tools remain
available, but agents should start here because the responses are oriented around
repairable action.

| Tool | Purpose |
| ---- | ------- |
| `observe` | One-call perception: windows, actionable elements, recommended locators, likely actions, session memory, optional screenshot path |
| `plan_steps` | Draft a conservative `run_steps` plan from a natural-language goal; dry-run it before execution |
| `repair_locator` | Rank replacement locators from the current UI plus observed locator memory |
| `act_and_observe` | Execute one `BatchStep` and return the action journal plus before/after structural diff |
| `click_and_wait` | Click a locator and wait for a post-click condition in one call |
| `type_and_assert` | Type text and immediately assert observable state |
| `get_agent_state` | Return compact target, guardrail, memory, last-failure, and recipe context |
| `get_session_memory` / `clear_session_memory` | Inspect or reset observed locators and recent repairable failures |
| `get_winsight_guide` | Return the embedded playbook for agents |

### `observe`

Use `observe` before acting on an unfamiliar UI. It combines `list_windows`,
`describe_ui`, session memory, likely next actions, and optional screenshot
capture.

| Name | Type | Default | Description |
| ---- | ---- | ------- | ----------- |
| `processId` | int? | session target | Target process |
| `windowTitle` | string? | null | Scope to a window title substring |
| `includeScreenshot` | bool | false | Include a screenshot path written under `WINSIGHT_OUTPUT_DIR` |
| `maxResults` | int | 100 | Max actionable elements |

The response is an `OperationResult` whose `data` has `windows`, `elements`,
`nextActions`, optional `screenshot`, and `memory`. Each actionable element
includes a stable `affordanceId` plus its `recommendedLocator`; each
`nextActions` entry names the preferred tool, ready-to-fill arguments,
required inputs, risk, expected feedback, and `locatorArgs`.

### Planning and Repair

`plan_steps(goal, text?, expectedValue?)` ranks visible controls and returns a
draft `{ dryRun: true, processId, steps }` payload for `run_steps`.
`repair_locator(query)` returns ranked `currentUi` candidates with `locatorArgs`
and memory matches. On missing targets or bad arguments, these high-level tools
return structured failure data:

```json
{
  "success": false,
  "error": "No processId given ...",
  "data": {
    "code": "missing_target",
    "message": "No processId given ...",
    "cause": "target_resolution",
    "suggestedTool": "set_target",
    "suggestedArgs": { "processId": "<target process id>" },
    "retryable": true
  }
}
```

### Agent State and Embedded Recipes

`get_agent_state` is the compact orientation call for agents that have lost
context. It returns:

- `target`: current session process id and whether a target exists.
- `guardrails`: dry-run, allowlist, and trace status.
- `memory`: recently observed locators and recent repairable failures.
- `lastFailure`: most recent failure, if any.
- `recommendedNextStep`: the next recovery/action step as a short string.
- `recipes`: built-in workflows such as `form_fill`, `dialog_flow`,
  `visual_click`, `webview_cdp`, and `locator_repair`.

### Macro Tools

`act_and_observe` is the generic one-step macro. Pass a single `BatchStep`; it
returns the action result, structural `change`, current `elements`, and optional
screenshot path. `click_and_wait` and `type_and_assert` are narrower macros for
common loops. Macro tools return `success=false` at the top level when an inner
action, wait, or assertion fails; the `data` object still carries the detailed
inner result journal for repair.

All mutating high-level tools honor `WINSIGHT_DRY_RUN`. For `act_and_observe`,
global dry-run behaves like the explicit `dryRun=true` argument: Winsight still
captures before/after observations and journals the would-run step, but does not
click, type, select, or hover.

#### `act_and_observe`

| Name | Type | Default | Description |
| ---- | ---- | ------- | ----------- |
| `step` | `BatchStep` | required | One step to execute or dry-run |
| `windowTitle` | string? | null | Scope observation to a window title substring |
| `includeScreenshot` | bool | false | Include an after-action screenshot path |
| `dryRun` | bool | false | Journal mutating actions without executing them |
| `processId` | int? | session target or step process | Target process |

#### `click_and_wait`

| Name | Type | Default | Description |
| ---- | ---- | ------- | ----------- |
| `automationId` / `name` / `controlType` / `nameContains` / `index` | locator fields | null | Click target |
| `condition` | string | `stable_for` | Wait condition after the click |
| `expectedValue` | string? | null | Required for `value_equals` / `value_contains` |
| `waitAutomationId` / `waitName` / `waitControlType` | locator fields | click locator | Optional distinct wait target |
| `timeoutMs` | int? | default wait timeout | Wait budget |
| `stableForMs` | int? | `350` for `stable_for`, capped by `timeoutMs` | Required unchanged-state duration |
| `windowTitle` | string? | null | Scope to a window title substring |
| `processId` | int? | session target | Target process |

#### `type_and_assert`

| Name | Type | Default | Description |
| ---- | ---- | ------- | ----------- |
| `text` | string | required | Text to type |
| `operator` | string | required | Assertion operator such as `equals`, `contains`, `enabled`, `checked` |
| `expected` | string? | null | Expected assertion operand |
| `automationId` / `name` / `controlType` / `nameContains` | locator fields | null | Type target |
| `assertAutomationId` / `assertName` | locator fields | type locator | Optional distinct assertion target |
| `windowTitle` | string? | null | Scope to a window title substring |
| `processId` | int? | session target | Target process |

## `get_user_activity`

Report whether a person is using this desktop. Read-only: it sends no input.

**Parameters**

| Name        | Type | Required | Description |
| ----------- | ---- | -------- | ----------- |
| `processId` | int? | no       | Process whose foreground window title may be returned. Falls back to the session target. |

**Returns** - `OperationResult` with:

| Field | Meaning |
| ----- | ------- |
| `humanIdleMs` | Milliseconds since the last input not injected by Winsight; null when none was seen |
| `lastInputAgeMs` / `lastInputSource` | Age of the most recent input of any kind, and whether it was `human` or `winsight` |
| `foreground` | `handle`, `processId`, `processName`; `title` only when the window belongs to the target process (`titleWithheld` says so) |
| `sessionLocked` | true, false, or null when Windows does not report it |
| `yieldToUserMs` / `userActive` | The `WINSIGHT_YIELD_TO_USER_MS` threshold, and whether a human input is more recent than it |

Winsight cannot see who produced an input without a low-level hook, which
endpoint products flag, so it attributes input by time. Every Winsight process
in the Windows session records the window of each injection in a shared
ledger; input inside that window (plus 50 ms) is Winsight's, anything else is
human. Consequences: a person's input made during a Winsight injection is
masked by it, and input injected by another tool counts as human.

## `wait_for_user_idle`

Wait until nobody has used the mouse or keyboard for `idleMs`, Winsight's own
input excluded. Read-only.

| Name        | Type | Required | Description |
| ----------- | ---- | -------- | ----------- |
| `idleMs`    | int  | yes      | Required quiet period, 1 to 600000 |
| `timeoutMs` | int  | yes      | Maximum wait, 0 to 600000; 0 checks once |
| `processId` | int? | no       | As for `get_user_activity` |

**Returns** - `{ idle, waitedMs, idleMs, activity }`, where `activity` is a
`get_user_activity` snapshot. On timeout the call fails with `timed_out` and the
same data.

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
(`CloseMainWindow`) and waits up to five seconds. If the application remains
open, the call returns a failure with `stillRunning: true` and `closeRequested`.
Complete any save confirmation before retrying. Only `force=true` kills the
process tree; a graceful request never escalates to a kill.

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
  `name`, `controlType`, `windowTitle`, `score`, `reason`) in both the
  error message and `data`, so an agent can refine its locator in one
  round trip.
- **Near misses** — when nothing matches, elements whose id or name
  partially overlaps the requested terms are suggested in the error, with
  lower scores and a `reason` such as `partialText`.
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
| `mainWindow`   | bool    | false   | Inspect the process main window even when a dialog or overlay is on top of it; ignored with `windowTitle` |

Without `windowTitle`, the inspected window is the topmost one that is not a
transient overlay. An open tooltip, menu, dropdown or flyout (an owned
captionless popup, a `WS_EX_NOACTIVATE` or tool window, or a native menu or
tooltip class) sits at the top of the z-order but is skipped, so hovering an
element no longer makes `inspect_ui_tree` return the tooltip popup. A modal
dialog is not an overlay and is still chosen when it is on top.

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

## `describe_ui`

Answer "what can I do here?" — return the window's **actionable** elements as a
compact, position-ordered list (top-to-bottom, left-to-right) instead of a full
UIA tree. Use it to orient on an unfamiliar window before acting; reach for
[`inspect_ui_tree`](#inspect_ui_tree) only when you need the full structure.

**Parameters**

| Name          | Type    | Default | Description                                                        |
| ------------- | ------- | ------- | ------------------------------------------------------------------ |
| `windowTitle` | string? | null    | Inspect the first window whose title contains this text (dialogs, popups) |
| `maxResults`  | int     | 200     | Maximum elements to return                                         |
| `processId`   | int?    | null    | Target process id; falls back to the session target (see [Session target](#session-target-set_target--get_target--clear_target)) |

Interactable kinds kept: buttons, text inputs, checkboxes, radio buttons, combo
boxes, lists and list items, tabs and tab items, menu items, hyperlinks, tree
items, sliders. Static text and layout containers are dropped.

**Returns** — `OperationResult` whose `data` is an array of `UiInteractable`,
each with a `recommendedLocator` (the smallest criterion that targets it —
`automationId` if unique, else a unique `name`, else `controlType` + `index`)
ready to feed `click_element` / `type_text` / `select_item`:

```json
{
  "success": true,
  "data": [
    {
      "controlType": "Button",
      "automationId": "IncrementButton",
      "name": "Increment",
      "value": null,
      "toggleState": null,
      "isSelected": null,
      "isEnabled": true,
      "isVisible": true,
      "x": 766, "y": 470, "width": 102, "height": 40,
      "windowTitle": "Winsight Sample - Counter",
      "recommendedLocator": "automationId=\"IncrementButton\""
    }
  ]
}
```

The walk is breadth-first over the process windows, so a `controlType=…
index=N` recommendation uses the same index the locator tools do.

## `snapshot_ui` / `diff_ui`

The feedback sense: see what an action changed without re-inspecting everything.
`snapshot_ui` captures a structural baseline of the process (its windows plus its
actionable elements); `diff_ui` captures the current state, reports the delta
against the baseline, and re-baselines so successive calls chain.

Typical flow: `snapshot_ui` → act → `diff_ui`. With no prior snapshot, `diff_ui`
captures a baseline and returns `verdict: "baseline_captured"`.

**Parameters** (both tools)

| Name          | Type    | Default | Description                                                  |
| ------------- | ------- | ------- | ------------------------------------------------------------ |
| `windowTitle` | string? | null    | Scope the element capture to a window whose title contains this text |
| `processId`   | int?    | null    | Target process id; falls back to the session target          |

**`snapshot_ui` returns** — `OperationResult` with `{ processId, windowCount, interactableCount }`.

**`diff_ui` returns** — `OperationResult` whose `data` is a `UiChange`:

```json
{
  "success": true,
  "data": {
    "verdict": "changed",
    "windowsOpened": [ { "title": "Winsight Sample - Note", "kind": "dialog", "isModal": true, "...": "..." } ],
    "windowsClosed": [],
    "elementsAdded": [ { "controlType": "Edit", "automationId": "NoteInput", "recommendedLocator": "automationId=\"NoteInput\"", "...": "..." } ],
    "elementsRemoved": [],
    "elementsChanged": [
      {
        "controlType": "CheckBox",
        "automationId": "FeatureToggle",
        "name": "Enable feature",
        "windowTitle": "Winsight Sample - Counter",
        "recommendedLocator": "automationId=\"FeatureToggle\"",
        "changes": [ { "field": "toggleState", "before": "Off", "after": "On" } ]
      }
    ]
  }
}
```

`verdict` is `no_change` when nothing differs. Windows are matched by handle;
elements by a stable key (window + unique AutomationId, else control type + name,
with duplicate occurrences matched in order); persisting elements are compared on
name, value, toggle, selection, enabled, and visible.

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
| `allowOutOfWindow` | bool   | no       | With `coordinateSpace=screen`, allow points outside the target window bounds. Default false. |
| `focusWindow`     | bool    | no       | Raise the target window and verify it owns the foreground before the point is validated. Default false. |
| `settleMs`        | int?    | no       | Milliseconds the pointer rests on the point before the press, 0 to 2000. Default 150. |

`coordinateSpace=window` interprets `x,y` in the target window's coordinate
space — the **same origin as a full-window screenshot**, so the `x,y` returned
by `find_element_visually` can be passed straight through. The tool resolves the
window origin (topmost visible window, or the one scoped by `windowTitle`),
verifies the point falls inside the window bounds, and translates it to screen
space. `coordinateSpace=screen` treats `x,y` as absolute desktop coordinates,
but still verifies the point falls inside a visible window of the target process
(after any `windowTitle` filter) unless `allowOutOfWindow=true` is set
intentionally.

Immediately before dispatch, Winsight also checks the window identity, geometry,
and the process/window under the point. A covering window causes a failure.
`allowOutOfWindow` never authorizes clicking another process.
A child window of another process parented into the target window counts as
the target: a WebView2 surface renders in a `Chrome_WidgetWin_1` child owned by
`msedgewebview2.exe`, and a point above it is accepted when its root ancestor
(`GetAncestor(GA_ROOT)`) is the target window. Another top-level window, or a
child hosted in another process's window, on top of the point is still refused. Input calls within
one server are serialized; other desktop users or processes can still change
the desktop between the native check and input dispatch.

Unlike `click_element`, there is no UIA pattern resolution: every style is a
physical mouse action at the computed point.

**Pointer settle** - the click happens in three beats: the pointer moves to the
point, rests for `settleMs`, is read back, and the button is pressed without
moving again. Immediate-mode UIs (iced on winit, egui) resolve hover from the
last `WM_MOUSEMOVE` once per frame and act on that hover when the button goes
down; a move and a press sent together act on the previous position. If the
pointer is no longer at the point after the rest (more than one pixel away),
the press is refused with `cursor_moved`: something else moved it, often a
person. The point recipient is checked again after the rest, because hovering
can open a popup over it. Double and right clicks follow the same path, as do
the physical clicks of `click_element`, `find_element_visually` and
`act_verified`.

**Covered window** - without `focusWindow`, a point covered by another window,
including another window of the same process, is refused. With
`focusWindow=true` the window is raised first (the same verified focus as
`send_key_chord` and `drag`), its frame is resolved again, and the call fails
if the window changed while it was raised.

**Returns** — `OperationResult` whose `data` reports the absolute screen point
that was clicked:

```json
{ "success": true, "data": { "screenX": 846, "screenY": 432 } }
```

A point outside the resolved window bounds is an explicit failure that reports
the window size, so the agent can correct the coordinate.

> **Targeting a specific window** — without `windowTitle`, the window origin is
> the **topmost** visible window of the process, which can be a transient
> tooltip or popup rather than the one you measured against. When clicking a
> point relative to a specific surface, pass `windowTitle`; this also narrows
> `coordinateSpace=screen` bounds checks to the intended window family.

## `scroll_point` / `hover_point`

Turn the mouse wheel, or rest the pointer, at a coordinate, for lists, canvases
and hover effects that UI Automation cannot reach. Both take the `click_point`
parameters except `clickStyle` (`x`, `y`, `windowTitle`, `coordinateSpace`,
`allowOutOfWindow`, `focusWindow`, `settleMs`, `processId`) and run the same
checks: the point must reach a visible target window, and the pointer settles
on it and is read back before anything else happens.

| Name | Tool | Type | Description |
| ---- | ---- | ---- | ----------- |
| `delta` | `scroll_point` | int | Wheel notches, -50 to 50 and not 0. Positive scrolls up, or right with `horizontal` |
| `horizontal` | `scroll_point` | bool | Turn the horizontal wheel. Default false |
| `holdMs` | `hover_point` | int? | Time the pointer stays after the settle, 0 to 10000; it is checked again afterwards |

Windows delivers the wheel to the window under the pointer, which is why the
pointer moves there first. `hover_point` presses nothing; capture or
`read_text_visually` afterwards to see what appeared. Both return the absolute
`screenX`/`screenY`, and fail with `cursor_moved` if other input moved the pointer.

## `type_text`

Locate an element and write text into it. Same locator parameters as
`click_element` (minus `clickStyle`), plus:

| Name   | Type   | Required | Description  |
| ------ | ------ | -------- | ------------ |
| `text` | string | yes      | Text to send |
| `focusWindow` | bool | no | Bring the matched top-level window to the foreground before writing; fails if `GetForegroundWindow` does not match |
| `allowWebView2Input` | bool | no | Write even when the element sits inside a WebView2 surface (default false) |

**Resolution order**: Value pattern (when writable) → focus + keyboard.Type.

An element inside a WebView2 surface is refused before anything is written:
synthetic keyboard input into WebView2 has crashed `ctfmon`, the text input
host. Use `type_into_webview` (CDP) for web content; see `get_focused_element`
for the detection signals.

**Returns** — `OperationResult` (same shape as `click_element`).

## `send_key_chord`

Focus a target top-level window, verify it became the foreground window,
then send a keyboard chord through `SendInput`. This tool does not use
`SendKeys`.

| Name          | Type    | Required | Description                                      |
| ------------- | ------- | -------- | ------------------------------------------------ |
| `processId`   | int     | yes      | Target process id                                |
| `chord`       | string  | yes      | Chord such as `Ctrl+Shift+N`, `Alt+F4`, `Ctrl+Enter` |
| `windowTitle` | string? | no       | Scope focus to windows whose title contains this text |
| `allowWebView2Input` | bool | no    | Send even when keyboard focus is inside a WebView2 surface (default false) |

The focus step restores the selected window (an open tooltip or menu is
skipped, see `inspect_ui_tree`), calls `SetForegroundWindow`, then polls
`GetForegroundWindow` for up to 1000 ms. If the foreground handle does not
become the selected window, the tool returns `success: false` with the
expected and actual window handles. If keyboard focus then sits inside a
WebView2 surface, nothing is sent and the failure carries the focused element.

**Dispatch.** Every transition carries its hardware scan code, and keys of the
extended block (PageUp, PageDown, Home, End, Insert, Delete, the arrows, right
Ctrl and Alt, the Windows keys) carry `KEYEVENTF_EXTENDEDKEY`. Modifiers go
down first in chord order, the other keys follow, everything is released in
reverse order, with 15 ms between transitions so a `WH_KEYBOARD_LL` hook that
reads `GetAsyncKeyState` sees each modifier settled. Without the extended flag
and scan code, Windows reads PageUp as numeric-keypad 9 and, with NumLock on
and Shift held, injects a fake Shift release around it: the application
receives Ctrl+PageUp instead of Ctrl+Shift+PageUp. The WPF lab sample
(`samples/WpfLabApp`) logs the hook, `WM_KEYDOWN` and WPF views of each stroke.

**Returns** (`focus` is the element that received the chord, shaped like
`get_focused_element`)

```json
{
  "success": true,
  "data": {
    "chord": "Ctrl+Shift+N",
    "windowTitle": "Winsight Sample - Counter",
    "windowHandle": 1247868,
    "focus": { "controlType": "Edit", "automationId": "NoteInput", "isWebView2": false, "...": "..." }
  }
}
```

## `get_focused_element`

Report where keyboard input would land right now. Read-only; takes only the
optional `processId`.

**Returns** (`FocusedElementInfo`)

```json
{
  "success": true,
  "data": {
    "controlType": "Edit",
    "automationId": "",
    "name": "Terminal input",
    "className": "xterm-helper-textarea",
    "frameworkId": "Chrome",
    "processId": 154408,
    "processName": "msedgewebview2",
    "belongsToTarget": true,
    "isWebView2": true,
    "webView2Evidence": ["frameworkId=Chrome", "process=msedgewebview2",
                         "win32Focus=Chrome_WidgetWin_1", "uiaClass=Chrome_RenderWidgetHostHWND"],
    "win32FocusClassName": "Chrome_WidgetWin_1",
    "topLevelWindowHandle": 25827204,
    "topLevelWindowTitle": "Heimdall - Remote Access Control Center",
    "x": 1984, "y": 497, "width": 7, "height": 17
  }
}
```

`isWebView2` is true when any signal places the focus in WebView2: a Chromium
`frameworkId`, an element owned by `msedgewebview2`, a Chromium window class
holding Win32 focus, or a Chromium or `WebView2` class among the UIA ancestors.
`belongsToTarget` is true when the element belongs to the target process or to
a window it hosts (WebView2 renders in its own process, parented into the host
window). `type_text` and `send_key_chord` refuse to send into WebView2 unless
`allowWebView2Input=true`.

## `hover_element`

Locate an element, move the mouse to its center, and capture a newly
appearing nearby tooltip. Tooltips already present before the hover are
filtered out, and candidates are filtered by proximity to the hover point.

Uses the shared locator parameters, plus:

| Name                  | Type    | Required | Description                                      |
| --------------------- | ------- | -------- | ------------------------------------------------ |
| `expectedTooltipText` | string? | no       | Case-insensitive substring assertion on tooltip text |
| `timeoutMs`           | int?    | no       | Tooltip wait timeout; default 1500, max 10000    |

Tooltips are read from the target process's transient windows (a WPF
`Popup` hosting a `ToolTip`, or a `tooltips_class32` window), and the popup is
awaited for the whole timeout. The element's `AutomationProperties.HelpText`
is only a fallback when no popup opened. `tooltip.source` tells which one the
text came from: `popup` (bounds and `windowHandle` are the popup's) or
`helpText` (bounds are the element's, `windowHandle` is 0).

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
      "height": 32,
      "source": "popup",
      "windowHandle": 3409212
    }
  }
}
```

## `select_item`

Select an item inside a located container. The container locator uses the
shared locator parameters. Name the item with exactly one of:

| Name               | Type    | Description                                          |
| ------------------ | ------- | ---------------------------------------------------- |
| `itemName`         | string? | Exact accessible name                                |
| `itemNameContains` | string? | Case-insensitive substring of the name               |
| `itemNamePattern`  | string? | .NET regular expression the name must match (250 ms evaluation cap) |

Substring and pattern selection survive names that carry live data, such as a
tree row named `lab-c, protocol SFTP, state Reachable (1 ms) · 13:03:33` whose
clock changes every second: `itemNameContains: "lab-c,"` keeps matching. They
are strict: when several items match, the call fails and lists them. An exact
`itemName` keeps the historical first-match behavior.

**Container handling**

| Container control type | Strategy                                       |
| ---------------------- | ----------------------------------------------- |
| `ComboBox`             | FlaUI ComboBox select + collapse                |
| `List`                 | FlaUI ListBox select                            |
| `Tab`                  | Tab item select                                 |
| anything else          | Descendant search for a SelectionItem element   |

On a missing item, the error lists the available item names.

**Returns**: an `OperationResult` whose `data.selectedName` is the full name of
the selected item.

## `drag`

Drag with the left mouse button from a source to a target, paced for OLE drag
and drop (`DragDrop.DoDragDrop` in WPF): press on the source, hold, leave the
system drag rectangle (`SM_CXDRAG`), move in steps, step one pixel past the
target and back so the drop target receives a final `DragOver`, wait, release.
The button is always released, even when a move fails.

Each end is either an element or a point:

| Name | Type | Description |
| ---- | ---- | ----------- |
| `sourceAutomationId`, `sourceName`, `sourceNameContains`, `sourceControlType`, `sourceIndex` | | Source element (strict locator, center) |
| `sourceX`, `sourceY` | int? | Source point instead of an element |
| `targetAutomationId`, `targetName`, `targetNameContains`, `targetControlType`, `targetIndex` | | Target element |
| `targetX`, `targetY` | int? | Target point instead of an element |
| `targetOffsetX`, `targetOffsetY` | int | Offset from the target element's center (e.g. its left half) |
| `coordinateSpace` | string | `window` (default) or `screen`, for the points |
| `holdMs` | int | Hold before the first move; default 150, max 5000 |
| `steps` | int | Intermediate moves; default 12, range 1-100 |
| `stepDelayMs` | int | Pause between moves; default 25, max 1000 |
| `dropDelayMs` | int | Hover on the target before releasing; default 250, max 5000 |
| `allowOutOfWindow` | bool | Let the target lie outside the target process windows (drop into another application) |
| `windowTitle` | string? | Scope element lookup and window-relative points |

The target window is brought to the foreground first (an open tooltip or menu
is skipped), since a window left on top of it would swallow the press. Both
points must then reach a window of the target process (checked with
`WindowFromPoint`) unless `allowOutOfWindow` releases the target check. As
with `click_point`, a cross-process child hosted by that window (a WebView2
surface) belongs to it.
Verify the outcome with `wait_for_element` or `get_element_state`: the
application decides what a drop means.

**Returns**

```json
{ "success": true, "data": { "fromX": 2162, "fromY": 410, "toX": 1926, "toY": 410, "moves": 15 } }
```

## `manage_window`

Move, resize, maximize, minimize or restore a top-level window of the target
process: the main window by default, or the first window whose title contains
`windowTitle`.

| Name | Type | Description |
| ---- | ---- | ----------- |
| `action` | string | `move`, `resize`, `set_bounds`, `maximize`, `minimize`, or `restore` |
| `x`, `y` | int? | Outer frame position in screen pixels, for `move` and `set_bounds` |
| `width`, `height` | int? | Outer frame size in pixels, for `resize` and `set_bounds` |
| `windowTitle` | string? | Act on this window instead of the main window |

Moving or resizing a maximized or minimized window restores it first.
`restore` always returns to normal bounds, even from a minimize that followed
a maximize. Requests are posted asynchronously (`ShowWindowAsync`,
`SWP_ASYNCWINDOWPOS`) so a busy UI thread cannot block the call; the result is
read once the window reached the requested state and its bounds stopped
changing (up to 2 s).

**Returns** (`matchesRequest` is false when the application constrained the
request, for instance with a minimum size)

```json
{
  "success": true,
  "data": {
    "windowHandle": 25827204, "title": "Heimdall - Remote Access Control Center",
    "action": "Resize", "state": "normal",
    "x": 1573, "y": 308, "width": 900, "height": 650, "matchesRequest": true
  }
}
```

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
| `condition`      | string  | -       | `exists`, `not_exists`, `enabled`, `visible`, `value_equals`, `value_contains`, `stable_for`, `name_equals`, `name_contains`, `name_matches`, or `child_count` |
| `expectedValue`  | string? | null    | Required for `value_*` and `name_*`; a regular expression for `name_matches` |
| `expectedCount`  | int?    | null    | Required for `child_count`                                    |
| `childControlType` | string? | null  | For `child_count`, count only direct children of this control type |
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

`name_equals` (ordinal), `name_contains` (case-insensitive) and
`name_matches` (regular expression) test the element's Name, for labels and
rows whose text the application updates. `child_count` passes when the element
has exactly `expectedCount` direct children, optionally only those of
`childControlType`; the outcome reports the last `childCount`. For example,
wait until the Tab named Sessions holds three tabs instead of sleeping:

```json
{ "name": "Sessions", "controlType": "Tab", "condition": "child_count",
  "expectedCount": 3, "childControlType": "TabItem", "timeoutMs": 5000 }
```

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

## `assert_element`

Assert a condition on a located element and get a clear pass/fail — an immediate
check (no polling, unlike `wait_for_element`). The same operators are available
as an `assert` step in [`run_steps`](#run_steps).

**Parameters** — the shared locator parameters, plus:

| Name       | Type    | Required | Description                                              |
| ---------- | ------- | -------- | -------------------------------------------------------- |
| `operator` | string  | yes      | The assertion operator (below)                           |
| `expected` | string? | for text/numeric operators | The expected operand                   |

**Operators** — text operators compare the element's Value, falling back to its
Name (so both inputs and labels work):

| Operator | Holds when |
| -------- | ---------- |
| `equals` / `not_equals` | the text equals / does not equal `expected` |
| `contains` / `not_contains` | the text contains / does not contain `expected` (case-insensitive) |
| `greater_than` / `less_than` | the text parses as a number and is greater / less than `expected` |
| `enabled` / `disabled` | the element is enabled / disabled |
| `visible` / `hidden` | the element is on-screen / off-screen |
| `checked` / `unchecked` | the toggle state is On / Off |
| `selected` | the element is selected |
| `exists` | the element was located (any match) |

**Returns** — `OperationResult`: `success: true` with `data.assertion` (the
detail) when it holds; `success: false` with an explanatory `error`
(expected vs actual) when it fails or the element is not found.

```json
{ "success": false, "error": "Assertion failed: expected value to equal \"Count: 999\" but was \"Count: 1\"" }
```

## `run_steps`

Run a sequence of UI steps in a single call, instead of one round trip per
action. A whole scenario — drive, synchronize, assert, capture — becomes one
tool call that returns a per-step journal. Each step dispatches to the same
primitive the matching single-action tool uses, so semantics are identical.

**Parameters**

| Name          | Type        | Required | Description                                                      |
| ------------- | ----------- | -------- | ---------------------------------------------------------------- |
| `steps`       | `Step[]`    | yes      | Ordered steps to execute                                         |
| `processId`   | int?        | no       | Default process id for steps that omit their own                 |
| `stopOnError` | bool        | no       | Stop at the first failing step (default true)                    |
| `captureExceptions` | bool  | no       | Attach the exceptions the target process threw during the batch (causal oracle). Requires the batch-level `processId`. |
| `maxDurationMs` | int?      | no       | Total budget for the batch. When elapsed time reaches it before a step, the run stops and `timedOut` is true. |
| `dryRun`      | bool        | no       | Validate and journal mutating steps without clicking, typing, selecting, hovering, or writing screenshots. Also enabled globally by `WINSIGHT_DRY_RUN`. |
| `traceRun`    | bool        | no       | Write a replay JSON artifact even when global tracing is off. |
| `stopOnForegroundChange` | bool | no    | Stop before a screen-changing step when the foreground moved to a window outside the batch (default true). |

**Step shape** — a flat object with an `action` discriminator plus that action's
fields. The shared locator fields (`automationId`, `name`, `className`,
`controlType`, `nameContains`, `index`, `descendantText`, `windowTitle`) work
exactly like [`click_element`](#click_element). Each step may set its own
`processId` (else the batch default applies) and an optional `label` echoed back
in the journal.

| `action`      | Maps to              | Step-specific fields                                            |
| ------------- | -------------------- | -------------------------------------------------------------- |
| `click`       | `click_element`      | `clickStyle`                                                   |
| `click_point` | `click_point`        | `x`, `y`, `clickStyle`, `coordinateSpace`, `allowOutOfWindow`, `focusWindow`, `settleMs` |
| `type`        | `type_text`          | `text`, `focusWindow`                                          |
| `scroll_point` | `scroll_point`     | `x`, `y`, `delta`, `horizontal`, and the `click_point` fields except `clickStyle` |
| `hover_point` | `hover_point`        | `x`, `y`, `holdMs`, and the `click_point` fields except `clickStyle` |
| `type_keys`   | `type_keys`          | `text`, `mode` (`keys` or `paste`), `restoreDelayMs`, `windowTitle` |
| `key_chord`   | `send_key_chord`     | `chord`                                                        |
| `select`      | `select_item`        | `itemName`                                                    |
| `hover`       | `hover_element`      | `expectedTooltipText`, `timeoutMs`                             |
| `wait`        | `wait_for_element`   | `condition`, `expectedValue`, `timeoutMs`, `pollIntervalMs`, `stableForMs` |
| `wait_visual_change` | `wait_for_visual_change` | `region`, `threshold`, `localThreshold`, `timeoutMs`, `pollIntervalMs` |
| `wait_visual_stable` | `wait_for_visual_stable` | `region`, `threshold`, `localThreshold`, `stableComparisons`, `timeoutMs`, `pollIntervalMs` |
| `assert`      | `assert_element`     | `operator`, `expectedValue`: immediate pass/fail; a failing assert fails the step. Operator `text_visible` reads `expectedValue` with OCR inside `region` |
| `get_state`   | `get_element_state`  | (locator only)                                                |
| `screenshot`  | `capture_screenshot` | `outputPath` (a timestamped default is used when omitted)      |

A `wait_visual_change` step that follows a screen-changing step compares
against a capture the runner takes just before that step, so a change faster
than one round trip is not missed. `region` is `{ "x", "y", "width", "height" }`
in window coordinates.

`launch_app`/`close_app` are intentionally not step actions; call them around
the batch (the process id is needed to scope the steps). A `screenshot` step
records the capture's path in its journal `data` (the batch journal is JSON, so
unlike the standalone `capture_screenshot` tool it does not inline the image).

**Returns** — `OperationResult`-style failure on bad input, otherwise a
`BatchResult`:

```json
{
  "success": true,
  "completed": 6,
  "total": 6,
  "failedAtIndex": null,
  "steps": [
    { "index": 0, "action": "click", "label": "increment-1", "success": true, "error": null, "data": null },
    { "index": 5, "action": "wait", "label": "assert-count", "success": true, "error": null,
      "data": { "condition": "Exists", "conditionMet": true, "elapsedMs": 41, "matchCount": 1, "state": { "...": "..." } } }
  ]
}
```

With `stopOnError: true` (default), the run halts at the first failing step;
`failedAtIndex` is that step's index, `success` is false, and steps after it are
absent from `steps`. With `stopOnError: false`, every step runs and
`failedAtIndex` reports the first failure. `completed` counts successful steps;
`total` is the number submitted. When `maxDurationMs` is set and the budget is
hit before all steps run, the result has `timedOut: true` and `success: false`.
When `dryRun` is true, mutating actions report `data.dryRun=true` and do not
touch the target UI or write screenshots. Read-only steps such as `wait`,
`assert`, and `get_state` still run.

**Shared desktop** - before each step that sends input (`click`, `click_point`,
`type`, `key_chord`, `select`, `hover`), the runner compares the foreground
window with the one left by the previous such step. If it moved to a window
whose process is not targeted by the batch, the run stops there whatever
`stopOnError` says: the step is journaled with a `foreground_changed` error
naming both windows (handles and process names, no titles), and the result has
`foregroundChanged: true`. A dialog the target opens, no foreground during a
transition, and the target closing the baseline window are tolerated. Set
`stopOnForegroundChange: false` to disable the check. With
`WINSIGHT_YIELD_TO_USER_MS` set, the same steps fail with `user_active` while a
person used the desktop more recently than that.

**Replay artifacts** — set `traceRun: true`, `WINSIGHT_TRACE=1`, or
`WINSIGHT_TRACE_DIR` to write a replay JSON file. `BatchResult.tracePath` points
to it. Use `replay_steps` to inspect or replay it.

**Causal exception oracle** — with `captureExceptions: true`, an EventPipe
session runs around the whole batch and the result gains an `exceptions` array
listing the exceptions the target process threw *during* the steps (caught ones
included — the runtime emits them too), turning a time-windowed oracle into a
causal one. Each entry carries `ExceptionType` and `ExceptionMessage`. An empty
array means none were observed; the field is absent when capture was not
requested. This needs the batch-level `processId` (the session targets one
managed process) and adds a short settle/drain overhead.

```json
{
  "success": true, "completed": 1, "total": 1, "failedAtIndex": null,
  "steps": [ { "index": 0, "action": "click", "success": true } ],
  "exceptions": [
    {
      "providerName": "Microsoft-Windows-DotNETRuntime",
      "eventName": "ExceptionThrown_V1",
      "payload": { "ExceptionType": "System.InvalidOperationException", "ExceptionMessage": "..." }
    }
  ]
}
```

## `replay_steps`

Replay a `run_steps` artifact. It is a dry run by default so an agent can
inspect the recorded actions without repeating clicks or keystrokes.

| Name | Type | Default | Description |
| ---- | ---- | ------- | ----------- |
| `tracePath` | string | — | Path returned by `run_steps.tracePath` |
| `processId` | int? | null | Optional process id override; otherwise uses the trace default, then the session target |
| `dryRun` | bool | true | Keep true for inspection; set false to execute recorded mutating steps |

The return shape is the same `BatchResult` as `run_steps` and writes a new replay
artifact for the replay attempt.

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

Capture a PNG of the target process's topmost visible window that is not a
transient overlay (an open tooltip or menu is skipped, see `inspect_ui_tree`),
of its main window when `mainWindow=true`, or of the first visible window whose
title contains `windowTitle`, using `PrintWindow` with `PW_RENDERFULLCONTENT`. This flag makes DirectX-composed WPF windows and
other accelerated surfaces render correctly into the bitmap; without it the
result is often a black rectangle.

**Parameters**

| Name          | Type   | Required | Description                                            |
| ------------- | ------ | -------- | ------------------------------------------------------ |
| `processId`   | int    | yes      | Target process id                                      |
| `outputPath`  | string | no       | Absolute file path. Default: a timestamped `screenshot-{utc-timestamp}.png` under `WINSIGHT_OUTPUT_DIR` (default `%TEMP%\winsight`) |
| `windowTitle` | string | no       | Case-insensitive substring of the target window title. Use this to capture modal dialogs, popups, or secondary windows. |
| `includeImage` | bool  | no       | Attach the captured PNG as an inline image (default true). Set false to return only the JSON path. |
| `mainWindow`  | bool   | no       | Capture the process main window even when a dialog or overlay is on top of it; ignored with `windowTitle` |

> **Output directory** — when `outputPath` is omitted, the capture is written
> under the directory named by the `WINSIGHT_OUTPUT_DIR` environment variable,
> falling back to `%TEMP%\winsight`. Set it to redirect captures into a folder a
> supervisor agent can read (e.g. a shared mount). The directory is created on
> first write.

**Returns** — MCP content with two blocks: a **text** block carrying the
`OperationResult` JSON wrapping a `ScreenshotResult`, and (unless
`includeImage=false`) an **image** block with the captured PNG inline, so a
supervising agent sees the UI directly without reading the file. The text block:

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

## `type_keys`

Type text into the focused control of the target's window when there is no
UI Automation element to type into (iced, winit, egui). Use `type_text` when
there is one.

| Name | Type | Required | Description |
| ---- | ---- | -------- | ----------- |
| `text` | string | yes | Up to 4096 characters |
| `mode` | string? | no | `keys` (default) or `paste` |
| `windowTitle` | string? | no | Restrict the window by title |
| `restoreDelayMs` | int? | no | Paste mode: time the text stays on the clipboard before the restore, 0 to 5000, default 300 |
| `allowWebView2Input` | bool | no | Send even when focus is inside WebView2. Default false |
| `processId` | int? | no | Target process |

The window is raised and its focus verified as for `send_key_chord`. In `keys`
mode every character is a `KEYEVENTF_UNICODE` keystroke (the application
receives `WM_CHAR`, whatever the keyboard layout), line breaks are Enter and
tabs are Tab. Before each keystroke the window must still own the foreground;
otherwise typing stops with `foreground_changed`, so the rest never lands in a
window someone else just clicked. `paste` mode needs `WINSIGHT_ALLOW_CLIPBOARD`:
it saves every clipboard format held in a memory block, puts the text on the
clipboard (out of history and the cloud clipboard), sends Ctrl+V, waits
`restoreDelayMs`, and puts the saved formats back. `restored` is `full`,
`partial` (bitmap, metafile or private-handle formats could not be saved) or
`failed`.

**Returns** - `{ mode, length, windowHandle, windowTitle, focusedElement, restored, skippedFormats }`.

## `set_clipboard_text` / `read_clipboard_text`

Write or read the clipboard text: prepare a paste, or check what a Copy button
copied. Both refuse with `clipboard_disabled` unless `WINSIGHT_ALLOW_CLIPBOARD`
is set, because the clipboard often holds passwords and tokens. Text written by
Winsight carries the formats that keep it out of the clipboard history, the
cloud clipboard and clipboard monitors. Traces record the size of the result,
never the text. `read_clipboard_text` returns `{ text, length }`, with `text`
null when the clipboard holds no text; `set_clipboard_text` takes `text` and
returns `{ length }`.

## `wait_for_visual_change` / `wait_for_visual_stable`

Synchronize on what a window shows instead of sleeping, for UIs with no UI
Automation tree to wait on.

| Name | Type | Required | Description |
| ---- | ---- | -------- | ----------- |
| `region` | object? | no | `{ x, y, width, height }` in window coordinates. Default: the whole window |
| `timeoutMs` | int | no | 0 to 60000, default 5000 |
| `pollMs` | int | no | Time between captures, 20 to 5000, default 100 |
| `threshold` / `localThreshold` | double? | no | SSIM and worst-block SSIM limits; default the `WINSIGHT_VISUAL_*` settings |
| `stableComparisons` | int | no | `wait_for_visual_stable` only: unchanged comparisons in a row, 1 to 50, default 3 |
| `baselinePath` | string? | no | `wait_for_visual_change` only: a PNG to compare against instead of a capture taken now |
| `windowTitle`, `processId` | | no | Window selection, as for `capture_screenshot` |

Two captures differ when global SSIM, worst 16x16 block SSIM or the pixel ratio
crosses the `compare_visual_baseline` thresholds, so one changed status word
counts. Captures stay in memory and the window is never touched. To wait for the
effect of an action, put a `run_steps` `wait_visual_change` step right after it:
the runner captures the reference before the action.

**Returns** - `{ condition, met, elapsedMs, frames, ssim, localSsimMin, pixelDiffRatio, region }`;
on timeout the call fails with `timed_out` and the same data.

## `read_text_visually`

Read the text a window draws with the OCR engine built into Windows. Offline:
nothing leaves the machine and nothing needs installing beyond an OCR language
(Settings, Time and language, Language and region).

| Name | Type | Required | Description |
| ---- | ---- | -------- | ----------- |
| `region` | object? | no | `{ x, y, width, height }` in window coordinates |
| `language` | string? | no | BCP-47 tag such as `en-US`; default the user profile languages |
| `scale` | int | no | Enlargement before recognition, 1 to 4, default 2 |
| `windowTitle`, `processId` | | no | Window selection |

**Returns** - `{ language, text, lines: [{ text, bounds, words: [{ text, bounds }] }], region, scale }`.
Every `bounds` is in window coordinates, the same space as window-relative
`click_point`. Without a matching OCR language the call fails with
`ocr_unavailable` and lists the installed ones. Inside a batch, assert with
`{ "action": "assert", "operator": "text_visible", "expectedValue": "...", "region": {...} }`:
the comparison ignores case and collapses spacing.

## `compare_visual_baseline`

Capture the target process window **client area** (no chrome, for determinism)
and compare it to an approved PNG baseline. The comparison is baseline-based and
deterministic: SSIM (Gaussian 11x11, sigma 1.5, grayscale) as a global mean and
a worst-block local minimum, plus a pixelmatch-style YIQ pixel-diff ratio with
anti-aliased-edge detection. No ML model, GPU, or Python runtime is used.

With `WINSIGHT_DRY_RUN=1`, this tool returns a preview without capturing, pruning
artifacts, creating a baseline, or replacing an existing baseline, including
when `updateBaseline=true`.

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
| `includeImage`        | bool     | no       | Attach the diff heatmap PNG as an inline image when a comparison runs (default true) |

**Configuration**

| Variable                          | Default                                      | Purpose                 |
| --------------------------------- | -------------------------------------------- | ----------------------- |
| `WINSIGHT_VISUAL_BASELINE_DIR`    | `%LOCALAPPDATA%\winsight\visual-baselines`   | Approved baseline store |
| `WINSIGHT_VISUAL_SSIM_THRESHOLD`  | `0.98`                                       | Minimum passing global SSIM |
| `WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD` | `0.85`                                  | Minimum passing worst-block SSIM |
| `WINSIGHT_VISUAL_PIXEL_THRESHOLD` | `0.005`                                      | Maximum passing diff ratio |
| `WINSIGHT_VISUAL_COLOR_THRESHOLD` | `0.1`                                        | pixelmatch color sensitivity (0..1) |
| `WINSIGHT_OUTPUT_DIR`             | `%TEMP%\winsight`                            | Candidate captures and heatmaps |
| `WINSIGHT_TRACE`                  | unset                                       | `1`/`true` enables JSONL tool traces and run replay artifacts |
| `WINSIGHT_TRACE_DIR`              | `%TEMP%\winsight\traces` when tracing is enabled | Trace and replay artifact directory |

Baselines live outside the repository by default. `baselineKey` must be a
relative `.png` path and cannot escape the configured baseline directory.

**Returns** — MCP content: a **text** block with the `OperationResult` JSON
wrapping a `VisualDiffResult`, plus (when a comparison ran and produced a diff
map, unless `includeImage=false`) an **image** block with the diff heatmap
inline, so you can see where the UI changed without opening the file. The text
block:

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

| Name            | Type | Default | Description                                  |
| --------------- | ---- | ------- | -------------------------------------------- |
| `port`          | int  | —       | CDP debugging port (`remoteDebuggingPort`).   |
| `hostProcessId` | int? | null    | Host process that owns the WebView2 endpoint. Falls back to `set_target`. |

The port must match a listening `remoteDebuggingPort` reported by
`detect_webview2_endpoints` for the authorized host process.

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
| `hostProcessId` | int? | null    | Host process that owns the WebView2 endpoint. Falls back to `set_target`. |

The port must match a listening WebView2 endpoint owned by the authorized host
process; raw loopback CDP ports are rejected.

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
| `hostProcessId` | int?    | null    | Host process that owns the WebView2 endpoint. Falls back to `set_target`. |

The port must match a listening WebView2 endpoint owned by the authorized host
process; raw loopback CDP ports are rejected.

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

**Returns** — `OperationResult` whose `data` is the `GroundedElement`:

```json
{
  "success": true,
  "data": {
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
both the grounding and the click outcome. The click is refused when the returned
confidence is below `WINSIGHT_VISION_MIN_CONFIDENCE` (default `0.5`); a
locate-only call still returns the low-confidence grounding for inspection.

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
| `WINSIGHT_VISION_TIMEOUT_MS`| `60000`                                  | Per-request timeout for the Anthropic backend (bounded; transient 429/5xx are retried) |
| `WINSIGHT_VISION_MIN_CONFIDENCE` | `0.5`                              | Minimum model confidence required before `act` clicks the grounded point |
| `WINSIGHT_VISION_ONNX_PATH` | —                                        | Local Qwen2.5-VL ONNX model directory |
| `WINSIGHT_VISION_EP`        | `cpu`                                    | `cpu` only in this build |
| `WINSIGHT_OUTPUT_DIR`       | `%TEMP%\winsight`                        | Directory for the intermediate `vision-{timestamp}.png` capture |

**Privacy** — screenshots are transmitted to the configured API when using
`WINSIGHT_VISION_BACKEND=anthropic`. `WINSIGHT_VISION_BACKEND=onnx` runs a local
Qwen2.5-VL model fully on-host and never falls back to Anthropic; see
[docs/local-onnx-grounder.md](local-onnx-grounder.md) for provisioning.
