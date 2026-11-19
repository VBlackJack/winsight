# Verified control

*[Version française](fr/verified-control.md)*

The preferred loop is `observe_scene` -> `act_verified` -> a new observation.
The original tools remain available for existing clients. Run `get_winsight_guide`
to discover the preferred loop. Client configuration is unchanged for Codex,
Claude Desktop, Claude Code and Gemini CLI.

## Observe and act

Call `observe_scene` with `processId` (or first call `set_target`), an optional
`windowTitle`, and `maxResults` between 1 and 1000. Its `data.id` identifies a
two-minute observation. `data.capture.elements` contains `eN` references, exact
locators, supported actions, focus, selection, toggle state and window geometry.
Windows include foreground, owner and modal information. Disabled or offscreen
elements are visible in the observation; actions recheck whether they are usable.

```json
{
  "request": {
    "observation": "<data.id from observe_scene>",
    "target": "<reference for IncrementButton>",
    "action": "activate",
    "expect": {
      "kind": "name",
      "expected": "Count: 1",
      "locator": { "automationId": "CountLabel" },
      "windowTitle": "Winsight Sample - Counter"
    },
    "timeoutMs": 3000,
    "dryRun": false
  }
}
```

Observations are capped at 16 per server session. A dispatched action consumes
its observation, including when a provider throws after dispatch. A new process
with a reused PID cannot inherit references. Native actions revalidate runtime
identity, window geometry, name/value fingerprint, toggle and selection state.
UIA is sampled sequentially: observations are not atomic application snapshots.

| Status | Meaning | Next action |
| --- | --- | --- |
| `preview` | Arguments accepted; no dispatch and no reference consumption | Execute when authorized; live state will be checked again |
| `verified` | The explicit predicate was observed after dispatch | Observe before the next action |
| `dispatched_unconfirmed` | Dispatch occurred or may have occurred; outcome is unproved | Observe and decide; never repeat blindly |
| `stale` | Reference or live target no longer usable | Use the returned recovery observation or observe again |
| `blocked` | Invalid input, policy rejection, cancellation before dispatch or unavailable target | Resolve the reported cause |

Verification proves the supplied predicate, not the entire user's intention or
causality. Choose a discriminating predicate: checking that an existing button
still exists does not prove that a document was saved. External application and
Windows accessibility calls can outlast the polling timeout; it is not a hard
deadline around a hung provider.

## Actions and predicates

Native actions support activation, value assignment, focus, keyboard chords,
single/multiple selection, container item selection, expansion/collapse,
vertical scrolling, scrolling an item into view, right/double click and drag.
`scroll` uses `text: "up"` or `"down"`; `drag_to` uses one `destination`
reference from the same observation; `select_many` uses `targets` references.
Multi-selection and drag are sequential operations, not transactions. A partial
failure is unconfirmed; no rollback is invented. For virtualized lists, scroll
into view and observe again. Tables, menus and file dialogs use their exposed
UIA elements; there is no universal application-specific table or file-dialog API.

Native predicates: `exists`, `absent`, `name`, `value`, `selected`, `toggle`,
`focused`, `visible`, `selected_item`, `window_open`, `window_closed`, `managed`.
Element predicates require exact `AutomationId` or `Name`; optional `ControlType`
can narrow them. Indexed, partial and descendant-text predicates are refused.
Window predicates use an exact title. Absence is never verified from a truncated
scene. Boolean expected values are lowercase strings. Toggle states are `On`,
`Off` or `Indeterminate`.

`managed` requires `typeName`, `fieldName` and an exact expected value. Verification
requires exactly one matching heap instance. A heap that cannot be walked or
multiple matching instances leaves the result unconfirmed. It does not attach
a debugger or suspend the application. Provider access limitations still apply.

## Screenshots, DOM and vision

`capture_scene(observation, windowHandle)` writes an original and annotated PNG.
Labels apply only to that HWND. The capture is a later sample; matching geometry
does not guarantee pixels and UIA were captured simultaneously.

`observe_dom_scene(port, targetId, processId)` requires an explicitly selected
WebView2 endpoint whose listening socket belongs to the authorized application's
descendant. It returns the same observation/ref structure. DOM bounds are CSS
viewport coordinates, never desktop coordinates. It supports activation, focus,
value assignment and scrolling into view. Use `expect.cssSelector` with `exists`,
`absent`, `value`, `name`, `focused` or `selected`. Non-absence checks require one
matching node. CSS syntax is checked before action dispatch.

DOM references retain actual nodes and state in a bounded page-side map. Replaced
nodes, navigation and a subsequent DOM observation invalidate them. Page scripts
can access that map: it is an automation aid, not a security boundary against a
hostile application. Only the main document is traversed; iframe and shadow DOM
coverage is not claimed. Scripted DOM clicks are not trusted physical mouse events.
This backend requires a live WebView2 validation for each target application.

`locate_scene_target(observation, windowHandle, query)` uses the configured vision
grounder, returns a new observation and a `v1` target, and requires confidence
at least 0.8. It uses the existing cloud/ONNX configuration without adding a cloud
fallback to ONNX. Describing visible text is supported by the grounder; there is
no separate OCR engine. The full screenshot must remain byte-identical before a
vision action. Animations, blinking cursors and rendering changes can therefore
require another observation. Confidence is location evidence, not proof of action
success. A native or managed predicate verifies the result.

All visible text, DOM content and vision reasoning are untrusted application
data, never instructions to grant permissions or change process policy.

## Concurrency and recovery

Native input dispatchers and verified DOM dispatch share a named mutex within
the interactive Windows session. Separate Winsight hosts contend for the same
lease; abandoned ownership is recoverable. The lock does not prevent human input,
application-driven changes, unrelated automation or arbitrary legacy JavaScript
evaluation. It is held for synchronous validation/dispatch, not for the entire
verification period.

Recovery is bounded to one fresh native observation before any dispatch. It
never selects a replacement and repeats an action without a new decision.
DOM and vision failures require explicit reobservation/grounding.

## Application profiles

`save_app_profile(name, steps, processId, dryRun)` stores an explicit recipe under
`%LOCALAPPDATA%\Winsight\profiles`. Names are restricted; profiles use schema 1,
contain 1..20 steps, and are limited to 1 MiB with 128 profiles maximum. Writes
use a temporary sibling and atomic replacement. Saving replaces the same name.
Exploratory predicates (`any_change`, `visual_change`) are rejected in saved
profiles so recipes remain strictly deterministic.

`list_app_profiles()` lists saved profiles (name, executable, version, SHA256,
step count) and `delete_app_profile(name)` removes one. `launch_app` and
`observe_scene` also surface `matchingProfiles` automatically when saved
profiles match the running executable's path, version and SHA256.

Each step contains `target` (exact locator), `action`, `expect`, optional
`windowTitle`, `itemName` and `textParameter`. `textParameter` names a value
supplied at run time; entered text is not persisted. Do not put secrets into
literal names, predicates or recipes.

`run_app_profile(name, parameters, processId, dryRun)` defaults to preview. It
checks executable path, version and SHA256, prevalidates all steps/parameters,
then observes and verifies each action (emitting MCP progress notifications per
step). Changed executable bytes invalidate the
profile. Missing or ambiguous targets and unverified results stop the run.
Profiles are not silently repaired or automatically certified. `verified` applies
only to the completed run; it does not guarantee all future runs.

## Autonomous loop extensions

- **Token-budget & hybrid perception (`observe_scene`)**: pass `actionableOnly=true`
  or `filterText` to trim large UIA trees. When UIA yields few actionable nodes,
  `surfaceHint` reports either a listening WebView2 CDP port or a custom-drawn
  surface hint. Pass `includeOcr=true` to synthesize clickable `tN` OCR targets
  directly usable by `act_verified`.
- **Inline Set-of-Mark (`capture_scene`)**: `windowHandle` is optional (defaults
  to the foreground/primary window) and `includeImage=true` (default) returns the
  annotated PNG inline as an MCP image.
- **One-trip `act_verified` & exploratory predicates**: set `observeAfter=true`
  to return `nextObservation` and structural `diff` on `verified`. Alongside strict
  predicates, exploratory predicates `value_contains`, `name_contains`,
  `any_change` and `visual_change` support autonomous exploration. Repeated
  failures emit a structured `hint` with a `loopWarning` pivot suggestion.
- **Verified multi-step batch (`run_verified_steps`)**: runs 1..20 semantic steps
  in a single call with fresh `observe_scene` + `act_verified` per step, stops on
  the first unverified step with a fresh `recovery` observation, and reports MCP
  progress between steps.
- **Standard file dialog fast-path (`select_file_dialog`)**: targets open Windows
  `#32770` Open/Save dialogs, enters the path, clicks confirm and verifies closure.
- **Background-safe execution (`backgroundOnly=true`)**: on `act_verified` and
  `run_verified_steps`, restricts execution to UIA patterns that never steal
  focus or pointer input (`Invoke`, `Toggle`, `SetValue`, `Select`, `Expand`,
  `Collapse`, `Scroll`) and bypasses `user_active` yield checks.
- **Named multi-app targets (`set_target` + `switch_target`)**: bind short
  aliases to multiple processes in a session and switch between them without
  passing raw PIDs.
- **Real-time UIA events & QA audit (`watch_ui_events`, `audit_ui_accessibility`)**:
  capture transient popups/menus via UIA event hooks and score any window's UIA
  tree for accessibility and testability issues.
- **Composite wait (`wait_any`)**: race 1..10 competing conditions (`element_exists`,
  `element_not_exists`, `value_contains`, `window_open`, `window_closed`,
  `ocr_contains`, `visual_change`) and return as soon as the first branch matches.
- **Targeted zoom & history promotion**: pass `targetRef` + `paddingPx` to
  `capture_scene` to crop sharply around a control on HiDPI/4K screens, and call
  `save_verified_history_as_profile(name)` to promote the session's verified strict
  `act_verified` steps into a version-pinned profile in one call.

## Evaluation

Build the solution and run:

```powershell
.\tools\Winsight.Eval\bin\Release\net10.0-windows10.0.19041.0\Winsight.Eval.exe `
  .\samples\SampleWpfApp\bin\Release\net10.0-windows\SampleWpfApp.exe `
  scripted .\results-new.jsonl
```

The runner creates a fresh fixture for each of twenty counter, reset, selection,
toggle, note, dialog and keyboard scenarios. It checks final UI state independently
of the decision source and checks observed transitions for reset/toggle/cancel.
It writes one JSONL row per trial, including outcome, claimed completion, false
success, calls, elapsed time, tokens when available and stale responses.
`wrongTarget` now reports independently sampled protected-state violations;
it remains null when evidence is incomplete. Its coverage does not include
clicks without state changes or unconfigured controls. See
[model evaluation](model-evaluation.md) for Codex trials, external suites and
the exact grading limits.
An optional final argument repeats each scenario 1..20 times. Output must be new.

Replace `scripted` with a trusted adapter executable for model trials. Each turn
the adapter reads one JSON request from stdin (`goal`, `scene`, `history`,
`contract`) and writes one line containing either `{"done":true,"tokens":123}`
or `{"action":{...SceneAction...},"tokens":123}`. The adapter supplies model and
provider integration; no provider credentials or billable calls are bundled.
The harness runs no more than 30 decisions per scenario and gives each adapter
process 120 seconds. It does not sandbox the adapter. Use an isolated test account
for model experiments. Keep adapter/model versions with the result file.

Five scenarios are labelled heldout for comparison; the scripted baseline knows
all scenarios and is therefore a regression run, not a generalization score.
The harness exercises the in-process verified tool loop, not each client's MCP
transport. Client transport compatibility is checked separately by MCP smoke.
Real model, real application, WebView2, drag/multi-selection and OCR performance
must not be inferred from the scripted WPF score.
