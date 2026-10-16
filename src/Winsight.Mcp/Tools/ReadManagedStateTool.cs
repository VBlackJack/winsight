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
using Winsight.Core.Models;
using Winsight.Core.Modules.ManagedState;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class ReadManagedStateTool
{
    [McpServerTool(Name = "read_managed_state")]
    [Description("Attach to a managed (.NET) process and read the value of a private or internal field " +
                 "of the first heap instance whose type matches the given fully-qualified type name. " +
                 "Use this to assert against ViewModel state, singletons, or any internal data the app " +
                 "does not expose through automation. The host process must match the target architecture (x64).")]
    public static string ReadManagedState(
        ManagedStateReader reader,
        SessionTarget target,
        [Description("Fully-qualified .NET type name (namespace + class), e.g. 'MyApp.ViewModels.MainViewModel'.")]
        string typeName,
        [Description("Field name on the type, including backing-field naming, e.g. '_internalCount'.")]
        string fieldName,
        [Description("Target managed process id. Optional; falls back to the session default set by set_target.")]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return JsonSerializer.Serialize(OperationResult.Failure(targetError), JsonOptions.Default);
            }
            OperationResult result = reader.Read(pid, typeName, fieldName);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        });
    }
}
