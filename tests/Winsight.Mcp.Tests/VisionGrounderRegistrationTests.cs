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
using Winsight.Mcp;
using Xunit;

namespace Winsight.Mcp.Tests;

public class VisionGrounderRegistrationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("anthropic")]
    [InlineData("ANTHROPIC")]
    public void Create_defaults_to_anthropic_grounder(string? backend)
    {
        IElementGrounder grounder = VisionGrounderRegistration.Create(
            new TestHttpClientFactory(),
            name => name == VisionGrounderRegistration.BackendVariable ? backend : null);

        grounder.Should().BeOfType<AnthropicVisionGrounder>();
    }

    [Theory]
    [InlineData("onnx")]
    [InlineData("ONNX")]
    public void Create_selects_onnx_grounder_without_requiring_model_at_startup(string backend)
    {
        IElementGrounder grounder = VisionGrounderRegistration.Create(
            new TestHttpClientFactory(),
            name => name == VisionGrounderRegistration.BackendVariable ? backend : null);

        grounder.Should().BeOfType<OnnxLocalGrounder>();
    }

    [Fact]
    public async Task Create_returns_invocation_failure_for_unknown_backend()
    {
        IElementGrounder grounder = VisionGrounderRegistration.Create(
            new TestHttpClientFactory(),
            name => name == VisionGrounderRegistration.BackendVariable ? "local" : null);

        Func<Task> act = () => grounder.FindAsync("unused.png", "increment button");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*WINSIGHT_VISION_BACKEND*anthropic*onnx*");
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}
