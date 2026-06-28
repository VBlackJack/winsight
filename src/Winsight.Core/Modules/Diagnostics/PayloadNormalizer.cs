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

using System.Collections;

namespace Winsight.Core.Modules.Diagnostics;

public static class PayloadNormalizer
{
    public const int MaxDepth = 4;
    public const int MaxCollectionItems = 100;
    public const int MaxByteArrayBytes = 4096;
    public const int MaxStringChars = 8192;
    private const string DepthMarker = "<max depth>";
    private const string TruncatedMarker = "<truncated>";

    public static object? Normalize(object? value)
    {
        return NormalizeCore(value, depth: 0);
    }

    private static object? NormalizeCore(object? value, int depth)
    {
        if (value is null)
        {
            return null;
        }
        if (depth >= MaxDepth)
        {
            return DepthMarker;
        }

        return value switch
        {
            string s => TruncateString(s),
            bool
                or int or long or short or byte or sbyte
                or uint or ulong or ushort
                or double or float or decimal => value,
            DateTime dt => dt.ToUniversalTime(),
            Guid g => g.ToString(),
            Enum e => e.ToString(),
            byte[] bytes => NormalizeBytes(bytes),
            IDictionary<string, object> typedDict => NormalizeDictionary(typedDict, depth),
            IDictionary nonGenericDict => NormalizeNonGenericDictionary(nonGenericDict, depth),
            IEnumerable list => NormalizeList(list, depth),
            _ => TruncateString(value.ToString()),
        };
    }

    private static Dictionary<string, object?> NormalizeDictionary(
        IDictionary<string, object> source, int depth)
    {
        var result = new Dictionary<string, object?>(source.Count, StringComparer.Ordinal);
        var count = 0;
        foreach (KeyValuePair<string, object> pair in source)
        {
            if (count++ >= MaxCollectionItems)
            {
                result[TruncatedMarker] = $"{source.Count - MaxCollectionItems} item(s) omitted";
                break;
            }
            result[pair.Key] = NormalizeCore(pair.Value, depth + 1);
        }
        return result;
    }

    private static Dictionary<string, object?> NormalizeNonGenericDictionary(
        IDictionary source, int depth)
    {
        var result = new Dictionary<string, object?>(source.Count, StringComparer.Ordinal);
        var count = 0;
        foreach (DictionaryEntry entry in source)
        {
            if (count++ >= MaxCollectionItems)
            {
                result[TruncatedMarker] = $"{source.Count - MaxCollectionItems} item(s) omitted";
                break;
            }
            var key = entry.Key.ToString();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }
            result[key] = NormalizeCore(entry.Value, depth + 1);
        }
        return result;
    }

    private static List<object?> NormalizeList(IEnumerable source, int depth)
    {
        var result = new List<object?>();
        var count = 0;
        foreach (var item in source)
        {
            if (count++ >= MaxCollectionItems)
            {
                result.Add(TruncatedMarker);
                break;
            }
            result.Add(NormalizeCore(item, depth + 1));
        }
        return result;
    }

    private static string NormalizeBytes(byte[] bytes)
    {
        if (bytes.Length <= MaxByteArrayBytes)
        {
            return Convert.ToHexString(bytes);
        }

        string prefix = Convert.ToHexString(bytes.AsSpan(0, MaxByteArrayBytes));
        return $"{prefix}...<{bytes.Length - MaxByteArrayBytes} byte(s) omitted>";
    }

    private static string? TruncateString(string? value)
    {
        if (value is null || value.Length <= MaxStringChars)
        {
            return value;
        }
        return value[..MaxStringChars] + "...<truncated>";
    }
}
