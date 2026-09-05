# Winsight v0.1.0 - 2026-09-05

First public release of the Windows x64 MCP server, with 41 tools for desktop
discovery, interaction, assertions, scenario execution, managed-state inspection,
diagnostics, screenshots, and visual grounding.

## Safety and reliability

- Process policy and macro execution use the same target; conflicting IDs fail.
- Graceful closure preserves applications awaiting user confirmation. Only
  `force=true` permits forced termination.
- Global dry-run prevents visual baseline creation and replacement.
- Coordinate clicks reject occluding windows and targets belonging to another
  process, including with `allowOutOfWindow=true`.
- Visual grounding retains the captured window identity and geometry. Stale
  targets and refused clicks return failures; confidence gates apply to actions.
- Trace storage failures preserve action outcomes. Unique capture filenames
  preserve before/after evidence in rapid batches.

## Install

**Recommended for desktop users:** run `Winsight-Setup-0.1.0-win-x64.exe`.
The English/French wizard installs for the current Windows user without
administrator rights. Select Codex, Claude Desktop, Claude Code and/or Gemini
CLI, then restart those clients. No command line or .NET SDK is required.
Client configuration folders are detected; you can also select clients manually.
The installer does not install the clients themselves or change their approvals.

Existing client settings are backed up before modification. Other settings and
MCP servers are retained; JSON/TOML formatting may be normalized. A conflicting
Winsight entry is preserved and reported instead of overwritten. Uninstall
removes only registrations still pointing to this installation. Run setup again
to add a client later. The package is not Authenticode-signed.

Download `winsight-0.1.0-win-x64.zip` and `SHA256SUMS.txt`, compare the archive's
SHA-256 using `Get-FileHash`, and extract it into a dedicated directory. Keep all
files together. The self-contained build includes the .NET runtime.

Configure your MCP client with an absolute executable path, for example:

```json
{
  "mcpServers": {
    "winsight": {
      "command": "C:\\Tools\\Winsight\\winsight-mcp.exe",
      "args": []
    }
  }
}
```

Use an interactive Windows x64 session with access to the target application.
Model weights, Python provisioning environments and API keys are not bundled.
The default vision backend uses the configured cloud API; select the ONNX
backend explicitly for local inference. See `docs/tools-reference.md` in the
archive for environment variables and tool parameters.

## Validation

The safety remediation passed 299 tests with zero failures; installer configuration
adds eight regression cases for preservation, conflicts, backups and rollback. Three real-model
tests were explicitly skipped because model/reference fixtures were absent.
Final local totals are 307 passed, zero failed, and three skipped. Installation,
installed-server smoke, refusal to uninstall with invalid configuration, and
successful scoped uninstall were exercised with an isolated Codex configuration.
Claude Code also connected using an isolated generated configuration. Gemini CLI
recognized its generated entry; folder trust remained enforced by the client.
The fresh stdio smoke initialized and listed 41 tools. Detailed evidence is in
`docs/audit-2026-09-05.md`.

Real model precision is not certified by these results. Native hit testing and
input dispatch are separate operations; other desktop actors can change the
target between them. The server's input lock serializes only its own calls.

## Resume francais

Cette premiere version fournit 41 outils pour inspecter et piloter les
applications Windows. Les neuf constats de l'audit sont corriges : protection
du processus cible, fermeture forcee explicite, respect du mode simulation,
clics controles et conservation des preuves. Les tests de modele reel restent
a executer avec les fichiers de reference requis.
Le setup Windows configure Codex, Claude Desktop, Claude Code et Gemini CLI avec
un assistant en francais ou anglais, sans commande a saisir.
