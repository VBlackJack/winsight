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
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Core.Modules.Vision;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// Measures local-grounder precision against UIA ground truth: for each query the
/// predicted point must fall inside the target element's bounding rectangle (in
/// window-relative coordinates, since the grounder works on a window screenshot).
/// Skipped unless WINSIGHT_VISION_ONNX_PATH points at a provisioned model.
/// </summary>
[Trait("Category", "E2E")]
public class VisionGroundingPrecisionTests
{
    private const string ReadyLabelId = "CountLabel";

    // query -> AutomationId of the element it must land on.
    private static readonly (string Query, string AutomationId)[] Cases =
    {
        ("Click the Increment button", "IncrementButton"),
        ("the Reset button", "ResetButton"),
        ("the dropdown to select an option", "OptionSelector"),
        ("the Enable feature checkbox", "FeatureToggle"),
        ("the button that opens a dialog", "OpenDialogButton"),
    };

    private readonly ITestOutputHelper _output;

    public VisionGroundingPrecisionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Vision")]
    public async Task Grounded_points_land_inside_their_target_elements()
    {
        string? modelPath = Environment.GetEnvironmentVariable("WINSIGHT_VISION_ONNX_PATH");
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            Assert.Skip("Requires WINSIGHT_VISION_ONNX_PATH.");
        }

        string executablePath = SampleAppLocator.ResolveExecutablePath();
        File.Exists(executablePath).Should().BeTrue(
            $"the sample app must be built before running E2E tests (looked at: {executablePath})");

        using Process process = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start sample app.");

        ElementWaiter waiter = new();
        WindowCapture capture = new();
        UiTreeInspector inspector = new();
        using OnnxLocalGrounder grounder = new(new OnnxLocalGrounderOptions(modelPath, "cpu"));

        string screenshotPath = Path.Combine(Path.GetTempPath(), $"winsight-precision-{Guid.NewGuid():N}.png");
        List<string> failures = new();

        try
        {
            OperationResult appeared = waiter.Wait(
                process.Id, new ElementLocator(AutomationId: ReadyLabelId), WaitCondition.Exists);
            appeared.Success.Should().BeTrue(appeared.Error);

            capture.Capture(process.Id, screenshotPath);
            UiElementNode root = inspector.Inspect(process.Id, 12, null, null, null);
            int originX = root.X;
            int originY = root.Y;

            _output.WriteLine($"window origin = ({originX},{originY}), size = {root.Width}x{root.Height}");
            _output.WriteLine($"{"query",-38} {"point",-12} {"rect (win-rel)",-26} hit");

            foreach ((string query, string automationId) in Cases)
            {
                UiElementNode? node = FindByAutomationId(root, automationId);
                node.Should().NotBeNull($"sample app must expose '{automationId}'");

                int rx = node!.X - originX;
                int ry = node.Y - originY;
                GroundedElement grounded = await grounder.FindAsync(
                    screenshotPath,
                    query,
                    TestContext.Current.CancellationToken);

                bool hit = grounded.X >= rx && grounded.X <= rx + node.Width
                    && grounded.Y >= ry && grounded.Y <= ry + node.Height;
                _output.WriteLine(
                    $"{query,-38} ({grounded.X,4},{grounded.Y,4})  " +
                    $"[{rx},{ry} {node.Width}x{node.Height}]".PadRight(26) + $" {(hit ? "HIT" : "MISS")}");
                if (!hit)
                {
                    failures.Add($"{automationId}: point ({grounded.X},{grounded.Y}) outside rect " +
                        $"[{rx},{ry} {node.Width}x{node.Height}]");
                }
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            if (File.Exists(screenshotPath))
            {
                File.Delete(screenshotPath);
            }
        }

        failures.Should().BeEmpty(
            "every grounded point must land inside its target element:\n" + string.Join("\n", failures));
    }

    private static UiElementNode? FindByAutomationId(UiElementNode node, string automationId)
    {
        if (string.Equals(node.AutomationId, automationId, StringComparison.Ordinal))
        {
            return node;
        }
        foreach (UiElementNode child in node.Children)
        {
            UiElementNode? found = FindByAutomationId(child, automationId);
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }
}
