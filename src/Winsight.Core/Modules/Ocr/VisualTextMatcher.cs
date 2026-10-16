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
using Winsight.Core.Models;

namespace Winsight.Core.Modules.Ocr;

/// <summary>
/// Decides whether expected text is visible in an OCR result. OCR returns words
/// with its own spacing and casing, so both sides are compared case-insensitively
/// with runs of whitespace collapsed, line by line and across the whole read.
/// </summary>
public static class VisualTextMatcher
{
    public const string TextVisibleOperator = "text_visible";

    /// <summary>
    /// The first line that contains the text, or null. A match that spans lines
    /// returns one line made of all of them.
    /// </summary>
    public static VisualTextLine? Find(VisualTextResult read, string expected)
    {
        string needle = Normalize(expected);
        if (needle.Length == 0)
        {
            return null;
        }
        VisualTextLine? line = read.Lines.FirstOrDefault(
            candidate => Normalize(candidate.Text).Contains(needle, StringComparison.Ordinal));
        if (line is not null)
        {
            return line;
        }
        return Normalize(read.Text).Contains(needle, StringComparison.Ordinal)
            ? new VisualTextLine(
                read.Text,
                OcrGeometry.Union(read.Lines.Select(candidate => candidate.Bounds)),
                read.Lines.SelectMany(candidate => candidate.Words).ToList())
            : null;
    }

    internal static string Normalize(string text)
    {
        StringBuilder normalized = new(text.Length);
        bool pendingSpace = false;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = normalized.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                normalized.Append(' ');
                pendingSpace = false;
            }
            normalized.Append(char.ToLowerInvariant(character));
        }
        return normalized.ToString();
    }
}
