# Verified control validation

Local validation on Windows, 2026-09-05. These results cover the native fixture
and deterministic contracts; they do not represent model-comparison scores.

| Gate | Evidence |
| --- | --- |
| Release build | Solution builds with zero warnings and errors |
| Core | 189 passed, 2 skipped (external vision fixtures) |
| MCP | 111 passed |
| Complete E2E | 35 passed, 1 skipped (external vision fixture) |
| Additional managed boolean E2E | 1 passed; lowercase wire value verified against a unique managed instance |
| Final verified-scene E2E | 5 passed, including two additional profile/capture scenarios |
| MCP stdio | Initialize and tools/list succeed; 48 tools, including all 7 new tools |
| Scripted evaluation | 20/20 scenarios passed after fixing owned-dialog duplication |

The five final scene scenarios cover: a verified increment; an intentionally
unconfirmed increment; refusal to repeat a consumed observation; dry run; changed
toggle state; annotated PNG output; changed-pixel vision refusal using a fake
grounder; profile preview, execution and executable-hash invalidation. Assertions
span several checks per scenario; they are not counted as separate tests.

The cross-process lease test starts a separate PowerShell process that owns the
named mutex, verifies refusal in the test process, terminates only that child,
and verifies recovery of abandoned ownership.

An initial grouped run encountered one failure in the pre-existing
`VisualDifferTests.Dimension_mismatch_fails_explicitly_and_writes_diff_map_when_requested`:
`System.Drawing.Image.Save` reported a null encoder. A subsequent complete Core
run passed. This transient failure was not diagnosed or hidden by retry logic;
retain it as a follow-up if it recurs in CI.

## Not measured

- Real Codex, Claude or Gemini decisions, token costs or client-specific success rates.
- Live WebView2 DOM dispatch/verification. Endpoint ownership refusal is tested.
- Real cloud/ONNX vision quality; the three required external fixtures remain skipped.
- Independent OCR, iframe/shadow DOM coverage or operation inside protected desktops.
- Broad drag/multi-selection/virtualization compatibility across third-party applications.
- Wrong-target action rate: evaluation emits null, not a fabricated zero.

The scripted runner is a regression baseline. Its final oracle is independent of
the decision source, but it knows all twenty scenarios and does not establish
generalization. CI runs the same baseline and retains JSONL results with test artifacts.
