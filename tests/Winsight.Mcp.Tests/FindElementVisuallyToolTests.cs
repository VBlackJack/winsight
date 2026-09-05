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
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

public sealed class FindElementVisuallyToolTests
{
    [Fact]
    public void Validate_minimum_confidence_rejects_low_confidence_action()
    {
        GroundedElement grounded = Grounded(confidence: 0.49);

        OperationResult? result = FindElementVisuallyTool.ValidateMinimumConfidence(
            grounded,
            minimumConfidence: 0.5);

        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Error.Should().Contain(FindElementVisuallyTool.MinimumConfidenceVariable);
        result.Error.Should().Contain("refusing to click");
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public void Validate_minimum_confidence_allows_threshold_match()
    {
        GroundedElement grounded = Grounded(confidence: 0.5);

        OperationResult? result = FindElementVisuallyTool.ValidateMinimumConfidence(
            grounded,
            minimumConfidence: 0.5);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void Validate_minimum_confidence_rejects_invalid_model_confidence(double confidence)
    {
        GroundedElement grounded = Grounded(confidence);

        OperationResult? result = FindElementVisuallyTool.ValidateMinimumConfidence(
            grounded,
            minimumConfidence: 0.5);

        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Error.Should().Contain("valid range 0..1");
    }

    [Fact]
    public void Resolve_minimum_confidence_defaults_when_env_is_unset()
    {
        bool ok = FindElementVisuallyTool.TryResolveMinimumConfidence(
            out double minimumConfidence,
            out string error,
            _ => null);

        ok.Should().BeTrue();
        minimumConfidence.Should().Be(0.5);
        error.Should().BeEmpty();
    }

    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("0.75", 0.75)]
    [InlineData("1", 1.0)]
    public void Resolve_minimum_confidence_accepts_configured_threshold(
        string raw,
        double expected)
    {
        bool ok = FindElementVisuallyTool.TryResolveMinimumConfidence(
            out double minimumConfidence,
            out string error,
            name => name == FindElementVisuallyTool.MinimumConfidenceVariable ? raw : null);

        ok.Should().BeTrue();
        minimumConfidence.Should().Be(expected);
        error.Should().BeEmpty();
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("-0.01")]
    [InlineData("1.01")]
    [InlineData("NaN")]
    public void Resolve_minimum_confidence_rejects_invalid_config(string raw)
    {
        bool ok = FindElementVisuallyTool.TryResolveMinimumConfidence(
            out _,
            out string error,
            name => name == FindElementVisuallyTool.MinimumConfidenceVariable ? raw : null);

        ok.Should().BeFalse();
        error.Should().Contain(FindElementVisuallyTool.MinimumConfidenceVariable);
    }

    private static GroundedElement Grounded(double confidence) =>
        new(
            X: 20,
            Y: 30,
            Width: 10,
            Height: 12,
            Confidence: confidence,
            Reasoning: "test candidate",
            Source: "test");
}
