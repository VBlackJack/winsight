# Model evaluation

Use this workflow to measure decisions and observable outcomes separately. The
runner supports the built-in twenty-scenario WPF regression suite, external JSON
suites, trusted executable adapters, and a built-in Codex CLI decision source.

## First run

Build the solution in Release, then validate the reference recipe before making
model calls:

```powershell
dotnet build Winsight.slnx -c Release
$runner = '.\tools\Winsight.Eval\bin\Release\net10.0-windows\Winsight.Eval.exe'
& $runner 'C:\Program Files\PuTTY\putty.exe' scripted '.\TestResults\putty-reference.jsonl' 1 '.\tools\Winsight.Eval\suites\putty-port.json'
```

Create the output directory first. Both the results file and its
`.trace.jsonl` companion must be new. Use disposable application data and run
desktop trials sequentially. Each trial launches and terminates only its own
process; a window belonging to an existing instance is never adopted. An
application that restores previous sessions or forwards to another process
requires a dedicated disposable environment before testing.

The PuTTY suite changes only the unsaved Port field. Its exact AutomationId
allowlist permits `set_value` and `focus` on that field, rejecting connection,
session-save and keyboard actions before Winsight dispatch. The grader checks
the final port independently and protects the empty hostname, SSH selection
and empty saved-session input. These IDs and clean-state expectations are
application-version dependent. A baseline mismatch fails before model calls.

## Codex trials

Set `WINSIGHT_CODEX_EXECUTABLE` to the native `codex.exe`, rather than a `.ps1`
or `.cmd` wrapper, and authenticate the CLI normally. No credential is copied
into a suite or results file. `WINSIGHT_CODEX_MODEL` optionally pins a model
accepted by that CLI account; a desktop model label may not be accepted by the
CLI. Without it, the CLI selects its default and `requestedModel` is null.

```powershell
$env:WINSIGHT_CODEX_EXECUTABLE = 'C:\path\to\codex.exe'
$env:WINSIGHT_CODEX_MODEL = 'your-supported-model'
& $runner 'C:\Program Files\PuTTY\putty.exe' codex '.\TestResults\putty-codex.jsonl' 3 '.\tools\Winsight.Eval\suites\putty-port.json'
```

The adapter uses `codex exec --json --output-schema` in a fresh temporary
directory, with read-only sandboxing, ignored user configuration/rules, no
persisted session, and `project_doc_max_bytes=0`. The prompt asks for a decision
using only the supplied goal, scene and action history. Any emitted tool item
invalidates the trial; this post-hoc check does not prevent a tool from executing.
Read-only mode and an empty working directory are not a filesystem secrecy
boundary. Use a disposable test account for stronger isolation. Installed skill
descriptions may still be present in CLI context. CLI notices are diagnostic;
failed turns, nonzero exits and missing/malformed decisions fail explicitly.

Each decision is a fresh CLI invocation bounded by 120 seconds. The native
adapter supports single-target activation, value assignment, item selection,
selection, focus, keyboard chords, expansion/collapse and scrolling into view.
It converts observed expectation references to their captured locators; it does
not accept model-authored locator metadata. Future controls not present in the
current scene cannot be used as expectation references. DOM, vision,
multi-selection and drag need a different adapter or a later extension.

These runs measure the in-process Winsight tool loop with CLI model decisions.
They do not measure Codex desktop transport or unrestricted Codex computer use.
See [official non-interactive documentation](https://developers.openai.com/codex/noninteractive)
for CLI structured output.

## Read the evidence

Each result contains:

- `goalOutcome`: independent final UI predicate and required milestones passed.
- `outcome`: goal passed and all protected-state samples were known and unchanged.
- `wrongTarget`: true when protected state differed at a sampled boundary; false
  when all configured samples matched; null when evidence was unavailable.
- `wrongTargetCoverage`: `protected_state_at_observation_boundaries`. This does
  not detect clicks without a state change, unsampled transient changes, or
  changes to unconfigured controls or other processes.
- `falseSuccess`: the source claimed completion despite a known failed grade;
  null when completion was claimed but the grading evidence was incomplete.
- `tokens`: input plus output tokens reported by the source; cached input is
  not added twice. A missing per-decision report makes this null.
  `reportedTokens` retains the known subtotal and `tokenUsageComplete` records
  coverage. Usage lost on a failed decision is not recovered or estimated.
- `calls`, `elapsedMs`, `staleResponses`, protected probe/sample counts,
  `finalEvidence`, executable SHA256, runtime and Winsight version, requested
  Codex model and CLI version.

The sidecar retains scenes, structured decisions and action results, not model
reasoning. It contains UI text and entered values: keep it local and use
non-sensitive fixtures. Missing properties and ambiguous locators never count
as successful oracle reads. A detected protected change stays a violation even
if a later action restores the state. Failure returns exit code 1.

## Add an application

Use `Winsight.Eval --inspect <app.exe> <new-scene.json>` to launch an owned
instance, observe its UI and close it without dispatching an action. Readiness
requires a unique UIA Window in that PID. Failure establishes only that this
launch/readiness path failed, not that the entire application is unsupported.

Copy the PuTTY suite as a starting point. A suite requires `name`, `ready`
(locator), and nonempty `scenarios` with unique IDs. Each scenario declares:

- `id`, `goal`, `oracleTarget`, `property`, `expected`, and `steps` for the
  independently validated reference recipe.
- At least one `preserve` probe, containing `locator`, `property`, `expected`
  and optional `windowTitle`.
- Optional `oracleLocator`, `oracleWindow`, `milestones`, `split`,
  `allowedTargets` (exact AutomationIds) and `allowedActions`.

Oracle properties are `name`, `value`, `toggle`, `selected_item`, `selected`,
`focused` and `visible`. Use exact AutomationId or Name locators; indexed and
partial matching are refused. `milestones` must be observed after an action,
so a pre-existing final state alone cannot satisfy a required transition.
Reference steps use `target` as an AutomationId, or an exact `targetLocator`,
plus optional `windowTitle`, `text` and `item`.

The executable is supplied separately and hashed for the report. There are no
application launch arguments, process adoption, file/database graders or
automatic state reset beyond starting a fresh process. Suites requiring these
features need additional isolation and grading work. Set
`WINSIGHT_EVAL_SCENARIOS` to a comma-separated list of known IDs for a bounded
subset; unknown or empty selections are refused. Trials are limited to 1..20
and decisions to 30 per trial. UIA calls can still outlast polling timeouts.

Keep reference recipes and graders out of model requests. Review the sidecar
when a trial fails. Three successful trials of one task establish only that
observed result; they do not establish broad application compatibility or a
general model ranking.

[First measured campaign](evaluation-campaign-2026-10-01.md) · [Version française](model-evaluation.fr.md)
