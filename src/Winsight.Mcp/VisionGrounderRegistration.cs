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

using Microsoft.Extensions.DependencyInjection;
using Winsight.Core.Models;
using Winsight.Core.Modules.Vision;

namespace Winsight.Mcp;

internal static class VisionGrounderRegistration
{
    public const string BackendVariable = "WINSIGHT_VISION_BACKEND";
    public const string AnthropicBackend = "anthropic";

    public static IElementGrounder Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        IHttpClientFactory httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        return Create(httpClientFactory, Environment.GetEnvironmentVariable);
    }

    internal static IElementGrounder Create(
        IHttpClientFactory httpClientFactory,
        Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        string? backend = getEnvironmentVariable(BackendVariable);
        if (string.IsNullOrWhiteSpace(backend) ||
            string.Equals(backend, AnthropicBackend, StringComparison.OrdinalIgnoreCase))
        {
            HttpClient http = httpClientFactory.CreateClient(nameof(AnthropicVisionGrounder));
            return new AnthropicVisionGrounder(
                http,
                getEnvironmentVariable("ANTHROPIC_API_KEY"),
                getEnvironmentVariable("WINSIGHT_VISION_MODEL"),
                getEnvironmentVariable("WINSIGHT_VISION_API_URL"),
                ResolveTimeoutMs(getEnvironmentVariable("WINSIGHT_VISION_TIMEOUT_MS")));
        }

        if (string.Equals(backend, OnnxLocalGrounder.BackendName, StringComparison.OrdinalIgnoreCase))
        {
            return new OnnxLocalGrounder(new OnnxLocalGrounderOptions(
                getEnvironmentVariable(OnnxLocalGrounder.ModelPathVariable),
                getEnvironmentVariable(OnnxLocalGrounder.ExecutionProviderVariable)));
        }

        return new UnavailableVisionGrounder(
            $"Unsupported {BackendVariable} value '{backend}'. " +
            $"Expected '{AnthropicBackend}' or '{OnnxLocalGrounder.BackendName}'.");
    }

    private static int ResolveTimeoutMs(string? raw) =>
        int.TryParse(raw, out int value) && value > 0
            ? value
            : AnthropicVisionGrounder.DefaultTimeoutMs;

    private sealed class UnavailableVisionGrounder : IElementGrounder
    {
        private readonly string _message;

        public UnavailableVisionGrounder(string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            _message = message;
        }

        public Task<GroundedElement> FindAsync(
            string screenshotPath,
            string query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(_message);
        }
    }
}
