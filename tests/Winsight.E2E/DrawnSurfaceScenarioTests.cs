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

using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Clipboard;
using Winsight.Core.Modules.Ocr;
using Winsight.Core.Modules.Orchestration;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// Text input, visual synchronization and OCR on the lab's pointer probe, a
/// window that, like iced, has no UI Automation content: it receives text
/// through WM_CHAR, draws its own status line, and animates for a fixed number
/// of frames after each left press before showing "Clicked N".
/// </summary>
[Trait("Category", "E2E")]
public class DrawnSurfaceScenarioTests
{
    private const string ProbeTitle = "Pointer Probe";
    private const string TypedTextId = "ProbeTypedText";
    private const string InitialStatus = "Status: ready";
    private const string ClickedStatus = "Clicked 1";
    private static readonly WindowRegion StatusArea = new(0, 30, 480, 90);
    private static readonly (int X, int Y) ClickPoint = (240, 250);

    [Fact]
    public void Type_keys_sends_unicode_text_line_breaks_and_tabs_to_a_surface_without_uia()
    {
        using WpfLabSession lab = PointerProbeScenarioTests.StartWithProbe();

        OperationResult typed = new ActionInvoker().TypeKeys(
            lab.ProcessId, "Hé \U0001F600\nok\tdone", ProbeTitle);

        typed.Success.Should().BeTrue(typed.Error);
        lab.WaitForName(TypedTextId, "Hé \U0001F600\\nok\\tdone").Should().BeTrue(lab.ReadName(TypedTextId));
    }

    [Fact]
    public void Paste_mode_types_through_the_clipboard_and_puts_the_previous_clipboard_back()
    {
        using WpfLabSession lab = PointerProbeScenarioTests.StartWithProbe();
        ClipboardService clipboard = new();
        string? before = ReadClipboard(clipboard);

        OperationResult pasted = new ActionInvoker().TypeKeys(
            lab.ProcessId, "pasted, not typed", ProbeTitle, paste: true);

        pasted.Success.Should().BeTrue(pasted.Error);
        lab.WaitForName(TypedTextId, "pasted, not typed").Should().BeTrue(lab.ReadName(TypedTextId));
        ((TypeKeysResult)pasted.Data!).Restored.Should().BeOneOf("full", "partial");
        // Compared without printing: the clipboard may hold the person's secrets.
        string.Equals(ReadClipboard(clipboard), before, StringComparison.Ordinal)
            .Should().BeTrue("the clipboard text from before the paste must be back");
    }

    [Fact]
    public void A_batch_waits_for_the_redraw_after_a_click_then_reads_the_new_status()
    {
        using WpfLabSession lab = PointerProbeScenarioTests.StartWithProbe();
        StepRunner runner = new(new ActionInvoker(), new ElementWaiter(), new ElementStateReader(), new WindowCapture());
        BatchStep[] steps =
        {
            new("assert", Label: "before", WindowTitle: ProbeTitle, Operator: "text_visible",
                ExpectedValue: InitialStatus, Region: StatusArea),
            new("click_point", Label: "click", X: ClickPoint.X, Y: ClickPoint.Y, WindowTitle: ProbeTitle,
                FocusWindow: true),
            new("wait_visual_change", Label: "animation starts", WindowTitle: ProbeTitle, TimeoutMs: 3000),
            new("wait_visual_stable", Label: "animation ends", WindowTitle: ProbeTitle, TimeoutMs: 5000),
            new("assert", Label: "after", WindowTitle: ProbeTitle, Operator: "text_visible",
                ExpectedValue: ClickedStatus, Region: StatusArea),
        };

        BatchResult result = runner.Run(steps, lab.ProcessId, stopOnError: true);

        result.Success.Should().BeTrue(string.Join("; ", result.Steps.Select(s => $"{s.Label}: {s.Error}")));
    }

    [Fact]
    public void Read_text_visually_returns_lines_with_window_coordinates()
    {
        using WpfLabSession lab = PointerProbeScenarioTests.StartWithProbe();

        OperationResult read = new VisualTextReader(new WindowCapture()).Read(lab.ProcessId, ProbeTitle);

        read.Success.Should().BeTrue(read.Error);
        VisualTextResult text = (VisualTextResult)read.Data!;
        VisualTextLine? status = VisualTextMatcher.Find(text, InitialStatus);
        status.Should().NotBeNull($"OCR read \"{text.Text}\"");
        status!.Bounds.Y.Should().BeInRange(StatusArea.Y, StatusArea.Y + StatusArea.Height,
            "the status line is drawn near the top of the client area");
    }

    private static string? ReadClipboard(ClipboardService clipboard)
    {
        OperationResult read = clipboard.ReadText();
        read.Success.Should().BeTrue(read.Error);
        return ((ClipboardTextResult)read.Data!).Text;
    }
}
