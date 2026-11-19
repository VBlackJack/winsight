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

using Winsight.Core.Modules.Capture;
using Winsight.Core.Modules.ManagedState;
using Winsight.Core.Modules.Ocr;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Core.Modules.VisualRegression;
using Winsight.Core.Modules.WebView2;

namespace Winsight.Mcp;

/// <summary>
/// Aggregates DI-managed Core and session services used by the verified scene tools so they
/// share the host's singleton instances instead of instantiating services per call.
/// </summary>
public sealed class SceneServices
{
    public static SceneServices Default { get; } = CreateDefault();

    public SceneServices(
        UiaSceneDriver driver,
        WindowDiscovery windows,
        ManagedStateReader managedState,
        ElementStateReader elementState,
        WebView2Detector webView2,
        WindowCapture capture,
        ActionInvoker invoker,
        VisualDiffer visualDiffer,
        VisualTextReader ocr,
        ApplicationProfiles profiles,
        SessionMemory memory)
    {
        Driver = driver;
        Windows = windows;
        ManagedState = managedState;
        ElementState = elementState;
        WebView2 = webView2;
        Capture = capture;
        Invoker = invoker;
        VisualDiffer = visualDiffer;
        Ocr = ocr;
        Profiles = profiles;
        Memory = memory;
    }

    public UiaSceneDriver Driver { get; }
    public WindowDiscovery Windows { get; }
    public ManagedStateReader ManagedState { get; }
    public ElementStateReader ElementState { get; }
    public WebView2Detector WebView2 { get; }
    public WindowCapture Capture { get; }
    public ActionInvoker Invoker { get; }
    public VisualDiffer VisualDiffer { get; }
    public VisualTextReader Ocr { get; }
    public ApplicationProfiles Profiles { get; }
    public SessionMemory Memory { get; }

    private static SceneServices CreateDefault()
    {
        WindowDiscovery windows = new();
        WindowCapture capture = new();
        return new SceneServices(
            new UiaSceneDriver(windows),
            windows,
            new ManagedStateReader(),
            new ElementStateReader(),
            new WebView2Detector(),
            capture,
            new ActionInvoker(),
            new VisualDiffer(),
            new VisualTextReader(capture),
            new ApplicationProfiles(),
            new SessionMemory());
    }
}
