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

using System.Text;
using System.Text.Json;
using Microsoft.ML.Tokenizers;

namespace Winsight.Core.Modules.Vision;

/// <summary>
/// Qwen2.5-VL tokenizer for grounding prompts. Qwen uses a byte-level GPT-2 BPE, so
/// <see cref="CodeGenTokenizer"/> reproduces it exactly once the vocab is patched with
/// Qwen's special tokens (which Qwen keeps out of <c>vocab.json</c>). Special tokens are
/// inserted by id when building the chat prompt rather than encoded from text.
/// </summary>
public sealed class QwenVisionTokenizer
{
    public const int EndOfTextId = 151643;
    public const int ImStartId = 151644;
    public const int ImEndId = 151645;
    public const int VisionStartId = 151652;
    public const int VisionEndId = 151653;
    public const int ImagePadId = 151655;

    private const string VocabFileName = "vocab.json";
    private const string MergesFileName = "merges.txt";

    private static readonly IReadOnlyDictionary<string, int> SpecialTokens = new Dictionary<string, int>
    {
        ["<|endoftext|>"] = EndOfTextId,
        ["<|im_start|>"] = ImStartId,
        ["<|im_end|>"] = ImEndId,
        ["<|object_ref_start|>"] = 151646,
        ["<|object_ref_end|>"] = 151647,
        ["<|box_start|>"] = 151648,
        ["<|box_end|>"] = 151649,
        ["<|quad_start|>"] = 151650,
        ["<|quad_end|>"] = 151651,
        ["<|vision_start|>"] = VisionStartId,
        ["<|vision_end|>"] = VisionEndId,
        ["<|vision_pad|>"] = 151654,
        ["<|image_pad|>"] = ImagePadId,
        ["<|video_pad|>"] = 151656,
    };

    private readonly CodeGenTokenizer _tokenizer;

    /// <summary>Loads <c>vocab.json</c>/<c>merges.txt</c> from the model directory.</summary>
    public QwenVisionTokenizer(string modelDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        string vocabPath = Path.Combine(modelDirectory, VocabFileName);
        string mergesPath = Path.Combine(modelDirectory, MergesFileName);
        if (!File.Exists(vocabPath) || !File.Exists(mergesPath))
        {
            throw new FileNotFoundException(
                $"Tokenizer files not found under '{modelDirectory}' " +
                $"(expected {VocabFileName} and {MergesFileName}).");
        }

        using MemoryStream patchedVocab = BuildPatchedVocab(vocabPath);
        using FileStream merges = File.OpenRead(mergesPath);
        _tokenizer = CodeGenTokenizer.Create(
            patchedVocab, merges,
            addPrefixSpace: false, addBeginOfSentence: false, addEndOfSentence: false);
    }

    /// <summary>EOS ids that terminate generation (im_end and endoftext).</summary>
    public IReadOnlyList<int> EndOfSequenceIds { get; } = new[] { ImEndId, EndOfTextId };

    /// <summary>The image-placeholder token id replaced by vision features.</summary>
    public int ImageTokenId => ImagePadId;

    /// <summary>Decodes generated token ids back to text.</summary>
    public string Decode(IReadOnlyList<int> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return _tokenizer.Decode(ids as IEnumerable<int> ?? ids.ToArray());
    }

    /// <summary>
    /// Builds the Qwen2.5-VL chat prompt input ids: system block, user block with
    /// <paramref name="imageTokenCount"/> image placeholders, then the assistant cue.
    /// </summary>
    public long[] BuildPrompt(string systemPrompt, string query, int imageTokenCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageTokenCount);

        List<long> ids = new(imageTokenCount + 64);

        ids.Add(ImStartId);
        AddText(ids, "system\n");
        AddText(ids, systemPrompt);
        ids.Add(ImEndId);
        AddText(ids, "\n");

        ids.Add(ImStartId);
        AddText(ids, "user\n");
        ids.Add(VisionStartId);
        for (int i = 0; i < imageTokenCount; i++)
        {
            ids.Add(ImagePadId);
        }
        ids.Add(VisionEndId);
        AddText(ids, query);
        ids.Add(ImEndId);
        AddText(ids, "\n");

        ids.Add(ImStartId);
        AddText(ids, "assistant\n");

        return ids.ToArray();
    }

    private void AddText(List<long> ids, string text)
    {
        foreach (int id in _tokenizer.EncodeToIds(text))
        {
            ids.Add(id);
        }
    }

    private static MemoryStream BuildPatchedVocab(string vocabPath)
    {
        Dictionary<string, int> vocab =
            JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(vocabPath))
            ?? throw new FormatException($"Could not parse vocabulary file '{vocabPath}'.");
        foreach (KeyValuePair<string, int> special in SpecialTokens)
        {
            vocab[special.Key] = special.Value;
        }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(vocab);
        return new MemoryStream(bytes, writable: false);
    }
}
