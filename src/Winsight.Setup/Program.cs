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

using System.Globalization;
using System.Text.Json;

namespace Winsight.Setup;

public static class Program
{
    public static int Main(string[] args)
    {
        string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winsight", "logs");
        try
        {
            if (args.Length < 3 || args[0] is not ("install" or "uninstall"))
                throw new ArgumentException("Usage: winsight-configure install|uninstall <server.exe> <clients|manifest.json> [--compact] [--yield-ms <ms>]");
            string executable = Path.GetFullPath(args[1]);
            bool remove = args[0] == "uninstall";
            Dictionary<string, string> environment = ParseEnvironmentOptions(args.Skip(3).ToArray());
            string manifest = Path.Combine(Path.GetDirectoryName(executable)!, "client-registration.json");
            string[] paths;
            if (remove)
            {
                if (!File.Exists(manifest)) return 0;
                paths = JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifest)) ?? [];
            }
            else
            {
                if (!File.Exists(executable)) throw new FileNotFoundException("The MCP executable is missing.");
                paths = ResolvePaths(args[2]);
            }
            ClientConfiguration.Apply(paths, executable, remove, manifestPath: remove ? null : manifest, environment: environment.Count > 0 ? environment : null);
            Log(logDirectory, "INFO", remove ? "Client registrations removed." : "Selected clients configured. Restart clients to load Winsight.");
            return 0;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            // Configuration contents and parser diagnostics may contain credentials; do not log them.
            string message = "Configuration failed (" + exception.GetType().Name + "). Close clients, check existing Winsight registrations and configuration syntax, then rerun setup. Backups are next to the configuration files.";
            Log(logDirectory, "ERROR", message);
            Console.Error.WriteLine(message);
            return 3;
        }
    }

    public static string[] ResolvePaths(string clients)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string codex = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(profile, ".codex");
        string gemini = Path.Combine(Environment.GetEnvironmentVariable("GEMINI_CLI_HOME") ?? profile, ".gemini");
        string claude = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is string configured
            ? Path.Combine(configured, ".claude.json") : Path.Combine(profile, ".claude.json");
        return clients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(client => client switch
        {
            "codex" => Path.Combine(codex, "config.toml"),
            "claude-desktop" => Path.Combine(roaming, "Claude", "claude_desktop_config.json"),
            "claude-code" => claude,
            "gemini" => Path.Combine(gemini, "settings.json"),
            _ => throw new ArgumentException("Unknown client: " + client),
        }).ToArray();
    }

    public static Dictionary<string, string> ParseEnvironmentOptions(IReadOnlyList<string> optionArgs)
    {
        Dictionary<string, string> env = new(StringComparer.Ordinal);
        for (int i = 0; i < optionArgs.Count; i++)
        {
            switch (optionArgs[i])
            {
                case "--compact":
                    env["WINSIGHT_TOOL_PROFILE"] = "compact";
                    break;
                case "--yield-ms":
                    if (i + 1 >= optionArgs.Count ||
                        !int.TryParse(optionArgs[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int yieldMs) ||
                        yieldMs <= 0)
                    {
                        throw new ArgumentException("--yield-ms requires a positive integer number of milliseconds.");
                    }
                    env["WINSIGHT_YIELD_TO_USER_MS"] = yieldMs.ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentException("Unknown option: " + optionArgs[i]);
            }
        }
        return env;
    }

    private static void Log(string directory, string level, string message)
    {
        Directory.CreateDirectory(directory);
        string day = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        File.AppendAllText(Path.Combine(directory, "setup-" + day + ".log"),
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}");
    }
}
