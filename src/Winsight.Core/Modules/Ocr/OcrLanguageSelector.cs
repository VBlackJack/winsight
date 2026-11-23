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

namespace Winsight.Core.Modules.Ocr;

/// <summary>
/// Picks the OCR recognizer to use when the requested language has none:
/// an installed one of the same script. Pure, over plain strings.
/// </summary>
internal static class OcrLanguageSelector
{
    private const char SubtagSeparator = '-';

    /// <summary>
    /// The installed tag to use, or null. Order: exact tag, same primary language
    /// subtag and script (en-GB for en-US), the user-profile recognizer when it
    /// has the same script, then the first installed recognizer of that script.
    /// An empty requested script never falls back.
    /// </summary>
    public static string? Select(
        string requestedTag,
        string? requestedScript,
        IReadOnlyList<(string Tag, string Script)> installed,
        string? userProfileTag)
    {
        ArgumentNullException.ThrowIfNull(installed);

        foreach ((string tag, _) in installed)
        {
            if (string.Equals(tag, requestedTag, StringComparison.OrdinalIgnoreCase))
            {
                return tag;
            }
        }

        if (string.IsNullOrWhiteSpace(requestedScript))
        {
            return null;
        }

        List<(string Tag, string Script)> sameScript = installed
            .Where(i => string.Equals(i.Script, requestedScript, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sameScript.Count == 0)
        {
            return null;
        }

        string primary = PrimarySubtag(requestedTag);
        foreach ((string tag, _) in sameScript)
        {
            if (string.Equals(PrimarySubtag(tag), primary, StringComparison.OrdinalIgnoreCase))
            {
                return tag;
            }
        }

        if (!string.IsNullOrWhiteSpace(userProfileTag))
        {
            foreach ((string tag, _) in sameScript)
            {
                if (string.Equals(tag, userProfileTag, StringComparison.OrdinalIgnoreCase))
                {
                    return tag;
                }
            }
        }

        return sameScript[0].Tag;
    }

    private static string PrimarySubtag(string tag)
    {
        int index = tag.IndexOf(SubtagSeparator);
        return index < 0 ? tag : tag[..index];
    }
}
