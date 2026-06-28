# Copyright 2026 Julien Bombled
# Licensed under the Apache License, Version 2.0.
#
# Runs the MCP server from a temporary copy of the built Release output.
# This keeps src\Winsight.Mcp\bin recompilable while clients are connected.

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $repoRoot "src\Winsight.Mcp\bin\$Configuration\net10.0-windows"
$sourceDll = Join-Path $sourceDir "winsight-mcp.dll"

if (-not (Test-Path $sourceDll)) {
    Write-Error "Build artifact not found: $sourceDll`nBuild first: dotnet build -c $Configuration"
    exit 1
}

$runtimeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "winsight-mcp-runtime"
$runtimeDir = Join-Path $runtimeRoot ([Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null

try {
    Copy-Item -Path (Join-Path $sourceDir "*") -Destination $runtimeDir -Recurse -Force
    $runtimeDll = Join-Path $runtimeDir "winsight-mcp.dll"
    & dotnet $runtimeDll
    exit $LASTEXITCODE
}
finally {
    try {
        $resolvedRoot = [System.IO.Path]::GetFullPath($runtimeRoot)
        $resolvedDir = [System.IO.Path]::GetFullPath($runtimeDir)
        if ($resolvedDir.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $runtimeDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    catch {
        # Best-effort cleanup only; never mask the server exit path.
    }
}
