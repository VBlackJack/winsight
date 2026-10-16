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
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Winsight.Mcp;

/// <summary>
/// Announces <c>notifications/tools/list_changed</c> once the client has
/// initialized. A client that cached the tool list of an earlier server
/// process across a reconnect then fetches it again. A session that kept its
/// old process gains nothing from this; <c>get_winsight_guide</c> reports that
/// case through <see cref="ServerBuildInfo"/>.
/// </summary>
internal sealed class ToolListAnnouncer : IConfigureOptions<McpServerOptions>
{
    private readonly IServiceProvider _services;

    public ToolListAnnouncer(IServiceProvider services)
    {
        _services = services;
    }

    public void Configure(McpServerOptions options)
    {
        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Tools ??= new ToolsCapability();
        options.Capabilities.Tools.ListChanged = true;
        options.Handlers.NotificationHandlers =
        [
            .. options.Handlers.NotificationHandlers ?? [],
            new(NotificationMethods.InitializedNotification, AnnounceAsync),
        ];
    }

    private async ValueTask AnnounceAsync(JsonRpcNotification notification, CancellationToken cancellationToken)
    {
        McpServer server = _services.GetRequiredService<McpServer>();
        await server.SendNotificationAsync(NotificationMethods.ToolListChangedNotification, cancellationToken)
            .ConfigureAwait(false);
    }
}
