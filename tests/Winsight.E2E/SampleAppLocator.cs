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

namespace Winsight.E2E;

internal static class SampleAppLocator
{
    private const string SampleProjectName = "SampleWpfApp";
    private const string SampleAssemblyName = "SampleWpfApp.exe";

    public static string ResolveExecutablePath()
    {
        string? configured = Environment.GetEnvironmentVariable("WINSIGHT_SAMPLE_APP_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }
        var configuration = ResolveConfiguration();
        var targetFramework = ResolveTargetFramework();
        var repoRoot = ResolveRepoRoot();
        return Path.Combine(
            repoRoot,
            "samples",
            SampleProjectName,
            "bin",
            configuration,
            targetFramework,
            SampleAssemblyName);
    }

    private static string ResolveConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static string ResolveTargetFramework()
    {
        // Match the SampleWpfApp.csproj TargetFramework.
        var frameworkName = AppContext.TargetFrameworkName ?? string.Empty;
        // frameworkName is like ".NETCoreApp,Version=v10.0" - we want "net10.0-windows".
        return frameworkName.Contains("v10", StringComparison.Ordinal)
            ? "net10.0-windows"
            : "net10.0-windows";
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (current.GetFiles("Winsight.sln").Length > 0 ||
                current.GetDirectories("samples").Length > 0 &&
                current.GetDirectories("src").Length > 0)
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException(
            "Unable to locate the Winsight repository root from " + AppContext.BaseDirectory);
    }
}
