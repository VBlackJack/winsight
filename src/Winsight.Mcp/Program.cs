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
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.Clipboard;
using Winsight.Core.Modules.Cdp;
using Winsight.Core.Modules.Diagnostics;
using Winsight.Core.Modules.Lifecycle;
using Winsight.Core.Modules.ManagedState;
using Winsight.Core.Modules.Ocr;
using Winsight.Core.Modules.Orchestration;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Core.Modules.UserActivity;
using Winsight.Core.Modules.VisualRegression;
using Winsight.Core.Modules.Vision;
using Winsight.Core.Modules.WebView2;

namespace Winsight.Mcp;

public static class Program
{
    public static async Task Main(string[] args)
    {
        ProcessDpiAwareness.TryEnablePerMonitorV2();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // MCP requires stdout to be free for protocol traffic. Logs go to stderr.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        RegisterServices(builder.Services);

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithResourcesFromAssembly()
            .WithPromptsFromAssembly();
        builder.Services.AddSingleton<IConfigureOptions<McpServerOptions>, ToolListAnnouncer>();
        builder.Services.AddSingleton<IConfigureOptions<McpServerOptions>, McpServerConfiguration>();

        await builder.Build().RunAsync();
    }

    /// <summary>Registers the host's singleton services; shared with tests so a missing registration fails there.</summary>
    internal static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<SessionTarget>();
        services.AddSingleton<UiaSceneDriver>();
        services.AddSingleton<ISceneDriver>(sp => sp.GetRequiredService<UiaSceneDriver>());
        services.AddSingleton<SceneServices>();
        services.AddSingleton<SceneStore>(sp => new SceneStore(sp.GetRequiredService<SceneServices>()));
        services.AddSingleton<ApplicationProfiles>();
        services.AddSingleton<SessionMemory>();
        services.AddSingleton<WindowDiscovery>();
        services.AddSingleton<AppLauncher>();
        services.AddSingleton<UiTreeInspector>();
        services.AddSingleton<AffordanceScanner>();
        services.AddSingleton<UiSnapshotter>();
        services.AddSingleton<UiSnapshotStore>();
        services.AddSingleton<ActionInvoker>();
        services.AddSingleton<ElementStateReader>();
        services.AddSingleton<FocusedElementReader>();
        services.AddSingleton<WindowManager>();
        services.AddSingleton<ElementWaiter>();
        services.AddSingleton<UiEventWatcher>();
        services.AddSingleton<ManagedStateReader>();
        services.AddSingleton<WindowCapture>();
        services.AddSingleton<StepRunner>();
        services.AddSingleton<WebView2Detector>();
        services.AddSingleton<DiagnosticsObserver>();
        services.AddSingleton<VisualDiffer>();
        services.AddSingleton<UserActivityMonitor>();
        services.AddSingleton<ClipboardService>();
        services.AddSingleton<VisualWaiter>();
        services.AddSingleton<VisualTextReader>();

        services.AddHttpClient();
        services.AddSingleton<CdpTargetDiscovery>(sp =>
        {
            HttpClient http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CdpTargetDiscovery));
            return new CdpTargetDiscovery(http);
        });
        services.AddSingleton<IElementGrounder>(VisionGrounderRegistration.Create);
    }
}
