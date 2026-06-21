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
using System.Drawing.Imaging;
using System.Net;
using System.Runtime.Versioning;
using FluentAssertions;
using Winsight.Core.Modules.Vision;
using Xunit;

namespace Winsight.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class AnthropicVisionGrounderTests
{
    [Fact]
    public async Task Retries_transient_failures_then_throws_after_max_attempts()
    {
        FakeHandler handler = new((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("overloaded"),
            }));
        using HttpClient http = new(handler);
        AnthropicVisionGrounder grounder = new(http, apiKey: "key", timeoutMs: 5_000);

        using TempPng image = TempPng.Create();
        Func<Task> act = () => grounder.FindAsync(image.Path, "the button");

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Contain("503");
        handler.Calls.Should().Be(3, "two retries follow the first attempt on a transient 5xx");
    }

    [Fact]
    public async Task Times_out_when_the_api_is_too_slow()
    {
        FakeHandler handler = new(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using HttpClient http = new(handler);
        AnthropicVisionGrounder grounder = new(http, apiKey: "key", timeoutMs: 100);

        using TempPng image = TempPng.Create();
        Func<Task> act = () => grounder.FindAsync(image.Path, "the button");

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Missing_api_key_fails_clearly_without_a_network_call()
    {
        FakeHandler handler = new((_, _) => throw new InvalidOperationException("should not be called"));
        using HttpClient http = new(handler);
        AnthropicVisionGrounder grounder = new(http, apiKey: null);

        using TempPng image = TempPng.Create();
        Func<Task> act = () => grounder.FindAsync(image.Path, "the button");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("ANTHROPIC_API_KEY");
        handler.Calls.Should().Be(0);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
        public int Calls;

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return _responder(request, cancellationToken);
        }
    }

    private sealed class TempPng : IDisposable
    {
        public string Path { get; }

        private TempPng(string path) => Path = path;

        public static TempPng Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"winsight-vision-test-{Guid.NewGuid():N}.png");
            using Bitmap bitmap = new(4, 4, PixelFormat.Format32bppArgb);
            bitmap.Save(path, ImageFormat.Png);
            return new TempPng(path);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
