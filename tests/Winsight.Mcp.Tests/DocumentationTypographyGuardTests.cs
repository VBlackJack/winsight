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
using FluentAssertions;
using Xunit;

namespace Winsight.Mcp.Tests;

/// <summary>
/// The French documentation uses French accents but none of the typographic
/// stand-ins for ASCII characters: an ASCII character says the same thing and
/// survives a Windows console, a diff and a CI log. The scan is recursive, and
/// a separate test proves it reaches the nested French folder, because a
/// non-recursive sweep once passed while whole folders were never opened.
/// </summary>
public sealed class DocumentationTypographyGuardTests
{
    private const string SolutionFile = "Winsight.slnx";
    private const string FrenchReadme = "README.fr.md";
    private const string FrenchDocsFolder = "fr";
    private const string DocsFolder = "docs";
    private const string MarkdownPattern = "*.md";

    // Refused characters, each with the ASCII to write instead. This table only
    // refuses; arrows, box drawing, check marks and emoji are not in it and stay allowed.
    private static readonly Dictionary<char, string> Refused = new()
    {
        ['\u2010'] = "-",
        ['\u2013'] = "-",
        ['\u2014'] = "-, or rewrite with a colon, comma or parentheses",
        ['\u2212'] = "-",
        ['\u2018'] = "'",
        ['\u2019'] = "'",
        ['\u201C'] = "\"",
        ['\u201D'] = "\"",
        ['\u00AB'] = "\"",
        ['\u00BB'] = "\"",
        ['\u2026'] = "...",
        ['\u00A0'] = "a plain space",
        ['\u202F'] = "a plain space",
        ['\u2009'] = "a plain space",
        ['\u200B'] = "nothing",
        ['\uFEFF'] = "nothing",
        ['\u0153'] = "oe",
        ['\u0152'] = "OE",
        ['\u00E6'] = "ae",
        ['\u00C6'] = "AE",
    };

    [Fact]
    public void French_documentation_uses_only_ascii_punctuation_and_french_accents()
    {
        List<string> findings = new();
        foreach (string file in FrenchDocuments())
        {
            string[] lines = File.ReadAllLines(file);
            for (int line = 0; line < lines.Length; line++)
            {
                foreach (char character in lines[line].Where(Refused.ContainsKey))
                {
                    findings.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{Path.GetRelativePath(RepositoryRoot(), file)}:{line + 1}: U+{(int)character:X4}, write {Refused[character]}"));
                }
            }
        }

        findings.Should().BeEmpty();
    }

    [Fact]
    public void The_scan_reaches_the_nested_french_folder()
    {
        string frenchFolder = Path.Combine(RepositoryRoot(), DocsFolder, FrenchDocsFolder);
        List<string> scanned = FrenchDocuments();

        scanned.Should().Contain(
            file => Path.GetDirectoryName(file)!.StartsWith(frenchFolder, StringComparison.OrdinalIgnoreCase),
            "the guard must descend into docs/fr, not only read the repository root");
        scanned.Should().Contain(file => Path.GetFileName(file) == FrenchReadme);
    }

    [Fact]
    public void Accents_are_not_refused()
    {
        "éèêàâçùîôûëïÉÈÀÇ".Where(Refused.ContainsKey).Should().BeEmpty();
    }

    private static List<string> FrenchDocuments()
    {
        string root = RepositoryRoot();
        List<string> files = new() { Path.Combine(root, FrenchReadme) };
        files.AddRange(Directory.EnumerateFiles(
            Path.Combine(root, DocsFolder, FrenchDocsFolder), MarkdownPattern, SearchOption.AllDirectories));
        return files;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFile)))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException($"{SolutionFile} not found above {AppContext.BaseDirectory}.");
    }
}
