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

namespace Winsight.Core.Models;

/// <summary>A recognized word and its rectangle, in window coordinates.</summary>
public sealed record VisualTextWord(string Text, WindowRegion Bounds);

/// <summary>A recognized line, its rectangle (the union of its words) and its words, in window coordinates.</summary>
public sealed record VisualTextLine(string Text, WindowRegion Bounds, IReadOnlyList<VisualTextWord> Words);

/// <summary>
/// Text read from a window capture by the Windows OCR engine. <see cref="Text"/>
/// joins the lines with newlines; <see cref="Region"/> is the area that was read.
/// </summary>
public sealed record VisualTextResult(
    string Language,
    string Text,
    IReadOnlyList<VisualTextLine> Lines,
    WindowRegion Region,
    int Scale);
