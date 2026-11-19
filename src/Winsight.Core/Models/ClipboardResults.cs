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

/// <summary>
/// Clipboard text and its length in UTF-16 code units. <see cref="Text"/> is
/// null when the clipboard holds no text, and after a write.
/// </summary>
public sealed record ClipboardTextResult(string? Text, int Length);

/// <summary>
/// What <c>type_keys</c> sent. <see cref="Restored"/> is set in paste mode only:
/// <c>full</c> when every saved clipboard format came back, <c>partial</c> when
/// some formats could not be saved (bitmaps, metafiles, private handles), and
/// <c>failed</c> when the restore itself failed.
/// </summary>
public sealed record TypeKeysResult(
    string Mode,
    int Length,
    long WindowHandle,
    string WindowTitle,
    FocusedElementInfo? FocusedElement,
    string? Restored = null,
    int SkippedFormats = 0,
    InputDispatchInfo? Dispatch = null);
