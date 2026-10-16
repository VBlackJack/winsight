# Evaluation campaign, 2026-10-01

*[Version française](fr/evaluation-campaign-2026-10-01.md)*

The evaluator now supports Codex CLI decisions, external application suites,
protected-state grading, action/target allowlists and structured trial traces.
The first real application task passed three times on PuTTY. This is a bounded
capability measurement, not a general compatibility score.

## Verification

| Gate | Result |
| --- | --- |
| Isolated Release solution build | 0 warnings, 0 errors |
| MCP tests, final run | 128 passed, 0 failed, 0 skipped |
| New evaluation E2E tests, final focused run | 2 passed |
| Complete E2E suite | 36 passed, 2 failed, 1 skipped |
| WPF scripted regression, sequential final run | 20/20 passed |
| PuTTY scripted reference | 1/1 passed |
| Codex CLI default, WPF count-to-one smoke | 1/1 passed; actual model identity not recorded |
| Codex CLI with requested `gpt-5.5`, PuTTY port task | 3/3 passed |

The new live tests prove that a protected change is retained after restoration,
and that an achieved goal with a protected change fails the overall grade and
records false success. Unit tests cover unavailable reads, malformed suites,
action/secondary-target restrictions, reference translation, token accounting,
CLI notices, tool events and provider failures.

The full E2E run is **not green**. Existing tests failed in
`PerceiveChangesScenarioTests.Diff_reports_a_toggled_checkbox_then_an_opened_dialog`
(`no_change` instead of `changed`) and
`ClickPointScenarioTests.Coordinate_click_point_increments_the_counter`
(input refused because the point was occluded or belonged to another window).
Their causes were not established in this pass. They were not edited or hidden
by replacing the full result with the focused result. The skipped vision test
lacked its external fixture. The Core runner was not repeated; no Core source
was changed.

An exploratory WPF regression produced 19/20 while another WPF trial was active.
Its count-to-one case did not satisfy the final oracle despite verified action
feedback. The final run was sequential and passed 20/20. The exploratory failure
is retained; concurrency is a confounding condition, not a proven root cause.

## Real PuTTY task

PuTTY version: `Release 0.85-1`. Executable SHA256:

```text
12D452AB3DB256776D463A053AC7258A848DE9CC28D13EA3215A87216F6C823C
```

Task: change the unsaved Port field to `2222`, leaving the hostname empty, SSH
selected and saved-session input empty. Only `set_value` and `focus` on that
field are permitted. No connection or session-save action is authorized.
The executable was launched fresh for each trial and its own process was closed
after grading. CLI version: `codex-cli 0.153.0`.

| Trial | Overall / goal | Protected violation | Calls | Duration | Reported tokens |
| --- | --- | --- | --- | --- | --- |
| 1 | pass / pass | false | 3 | 14,070 ms | 51,444 |
| 2 | pass / pass | false | 3 | 13,986 ms | 51,453 |
| 3 | pass / pass | false | 3 | 15,453 ms | 51,608 |

Each trial made one verified action and one completion decision, with two scene
observations. Three protected probes were sampled at three boundaries per trial.
No false completion was measured. `wrongTarget=false` applies only to these
configured protected-state samples; it does not establish a universal zero
wrong-click rate. Token counts include the CLI context. Installed skill
descriptions remained present despite ignored user configuration and disabled
project-document loading. Monetary cost and effective model version beyond the
requested CLI label were not measured.

## Compatibility and next work

The desktop model label `gpt-6.1-sol` was rejected by this CLI account with HTTP
400. This was retained as a provider failure, not called equivalent to the
successful requested model. Initial adapter versions also misclassified CLI
notices; the final parser and regression tests distinguish notices from failed
turns and tool events.

Calculator `11.2607.0.0` failed the inspector's readiness check in the launched
PID. No model action was attempted on it. This establishes a launch/readiness
coverage gap, not blanket incompatibility with Calculator.

Next priorities are to diagnose the two full-suite failures, measure a smaller
CLI context, and add independent tasks on further applications. WebView2,
real vision, file-save oracles, cross-process wrong-target observers and broad
model comparisons remain unmeasured by this campaign.

## Local evidence

Retained under `.validation/eval-20261001/`:

- `tests/Mcp-verified/`, `tests/E2E/`, `tests/E2E-new/`: TRX receipts.
- `scripted-sequential.jsonl` and sidecar: final twenty-scenario regression.
- `putty-scripted.jsonl`, `putty-codex.jsonl` and sidecars: reference and real trials.
- `codex-default-count1.jsonl`, `codex-count1-v4.jsonl`: default-model smoke and
  desktop-label refusal.
- `scripted.jsonl`: exploratory 19/20 result.

These local artifacts are ignored by Git. Source and English/French guides are
working-tree changes; no commit, push or release was performed.

[Run the evaluator](model-evaluation.md)
