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
using Winsight.Core.Modules.Vision;
using Xunit;

namespace Winsight.Core.Tests;

public class VisionBackendTests
{
    [Theory]
    [InlineData("onnx", VisionBackendKind.Onnx)]
    [InlineData("ONNX", VisionBackendKind.Onnx)]
    [InlineData("  onnx  ", VisionBackendKind.Onnx)]
    [InlineData("anthropic", VisionBackendKind.Anthropic)]
    [InlineData(null, VisionBackendKind.Anthropic)]
    [InlineData("", VisionBackendKind.Anthropic)]
    [InlineData("garbage", VisionBackendKind.Anthropic)]
    public void ResolveBackend_defaults_to_anthropic_unless_onnx(string? raw, VisionBackendKind expected)
    {
        VisionBackend.ResolveBackend(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("dml", OnnxExecutionProvider.DirectML)]
    [InlineData("directml", OnnxExecutionProvider.DirectML)]
    [InlineData("cuda", OnnxExecutionProvider.Cuda)]
    [InlineData("cpu", OnnxExecutionProvider.Cpu)]
    [InlineData(null, OnnxExecutionProvider.Cpu)]
    [InlineData("garbage", OnnxExecutionProvider.Cpu)]
    public void ResolveExecutionProvider_defaults_to_cpu(string? raw, OnnxExecutionProvider expected)
    {
        VisionBackend.ResolveExecutionProvider(raw).Should().Be(expected);
    }
}
