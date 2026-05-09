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

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class ListWindowsTool
{
    [McpServerTool(Name = "list_windows")]
    [Description("Enumerate every named top-level window currently on the desktop. " +
                 "Returns process id, native window handle, title, process name, foreground flag, " +
                 "and bounding rectangle for each window. Use this to discover candidate target processes " +
                 "before calling other tools. " +
                 "On failure, returns an OperationResult JSON with success=false and an error message.")]
    public static string ListWindows(WindowDiscovery discovery)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            var windows = discovery.List();
            return JsonSerializer.Serialize(windows, JsonOptions.Default);
        });
    }
}
