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

using System.Drawing;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.Capture;

/// <summary>An in-memory capture of a window, or of a region of it, with the window it came from.</summary>
public sealed class CapturedFrame : IDisposable
{
    public CapturedFrame(Bitmap bitmap, WindowIdentity window, WindowRegion region)
    {
        Bitmap = bitmap;
        Window = window;
        Region = region;
    }

    public Bitmap Bitmap { get; }

    public WindowIdentity Window { get; }

    /// <summary>The captured rectangle, in window coordinates.</summary>
    public WindowRegion Region { get; }

    /// <summary>Hand the bitmap to the caller, who disposes it; the frame itself holds nothing else.</summary>
    public Bitmap Detach() => Bitmap;

    public void Dispose() => Bitmap.Dispose();
}
