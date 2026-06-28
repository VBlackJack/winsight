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
