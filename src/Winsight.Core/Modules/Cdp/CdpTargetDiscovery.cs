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

using Winsight.Core.Models;

namespace Winsight.Core.Modules.Cdp;

/// <summary>
/// Queries a CDP debugging endpoint (as surfaced by <c>detect_webview2_endpoints</c>)
/// for its list of debuggable targets over plain HTTP.
/// </summary>
public sealed class CdpTargetDiscovery
{
    private readonly HttpClient _http;

    public CdpTargetDiscovery(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>
    /// Fetch and parse <c>/json/list</c> from the endpoint listening on
    /// <paramref name="port"/> at the loopback host.
    /// </summary>
    /// <exception cref="HttpRequestException">The endpoint is unreachable or returned a non-success status.</exception>
    public async Task<IReadOnlyList<CdpTarget>> ListAsync(
        int port,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);

        Uri uri = CdpDefaults.TargetListUri(port);
        using HttpResponseMessage response = await _http
            .GetAsync(uri, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        return CdpTargetParser.Parse(body);
    }
}
