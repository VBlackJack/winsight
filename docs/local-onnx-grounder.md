# Local ONNX vision grounder

*[Version française](fr/local-onnx-grounder.md)*

`WINSIGHT_VISION_BACKEND=onnx` selects a fully local `IElementGrounder` that runs
**Qwen2.5-VL-3B** entirely on-host: nothing leaves the machine, which is what
makes it usable on regulated or sensitive content. It never falls back to the
cloud: if it is selected but misconfigured, `find_element_visually` fails
explicitly.

## How it works

The model is consumed as a transformers.js-style 3-ONNX split
(`vision_encoder` + `embed_tokens` + `decoder_model_merged`, fp16) run on plain
`Microsoft.ML.OnnxRuntime` with a hand-rolled mRoPE generation loop:

- `QwenImagePreprocessing`: smart-resize (28px grid) + CLIP normalization +
  Qwen patchify into the `pixel_values` tensor (`System.Drawing`).
- `QwenVisionTokenizer`: Qwen byte-level BPE via `CodeGenTokenizer` over a vocab
  patched with the Qwen special tokens; builds the chat prompt.
- `OnnxLocalGrounder`: runs vision → embed → decode, parses the model's
  `[x, y]` / `[x1, y1, x2, y2]` output (`GroundingCoordinateParser`), and
  denormalizes the resized-space center to original-image pixels.

## Configuration

| Variable                    | Default | Notes                                            |
| --------------------------- | ------- | ------------------------------------------------ |
| `WINSIGHT_VISION_BACKEND`   | `anthropic` | Set to `onnx` for the local backend.         |
| `WINSIGHT_VISION_ONNX_PATH` | -       | Directory holding the provisioned model (below).  |
| `WINSIGHT_VISION_EP`        | `cpu`   | `cpu` only in this build.                         |

## Provisioning the model

Weights are never committed. Run the provisioning script to download the model
(~6 GB) into a target directory, then point `WINSIGHT_VISION_ONNX_PATH` at it:

```powershell
pwsh tools/provision-vision-model.ps1 -Destination C:\models\qwen2.5-vl-3b-onnx
$env:WINSIGHT_VISION_BACKEND = "onnx"
$env:WINSIGHT_VISION_ONNX_PATH = "C:\models\qwen2.5-vl-3b-onnx"
```

The directory must contain `onnx\*_fp16.onnx` (+ `.onnx_data*` parts), plus
`vocab.json` and `merges.txt` (the BPE tokenizer files the runtime needs).

## Known limitations (follow-ups)

- **Execution provider**: CPU only. The decoder's fused `MultiHeadAttention`
  op currently fails on the DirectML EP, so `WINSIGHT_VISION_EP=dml` is rejected
  until GPU acceleration is implemented. CPU latency is on the order of ~15 s
  per call.
- **Resize fidelity**: `System.Drawing`'s bicubic resampler differs from the
  reference (PIL) more than an ideal match, which can shift the grounded point by
  a few tens of pixels. It still lands on the target element on tested UIs; a
  PIL-matching resize is a follow-up for dense layouts.
- **Model quality**: v1 ships the base Qwen2.5-VL-3B decoder. A UI-grounding
  fine-tune (e.g. ZonUI-3B, whose vision tower is identical to the base) can be
  swapped in by replacing the decoder ONNX.
