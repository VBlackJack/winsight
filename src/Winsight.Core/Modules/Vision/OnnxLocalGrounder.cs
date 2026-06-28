// Copyright 2026 Julien Bombled
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Runtime.Versioning;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.Vision;

/// <summary>
/// Fully local <see cref="IElementGrounder"/> backed by Qwen2.5-VL-3B exported as a
/// transformers.js-style 3-ONNX split (vision encoder + token embedding + decoder),
/// run on plain ONNX Runtime with a hand-rolled mRoPE generation loop. Nothing leaves
/// the host; when misconfigured it fails explicitly and never falls back to the cloud.
/// Execution is CPU-only in this build (the decoder's fused MultiHeadAttention op is not
/// yet supported on the DirectML EP; GPU support is a tracked follow-up).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OnnxLocalGrounder : IElementGrounder, IDisposable
{
    public const string BackendName = "onnx";
    public const string ModelPathVariable = "WINSIGHT_VISION_ONNX_PATH";
    public const string ExecutionProviderVariable = "WINSIGHT_VISION_EP";
    public const string SourceModel = "qwen2.5-vl-3b-onnx";

    private const string VisionModelRelativePath = @"onnx\vision_encoder_fp16.onnx";
    private const string EmbedModelRelativePath = @"onnx\embed_tokens_fp16.onnx";
    private const string DecoderModelRelativePath = @"onnx\decoder_model_merged_fp16.onnx";
    private const int DecoderLayers = 36;
    private const int KvHeads = 2;
    private const int HeadDim = 128;
    private const int FeatureLength = 1176;
    private const string GroundingSystemPrompt =
        "You are a GUI grounding assistant. Given a screenshot and an instruction, " +
        "output ONLY the click coordinate as [x, y] in the image's pixel space.";
    private const string PointReasoning =
        "Local ONNX grounding (Qwen2.5-VL); coordinate is the click center, box is zero-area.";

    private readonly OnnxLocalGrounderOptions _options;
    private readonly object _loadLock = new();
    private InferenceSession? _vision;
    private InferenceSession? _embed;
    private InferenceSession? _decoder;
    private QwenVisionTokenizer? _tokenizer;

    public OnnxLocalGrounder(OnnxLocalGrounderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public Task<GroundedElement> FindAsync(
        string screenshotPath,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenshotPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(screenshotPath))
        {
            throw new FileNotFoundException("Screenshot file not found.", screenshotPath);
        }

        ValidateConfiguration();

        // ONNX Runtime inference is synchronous; run it off the calling thread.
        return Task.Run(() => Ground(screenshotPath, query, cancellationToken), cancellationToken);
    }

    private GroundedElement Ground(string screenshotPath, string query, CancellationToken cancellationToken)
    {
        EnsureLoaded();
        InferenceSession vision = _vision!;
        InferenceSession embed = _embed!;
        InferenceSession decoder = _decoder!;
        QwenVisionTokenizer tokenizer = _tokenizer!;

        QwenPreprocessResult image = QwenImagePreprocessing.Build(
            screenshotPath,
            QwenImagePreprocessing.DefaultMinPixels,
            QwenImagePreprocessing.DefaultMaxPixels);
        int imageTokenCount = (image.GridH / 2) * (image.GridW / 2);
        long[] inputIds = tokenizer.BuildPrompt(GroundingSystemPrompt, query, imageTokenCount);

        int[] generated = Generate(vision, embed, decoder, tokenizer, image, inputIds, cancellationToken);
        string modelText = tokenizer.Decode(generated);
        (double cx, double cy) = GroundingCoordinateParser.ParseCenter(modelText);

        int x = ToOriginal(cx, image.ResizedWidth, image.OriginalWidth);
        int y = ToOriginal(cy, image.ResizedHeight, image.OriginalHeight);
        return new GroundedElement(x, y, 0, 0, 1.0, PointReasoning, SourceModel);
    }

    private int[] Generate(
        InferenceSession vision,
        InferenceSession embed,
        InferenceSession decoder,
        QwenVisionTokenizer tokenizer,
        QwenPreprocessResult image,
        long[] inputIds,
        CancellationToken cancellationToken)
    {
        int seq = inputIds.Length;
        DenseTensor<float> embeds = EmbedRun(embed, inputIds, seq);
        int hidden = embeds.Dimensions[2];

        DenseTensor<float> pixelValues = new(
            image.PixelValues, new[] { image.PixelValues.Length / FeatureLength, FeatureLength });
        DenseTensor<long> grid = new(
            new long[] { image.GridT, image.GridH, image.GridW }, new[] { 1, 3 });
        using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> visionOut = vision.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("pixel_values", pixelValues),
            NamedOnnxValue.CreateFromTensor("image_grid_thw", grid),
        }))
        {
            Tensor<float> features = visionOut[0].AsTensor<float>();
            int featureIndex = 0;
            for (int position = 0; position < seq; position++)
            {
                if (inputIds[position] == tokenizer.ImageTokenId)
                {
                    for (int d = 0; d < hidden; d++)
                    {
                        embeds[0, position, d] = features[featureIndex, d];
                    }
                    featureIndex++;
                }
            }
        }

        long[] positionData = PrefillPositions(inputIds, tokenizer.ImageTokenId, image.GridH, image.GridW, out long ropeDelta);
        Dictionary<string, DenseTensor<Float16>> past = new(DecoderLayers * 2);
        for (int layer = 0; layer < DecoderLayers; layer++)
        {
            past[$"past_key_values.{layer}.key"] = new DenseTensor<Float16>(new[] { 1, KvHeads, 0, HeadDim });
            past[$"past_key_values.{layer}.value"] = new DenseTensor<Float16>(new[] { 1, KvHeads, 0, HeadDim });
        }

        List<long> attention = new(Enumerable.Repeat(1L, seq));
        List<int> generated = new();
        HashSet<int> eos = new(tokenizer.EndOfSequenceIds);
        DenseTensor<float> stepEmbeds = embeds;
        long[] stepPositions = positionData;
        int stepLength = seq;

        for (int step = 0; step < _options.MaxNewTokens; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DenseTensor<long> attentionTensor = new(attention.ToArray(), new[] { 1, attention.Count });
            DenseTensor<long> positionTensor = new(stepPositions, new[] { 3, 1, stepLength });
            List<NamedOnnxValue> feeds = new(3 + (DecoderLayers * 2))
            {
                NamedOnnxValue.CreateFromTensor("inputs_embeds", stepEmbeds),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionTensor),
                NamedOnnxValue.CreateFromTensor("position_ids", positionTensor),
            };
            foreach (KeyValuePair<string, DenseTensor<Float16>> entry in past)
            {
                feeds.Add(NamedOnnxValue.CreateFromTensor(entry.Key, entry.Value));
            }

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = decoder.Run(feeds);
            Dictionary<string, DisposableNamedOnnxValue> byName = outputs.ToDictionary(v => v.Name, v => v);
            Tensor<Float16> logits = byName["logits"].AsTensor<Float16>();
            int vocab = logits.Dimensions[2];
            int lastPosition = logits.Dimensions[1] - 1;
            int best = 0;
            float bestValue = float.NegativeInfinity;
            for (int token = 0; token < vocab; token++)
            {
                float value = (float)logits[0, lastPosition, token];
                if (value > bestValue)
                {
                    bestValue = value;
                    best = token;
                }
            }
            generated.Add(best);
            if (eos.Contains(best))
            {
                break;
            }

            for (int layer = 0; layer < DecoderLayers; layer++)
            {
                past[$"past_key_values.{layer}.key"] = byName[$"present.{layer}.key"].AsTensor<Float16>().ToDenseTensor();
                past[$"past_key_values.{layer}.value"] = byName[$"present.{layer}.value"].AsTensor<Float16>().ToDenseTensor();
            }
            attention.Add(1L);
            long nextPosition = attention.Count - 1 + ropeDelta;
            stepPositions = new[] { nextPosition, nextPosition, nextPosition };
            stepLength = 1;
            stepEmbeds = EmbedRun(embed, new[] { (long)best }, 1);
        }

        return generated.ToArray();
    }

    private static DenseTensor<float> EmbedRun(InferenceSession embed, long[] ids, int seq)
    {
        DenseTensor<long> input = new(ids, new[] { 1, seq });
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> result =
            embed.Run(new[] { NamedOnnxValue.CreateFromTensor("input_ids", input) });
        return result[0].AsTensor<float>().ToDenseTensor();
    }

    // Port of the reference get_rope_index for a single batch with one contiguous image.
    private static long[] PrefillPositions(long[] ids, int imageTokenId, int gridH, int gridW, out long ropeDelta)
    {
        int seq = ids.Length;
        long[] positions = new long[3 * seq];
        int firstImage = Array.IndexOf(ids, (long)imageTokenId);
        long current = 0;
        long maxPosition = 0;
        int i = 0;
        while (i < seq)
        {
            if (firstImage >= 0 && i == firstImage)
            {
                int llmH = gridH / 2;
                int llmW = gridW / 2;
                for (int hh = 0; hh < llmH; hh++)
                {
                    for (int ww = 0; ww < llmW; ww++)
                    {
                        positions[i] = current;
                        positions[seq + i] = current + hh;
                        positions[(2 * seq) + i] = current + ww;
                        maxPosition = Math.Max(maxPosition, current + Math.Max(hh, ww));
                        i++;
                    }
                }
                current += Math.Max(gridH, gridW) / 2;
            }
            else
            {
                positions[i] = current;
                positions[seq + i] = current;
                positions[(2 * seq) + i] = current;
                maxPosition = Math.Max(maxPosition, current);
                current++;
                i++;
            }
        }
        ropeDelta = maxPosition + 1 - seq;
        return positions;
    }

    private static int ToOriginal(double resizedCoordinate, int resizedSize, int originalSize)
    {
        double scaled = resizedCoordinate * originalSize / resizedSize;
        return Math.Clamp((int)Math.Round(scaled, MidpointRounding.AwayFromZero), 0, originalSize - 1);
    }

    private void EnsureLoaded()
    {
        if (_decoder is not null)
        {
            return;
        }
        lock (_loadLock)
        {
            if (_decoder is not null)
            {
                return;
            }

            string modelPath = _options.ModelPath!;
            InferenceSession vision = new(Path.Combine(modelPath, VisionModelRelativePath));
            InferenceSession embed = new(Path.Combine(modelPath, EmbedModelRelativePath));
            InferenceSession decoder = new(Path.Combine(modelPath, DecoderModelRelativePath));
            QwenVisionTokenizer tokenizer = new(modelPath);

            _vision = vision;
            _embed = embed;
            _decoder = decoder;
            _tokenizer = tokenizer;
        }
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ModelPath))
        {
            throw new InvalidOperationException(
                $"{ModelPathVariable} environment variable is not set. " +
                "It must point to a local Qwen2.5-VL ONNX model directory; " +
                "no cloud fallback is performed for the ONNX backend.");
        }

        if (!Directory.Exists(_options.ModelPath))
        {
            throw new DirectoryNotFoundException(
                $"{ModelPathVariable} points to a directory that does not exist: {_options.ModelPath}");
        }

        string executionProvider = _options.EffectiveExecutionProvider;
        if (executionProvider is not "cpu")
        {
            throw new InvalidOperationException(
                $"{ExecutionProviderVariable} must be 'cpu' in this build; actual value: {executionProvider}. " +
                "DirectML is not enabled because the decoder model is not compatible with the DirectML EP yet.");
        }
    }

    public void Dispose()
    {
        _vision?.Dispose();
        _embed?.Dispose();
        _decoder?.Dispose();
    }
}
