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
using Winsight.Core.Modules.WebView2;

namespace Winsight.Mcp.Tools;

internal static class CdpEndpointPolicy
{
    public static OperationResult? DenyUnauthorizedPort(
        WebView2Detector detector,
        int hostProcessId,
        int port)
    {
        ArgumentNullException.ThrowIfNull(detector);

        if (port <= 0)
        {
            return OperationResult.Failure("CDP port must be a positive integer.");
        }

        IReadOnlyList<WebView2Endpoint> endpoints = detector.Detect(hostProcessId);
        WebView2Endpoint? match = endpoints.FirstOrDefault(endpoint =>
            endpoint.RemoteDebuggingPort == port);

        if (match is null)
        {
            return OperationResult.Failure(
                $"CDP port {port} is not exposed by a WebView2 descendant of authorized process {hostProcessId}. " +
                "Call detect_webview2_endpoints for the target process and use one of its RemoteDebuggingPort values.");
        }

        if (!match.IsPortListening)
        {
            return OperationResult.Failure(
                $"CDP port {port} was declared by a WebView2 descendant of process {hostProcessId}, " +
                "but no listening socket owned by that WebView2 process could be verified.");
        }

        return null;
    }
}
