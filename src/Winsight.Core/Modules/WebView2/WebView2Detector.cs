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

using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.WebView2;

[SupportedOSPlatform("windows")]
public sealed class WebView2Detector
{
    private const string WebView2ProcessName = "msedgewebview2.exe";

    public IReadOnlyList<WebView2Endpoint> Detect(int hostProcessId)
    {
        Dictionary<int, List<ProcessSnapshot>> processTree = BuildProcessTree();
        HashSet<int> listeningPorts = GetListeningTcpPorts();
        IEnumerable<ProcessSnapshot> descendants = EnumerateDescendants(hostProcessId, processTree);

        var endpoints = new List<WebView2Endpoint>();
        foreach (ProcessSnapshot process in descendants)
        {
            if (!string.Equals(process.Name, WebView2ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var port = WebView2CommandLineParser.TryGetDebuggingPort(process.CommandLine);
            endpoints.Add(new WebView2Endpoint(
                HostProcessId: hostProcessId,
                WebView2ProcessId: process.ProcessId,
                RemoteDebuggingPort: port,
                IsPortListening: port.HasValue && listeningPorts.Contains(port.Value),
                CommandLine: process.CommandLine ?? string.Empty));
        }
        return endpoints;
    }

    private static HashSet<int> GetListeningTcpPorts()
    {
        IPEndPoint[] listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        var ports = new HashSet<int>(listeners.Length);
        foreach (IPEndPoint endpoint in listeners)
        {
            ports.Add(endpoint.Port);
        }
        return ports;
    }

    private static Dictionary<int, List<ProcessSnapshot>> BuildProcessTree()
    {
        const string Query = "SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process";
        using var searcher = new ManagementObjectSearcher(Query);
        var byParent = new Dictionary<int, List<ProcessSnapshot>>();
        foreach (ManagementBaseObject? item in searcher.Get())
        {
            using ManagementBaseObject managementObject = item;
            var snapshot = new ProcessSnapshot(
                ProcessId: ToInt(managementObject["ProcessId"]),
                ParentProcessId: ToInt(managementObject["ParentProcessId"]),
                Name: managementObject["Name"]?.ToString() ?? string.Empty,
                CommandLine: managementObject["CommandLine"]?.ToString());

            if (!byParent.TryGetValue(snapshot.ParentProcessId, out List<ProcessSnapshot>? list))
            {
                list = new List<ProcessSnapshot>();
                byParent[snapshot.ParentProcessId] = list;
            }
            list.Add(snapshot);
        }
        return byParent;
    }

    private static int ToInt(object? value)
    {
        if (value is null)
        {
            return 0;
        }
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IEnumerable<ProcessSnapshot> EnumerateDescendants(
        int rootProcessId,
        IReadOnlyDictionary<int, List<ProcessSnapshot>> processTree)
    {
        var visited = new HashSet<int> { rootProcessId };
        var queue = new Queue<int>();
        queue.Enqueue(rootProcessId);

        while (queue.Count > 0)
        {
            var parentId = queue.Dequeue();
            if (!processTree.TryGetValue(parentId, out List<ProcessSnapshot>? children))
            {
                continue;
            }
            foreach (ProcessSnapshot child in children)
            {
                if (!visited.Add(child.ProcessId))
                {
                    continue;
                }
                yield return child;
                queue.Enqueue(child.ProcessId);
            }
        }
    }

    private sealed record ProcessSnapshot(
        int ProcessId,
        int ParentProcessId,
        string Name,
        string? CommandLine);
}
