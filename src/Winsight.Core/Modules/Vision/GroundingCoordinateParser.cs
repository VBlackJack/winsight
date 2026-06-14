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

using System.Globalization;
using System.Text.RegularExpressions;

namespace Winsight.Core.Modules.Vision;

/// <summary>
/// Parses the textual coordinate output of a local grounding model into a single
/// click point. Qwen2.5-VL-family models emit either a point <c>[x, y]</c> or a
/// bounding box <c>[x1, y1, x2, y2]</c>, optionally wrapped in prose; both are in
/// the model's (smart-resized) pixel space, so the caller denormalizes afterwards.
/// A box collapses to its center, which is what <c>GroundedElement</c> exposes.
/// </summary>
public static partial class GroundingCoordinateParser
{
    [GeneratedRegex(
        @"\[\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)" +
        @"(?:\s*,\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?))?\s*\]",
        RegexOptions.CultureInvariant)]
    private static partial Regex CoordinateRegex();

    /// <summary>
    /// Extracts the click point (center, in model pixel space) from the model text.
    /// Returns the box center when four numbers are present, the raw point when two.
    /// </summary>
    /// <exception cref="FormatException">No <c>[x, y]</c> or <c>[x1, y1, x2, y2]</c> list found.</exception>
    public static (double X, double Y) ParseCenter(string modelText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelText);

        Match match = CoordinateRegex().Match(modelText);
        if (!match.Success)
        {
            throw new FormatException(
                $"No coordinate list found in grounding output: \"{Truncate(modelText)}\".");
        }

        double x1 = Parse(match.Groups[1].Value);
        double y1 = Parse(match.Groups[2].Value);

        // Two numbers -> point; four -> box, return its center.
        if (!match.Groups[3].Success)
        {
            return (x1, y1);
        }

        double x2 = Parse(match.Groups[3].Value);
        double y2 = Parse(match.Groups[4].Value);
        return ((x1 + x2) / 2.0, (y1 + y2) / 2.0);
    }

    private static double Parse(string value)
    {
        return double.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string Truncate(string text)
    {
        const int Limit = 120;
        string collapsed = text.Replace('\n', ' ').Replace('\r', ' ');
        return collapsed.Length <= Limit ? collapsed : collapsed[..Limit] + "...";
    }
}
