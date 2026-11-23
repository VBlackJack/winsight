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

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Ocr;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// Live-window proof of the fixes shipped in 0.5.0 after the field report:
/// the screen-capture border mask, the OCR language fallback, title withholding
/// in list_windows and get_focused_element, strict actionableOnly observations
/// and the region_uniform warning. Environmental preconditions skip; product
/// behavior is asserted.
/// </summary>
[Trait("Category", "E2E")]
public class FieldReportScenarioTests
{
    private const string ReadyLabelId = "CountLabel";
    private const string ScreenSource = "screen";
    private const string ScreenPolicyRefusalPrefix = "Cannot capture with captureSource=\"screen\"";
    private const string UiaSource = "uia";
    private const string FallbackWarningPrefix = "ocr_language_fallback:";
    private const string InvalidLanguagePrefix = "ocr_language_invalid";
    private const string UnavailableOcrPrefix = "ocr_unavailable";
    private const string MalformedLanguageTag = "not a tag!!";
    private const string LatinScript = "Latn";
    private const string UniformWarningCode = "region_uniform:";
    private const string CaptureFileName = "winsight-field-report-";
    private const uint DwmExtendedFrameBounds = 9;
    private const int OpaqueAlpha = 255;
    private const int TransparentAlpha = 0;
    private const int UniformBlockWidth = 80;
    private const int UniformBlockHeight = 60;
    private const int UniformScanStep = 8;
    private const int FocusSettleMs = 750;
    private const int WindowSettleMs = 500;
    private const int ChannelMask = 0x00FFFFFF;

    // Latin-script tags that Windows does not ship an OCR component for on a stock image.
    private static readonly string[] LatinCandidateTags =
        { "nl-NL", "fi-FI", "pl-PL", "pt-PT", "da-DK", "sv-SE", "cs-CZ", "hu-HU", "ro-RO", "tr-TR" };

    [Fact]
    public void Screen_capture_leaves_the_invisible_border_transparent_and_keeps_the_window_size()
    {
        using Process app = LaunchSample();
        string outputPath = Path.Combine(Path.GetTempPath(), CaptureFileName + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            CallToolResult call = CaptureScreenshotTool.CaptureScreenshot(
                new WindowCapture(), new SessionTarget(), outputPath, includeImage: false,
                mainWindow: true, captureSource: ScreenSource, allowOccluded: true, processId: app.Id);
            string json = call.Content.OfType<TextContentBlock>().First().Text;
            using JsonDocument document = JsonDocument.Parse(json);
            if (call.IsError == true || !document.RootElement.GetProperty("success").GetBoolean())
            {
                string? error = document.RootElement.TryGetProperty("error", out JsonElement errorElement)
                    ? errorElement.GetString()
                    : null;
                if (error is not null && error.StartsWith(ScreenPolicyRefusalPrefix, StringComparison.Ordinal))
                {
                    Assert.Skip("The screen capture was refused by the occlusion policy (environmental): " + json);
                }

                Assert.Fail("The screen capture failed unexpectedly: " + json);
            }

            JsonElement data = document.RootElement.GetProperty("data");
            JsonElement window = data.GetProperty("window");
            JsonElement bounds = window.GetProperty("bounds");
            int left = bounds.GetProperty("left").GetInt32();
            int top = bounds.GetProperty("top").GetInt32();
            int width = bounds.GetProperty("width").GetInt32();
            int height = bounds.GetProperty("height").GetInt32();
            data.GetProperty("width").GetInt32().Should().Be(width);
            data.GetProperty("height").GetInt32().Should().Be(height);

            using Bitmap image = LoadDetached(outputPath);
            image.Width.Should().Be(width, "the image keeps the window rectangle size");
            image.Height.Should().Be(height);

            IntPtr hwnd = new(window.GetProperty("handle").GetInt64());
            if (DwmGetWindowAttribute(hwnd, DwmExtendedFrameBounds, out NativeRect frame, Marshal.SizeOf<NativeRect>()) != 0)
            {
                Assert.Skip("DWM extended frame bounds are unavailable on this runner.");
            }

            int insetLeft = frame.Left - left;
            int insetTop = frame.Top - top;
            int insetRight = left + width - frame.Right;
            int insetBottom = top + height - frame.Bottom;
            bool contained = insetLeft >= 0 && insetTop >= 0 && insetRight >= 0 && insetBottom >= 0;
            bool hasInset = insetLeft > 0 || insetTop > 0 || insetRight > 0 || insetBottom > 0;
            if (!contained || !hasInset)
            {
                Assert.Skip(
                    $"No invisible border to mask: window ({left},{top},{width}x{height}), " +
                    $"frame ({frame.Left},{frame.Top},{frame.Right},{frame.Bottom}).");
            }

            image.GetPixel(width / 2, height / 2).A.Should().Be(OpaqueAlpha, "the visible frame is copied opaque");
            if (insetLeft > 0)
            {
                image.GetPixel(0, height / 2).A.Should().Be(TransparentAlpha, "the left resize border is masked");
            }
            if (insetTop > 0)
            {
                image.GetPixel(width / 2, 0).A.Should().Be(TransparentAlpha, "the top resize border is masked");
            }
            if (insetRight > 0)
            {
                image.GetPixel(width - 1, height / 2).A.Should().Be(TransparentAlpha, "the right resize border is masked");
            }
            if (insetBottom > 0)
            {
                image.GetPixel(width / 2, height - 1).A.Should().Be(TransparentAlpha, "the bottom resize border is masked");
            }
        }
        finally
        {
            Kill(app);
            TryDelete(outputPath);
        }
    }

    [Fact]
    public void An_uninstalled_ocr_language_falls_back_to_an_installed_one_of_the_same_script()
    {
        string? candidate = PickUnsupportedLatinCandidate(out string skipReason);
        if (candidate is null)
        {
            Assert.Skip(skipReason);
        }

        using Process app = LaunchSample();
        try
        {
            using JsonDocument document = JsonDocument.Parse(VisualObservationTool.ReadTextVisually(
                new VisualTextReader(new WindowCapture()), new SessionTarget(),
                language: candidate, processId: app.Id));

            document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(document.RootElement.ToString());
            JsonElement data = document.RootElement.GetProperty("data");
            data.TryGetProperty("warning", out JsonElement warning).Should().BeTrue(
                "a fallback must report a warning: " + data);
            warning.GetString().Should().StartWith(FallbackWarningPrefix);
            data.TryGetProperty("requestedLanguage", out JsonElement requested).Should().BeTrue(
                "a fallback must report the requested language: " + data);
            requested.GetString().Should().Be(candidate);
            data.GetProperty("language").GetString().Should().NotBeNullOrEmpty().And.NotBe(candidate);
            data.GetProperty("text").GetString().Should().NotBeNullOrWhiteSpace("the fallback recognizer still reads the window");
        }
        finally
        {
            Kill(app);
        }
    }

    [Fact]
    public void A_malformed_ocr_language_tag_is_rejected_with_a_dedicated_code()
    {
        using Process app = LaunchSample();
        try
        {
            using JsonDocument document = JsonDocument.Parse(VisualObservationTool.ReadTextVisually(
                new VisualTextReader(new WindowCapture()), new SessionTarget(),
                language: MalformedLanguageTag, processId: app.Id));

            document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            document.RootElement.GetProperty("error").GetString().Should().StartWith(InvalidLanguagePrefix);
        }
        finally
        {
            Kill(app);
        }
    }

    [Fact]
    public void Desktop_wide_window_listing_withholds_titles_of_processes_other_than_the_target()
    {
        // Start the lab first: a failure here must not leave a launched sample behind.
        using WpfLabSession other = WpfLabSession.Start();
        using Process target = LaunchSample();
        try
        {
            Thread.Sleep(WindowSettleMs);
            SessionTarget session = new();
            session.Set(target.Id);
            WindowDiscovery discovery = new();

            List<JsonElement> desktop = ParseWindows(ListWindowsTool.ListWindows(discovery, session), out JsonDocument desktopDoc);
            using (desktopDoc)
            {
                List<JsonElement> targetWindows = desktop.Where(w => w.GetProperty("processId").GetInt32() == target.Id).ToList();
                List<JsonElement> otherWindows = desktop.Where(w => w.GetProperty("processId").GetInt32() == other.ProcessId).ToList();
                targetWindows.Should().NotBeEmpty("the target window is listed");
                otherWindows.Should().NotBeEmpty("the second process window is listed");
                targetWindows.Should().OnlyContain(w => !w.GetProperty("titleWithheld").GetBoolean());
                targetWindows.Should().Contain(w => w.GetProperty("title").GetString() != string.Empty,
                    "the target keeps its real title");
                otherWindows.Should().OnlyContain(w => w.GetProperty("titleWithheld").GetBoolean());
                otherWindows.Should().OnlyContain(w => w.GetProperty("title").GetString() == string.Empty);
            }

            List<JsonElement> opened = ParseWindows(
                ListWindowsTool.ListWindows(discovery, session, includeTitles: true), out JsonDocument openedDoc);
            using (openedDoc)
            {
                opened.Where(w => w.GetProperty("processId").GetInt32() == other.ProcessId)
                    .Should().Contain(w => w.GetProperty("title").GetString() != string.Empty,
                        "includeTitles opts in to every title");
            }

            List<JsonElement> perProcess = ParseWindows(
                ListWindowsTool.ListWindows(discovery, session, processId: other.ProcessId), out JsonDocument perProcessDoc);
            using (perProcessDoc)
            {
                perProcess.Should().NotBeEmpty();
                perProcess.Should().OnlyContain(w => !w.GetProperty("titleWithheld").GetBoolean());
                perProcess.Should().Contain(w => w.GetProperty("title").GetString() != string.Empty,
                    "a per-process listing returns real titles");
            }
        }
        finally
        {
            Kill(target);
        }
    }

    [Fact]
    public void A_focused_element_outside_the_target_has_its_name_and_window_title_withheld()
    {
        // Start the lab first: a failure here must not leave a launched sample behind.
        using WpfLabSession other = WpfLabSession.Start();
        using Process target = LaunchSample();
        try
        {
            Thread.Sleep(FocusSettleMs);
            SessionTarget session = new();
            session.Set(target.Id);

            using JsonDocument document = JsonDocument.Parse(
                ElementStateTool.GetFocusedElement(new FocusedElementReader(), session));
            if (!document.RootElement.GetProperty("success").GetBoolean())
            {
                Assert.Skip("No element owns keyboard focus on this runner: " + document.RootElement);
            }

            JsonElement data = document.RootElement.GetProperty("data");
            if (data.GetProperty("belongsToTarget").GetBoolean())
            {
                Assert.Skip("The target kept the keyboard focus (foreground lock), nothing to withhold.");
            }

            data.GetProperty("processId").GetInt32().Should().Be(other.ProcessId,
                "the focus belongs to the other process, not the target");
            data.GetProperty("topLevelWindowTitle").GetString().Should().BeEmpty();
            data.GetProperty("name").GetString().Should().BeEmpty();
            data.GetProperty("titleWithheld").GetBoolean().Should().BeTrue();
        }
        finally
        {
            Kill(target);
        }
    }

    [Fact]
    public void Strict_actionable_only_keeps_only_real_controls_and_ties_elements_to_listed_windows()
    {
        using Process app = LaunchSample();
        try
        {
            SceneStore store = new();
            SessionTarget session = new();

            string strictJson = VerifiedSceneTool.Observe(store, session, app.Id, actionableOnly: true);
            using JsonDocument strict = JsonDocument.Parse(strictJson);
            strict.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(strictJson);
            JsonElement strictData = strict.RootElement.GetProperty("data");
            string strictId = strictData.GetProperty("id").GetString()!;

            IReadOnlyList<SceneElement> strictElements = store.Get(strictId)!.Capture.Elements;
            strictElements.Should().NotBeEmpty("the sample exposes buttons, a checkbox and a combo box");
            strictElements.Should().OnlyContain(e => SceneActionability.IsActionable(e));

            JsonElement capture = strictData.GetProperty("capture");
            HashSet<long> listedWindows = capture.GetProperty("windows").EnumerateArray()
                .Select(w => w.GetProperty("windowHandle").GetInt64()).ToHashSet();
            listedWindows.Should().NotBeEmpty();
            JsonElement[] elements = capture.GetProperty("elements").EnumerateArray().ToArray();
            elements.Should().NotBeEmpty();
            foreach (JsonElement element in elements)
            {
                element.TryGetProperty("window", out _).Should().BeFalse("window bounds live in windows[] only");
                if (element.GetProperty("source").GetString() == UiaSource)
                {
                    JsonElement handle = element.GetProperty("windowHandle");
                    handle.ValueKind.Should().Be(JsonValueKind.Number);
                    listedWindows.Should().Contain(handle.GetInt64());
                }
            }

            string looseJson = VerifiedSceneTool.Observe(store, session, app.Id);
            using JsonDocument loose = JsonDocument.Parse(looseJson);
            string looseId = loose.RootElement.GetProperty("data").GetProperty("id").GetString()!;
            IReadOnlyList<SceneElement> allElements = store.Get(looseId)!.Capture.Elements;
            if (!allElements.Any(e => !SceneActionability.IsActionable(e)))
            {
                Assert.Skip("The sample exposes no element that the strict filter would exclude.");
            }

            allElements.Count.Should().BeGreaterThan(strictElements.Count, "the strict filter removed elements");
        }
        finally
        {
            Kill(app);
        }
    }

    [Fact]
    public void A_single_color_region_warns_region_uniform_while_a_full_window_read_does_not()
    {
        using Process app = LaunchSample();
        string probePath = Path.Combine(Path.GetTempPath(), CaptureFileName + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            WindowCapture capture = new();
            ScreenshotResult shot = capture.Capture(app.Id, probePath, mainWindow: true);
            WindowRegion? uniform;
            using (Bitmap image = LoadDetached(probePath))
            {
                uniform = FindUniformBlock(image);
            }
            if (uniform is null)
            {
                Assert.Skip($"No {UniformBlockWidth}x{UniformBlockHeight} single-color block in the {shot.Width}x{shot.Height} window.");
            }

            VisualTextReader reader = new(capture);
            string regionJson = VisualObservationTool.ReadTextVisually(
                reader, new SessionTarget(), region: uniform, processId: app.Id);
            using JsonDocument region = JsonDocument.Parse(regionJson);
            SkipWhenOcrUnavailable(region.RootElement);
            region.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(regionJson);
            JsonElement regionData = region.RootElement.GetProperty("data");
            regionData.TryGetProperty("warning", out JsonElement regionWarning).Should().BeTrue(regionJson);
            regionWarning.GetString().Should().Contain(UniformWarningCode);

            string fullJson = VisualObservationTool.ReadTextVisually(reader, new SessionTarget(), processId: app.Id);
            using JsonDocument full = JsonDocument.Parse(fullJson);
            full.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(fullJson);
            JsonElement fullData = full.RootElement.GetProperty("data");
            if (fullData.TryGetProperty("warning", out JsonElement fullWarning))
            {
                fullWarning.GetString().Should().NotContain(UniformWarningCode);
            }
        }
        finally
        {
            Kill(app);
            TryDelete(probePath);
        }
    }

    private static string? PickUnsupportedLatinCandidate(out string skipReason)
    {
        List<string> installed = VisualTextReader.AvailableLanguages().ToList();
        bool hasLatin = installed.Any(tag => string.Equals(
            new Windows.Globalization.Language(tag).Script, LatinScript, StringComparison.OrdinalIgnoreCase));
        if (!hasLatin)
        {
            skipReason = "No Latin-script OCR recognizer is installed: " + string.Join(", ", installed);
            return null;
        }

        HashSet<string> installedPrimaries = installed.Select(PrimarySubtag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string tag in LatinCandidateTags)
        {
            Windows.Globalization.Language language = new(tag);
            if (!installedPrimaries.Contains(PrimarySubtag(tag)) &&
                string.Equals(language.Script, LatinScript, StringComparison.OrdinalIgnoreCase) &&
                !Windows.Media.Ocr.OcrEngine.IsLanguageSupported(language))
            {
                skipReason = string.Empty;
                return tag;
            }
        }

        skipReason = "Every candidate tag is installed or supported: " + string.Join(", ", installed);
        return null;
    }

    private static string PrimarySubtag(string tag)
    {
        int index = tag.IndexOf('-', StringComparison.Ordinal);
        return index < 0 ? tag : tag[..index];
    }

    private static void SkipWhenOcrUnavailable(JsonElement result)
    {
        if (!result.GetProperty("success").GetBoolean() &&
            result.TryGetProperty("error", out JsonElement error) &&
            (error.GetString() ?? string.Empty).StartsWith(UnavailableOcrPrefix, StringComparison.Ordinal))
        {
            Assert.Skip("No OCR recognizer is available on this runner: " + error.GetString());
        }
    }

    private static List<JsonElement> ParseWindows(string json, out JsonDocument document)
    {
        document = JsonDocument.Parse(json);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array, json);
        return document.RootElement.EnumerateArray().ToList();
    }

    /// <summary>First block of exactly one RGB color that is at least the minimum size, in window coordinates.</summary>
    private static WindowRegion? FindUniformBlock(Bitmap image)
    {
        int width = image.Width;
        int height = image.Height;
        int[] pixels = new int[width * height];
        BitmapData data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int row = 0; row < height; row++)
            {
                Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * width, width);
            }
        }
        finally
        {
            image.UnlockBits(data);
        }

        for (int y = 0; y + UniformBlockHeight <= height; y += UniformScanStep)
        {
            for (int x = 0; x + UniformBlockWidth <= width; x += UniformScanStep)
            {
                if (IsSingleColor(pixels, width, x, y))
                {
                    return new WindowRegion(x, y, UniformBlockWidth, UniformBlockHeight);
                }
            }
        }
        return null;
    }

    private static bool IsSingleColor(int[] pixels, int stride, int left, int top)
    {
        int reference = pixels[top * stride + left] & ChannelMask;
        for (int y = top; y < top + UniformBlockHeight; y++)
        {
            for (int x = left; x < left + UniformBlockWidth; x++)
            {
                if ((pixels[y * stride + x] & ChannelMask) != reference)
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>Loads a PNG into memory so the file is not left locked.</summary>
    private static Bitmap LoadDetached(string path)
    {
        using MemoryStream stream = new(File.ReadAllBytes(path));
        using Bitmap loaded = new(stream);
        return new Bitmap(loaded);
    }

    private static Process LaunchSample()
    {
        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");
        Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");
        try
        {
            OperationResult appeared = new ElementWaiter().Wait(
                process.Id, new ElementLocator(AutomationId: ReadyLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);
            return process;
        }
        catch
        {
            Kill(process);
            process.Dispose();
            throw;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied or the process is already terminating.
        }
        catch (AggregateException)
        {
            // Some members of the process tree could not be terminated.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: the temp file is harmless.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out NativeRect value, int size);
}
