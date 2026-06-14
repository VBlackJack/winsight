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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Cdp;
using Winsight.Core.Modules.Diagnostics;
using Winsight.Core.Modules.ManagedState;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Core.Modules.Vision;
using Winsight.Core.Modules.WebView2;

namespace Winsight.Mcp;

public static class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // MCP requires stdout to be free for protocol traffic. Logs go to stderr.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services.AddSingleton<WindowDiscovery>();
        builder.Services.AddSingleton<UiTreeInspector>();
        builder.Services.AddSingleton<ActionInvoker>();
        builder.Services.AddSingleton<ElementStateReader>();
        builder.Services.AddSingleton<ElementWaiter>();
        builder.Services.AddSingleton<ManagedStateReader>();
        builder.Services.AddSingleton<WindowCapture>();
        builder.Services.AddSingleton<WebView2Detector>();
        builder.Services.AddSingleton<DiagnosticsObserver>();

        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<CdpTargetDiscovery>(sp =>
        {
            HttpClient http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CdpTargetDiscovery));
            return new CdpTargetDiscovery(http);
        });
        builder.Services.AddSingleton<IElementGrounder>(VisionGrounderRegistration.Create);

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync();
    }
}
