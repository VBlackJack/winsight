# Copyright 2026 Julien Bombled
# Licensed under the Apache License, Version 2.0.
#
# Smoke test for Winsight.Mcp.
# Spawns the server, exchanges initialize + tools/list over stdio, and reports
# the discovered tool count and names. Exits non-zero on any failure.
#
# Usage:
#   .\tools\smoke-mcp.ps1
#   .\tools\smoke-mcp.ps1 -Configuration Debug

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [int]$ExpectedToolCount = 26,
    [int]$ResponseTimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $repoRoot "src\Winsight.Mcp\bin\$Configuration\net10.0-windows\winsight-mcp.dll"

if (-not (Test-Path $dll)) {
    Write-Error "Build artifact not found: $dll`nBuild first: dotnet build -c $Configuration"
    exit 1
}

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "dotnet"
$psi.Arguments = "`"$dll`""
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true

$proc = [System.Diagnostics.Process]::Start($psi)
try {
    $initialize = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"winsight-smoke","version":"0.1"}}}'
    $proc.StandardInput.WriteLine($initialize)
    $proc.StandardInput.Flush()

    $initResponseTask = $proc.StandardOutput.ReadLineAsync()
    if (-not $initResponseTask.Wait([TimeSpan]::FromSeconds($ResponseTimeoutSeconds))) {
        Write-Error "Timeout waiting for initialize response."
        exit 2
    }
    $initResponse = $initResponseTask.Result
    if ([string]::IsNullOrWhiteSpace($initResponse)) {
        Write-Error "Server returned empty initialize response."
        exit 2
    }

    $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $proc.StandardInput.Flush()

    $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list"}')
    $proc.StandardInput.Flush()

    $toolsResponseTask = $proc.StandardOutput.ReadLineAsync()
    if (-not $toolsResponseTask.Wait([TimeSpan]::FromSeconds($ResponseTimeoutSeconds))) {
        Write-Error "Timeout waiting for tools/list response."
        exit 3
    }
    $toolsResponse = $toolsResponseTask.Result

    $parsed = $toolsResponse | ConvertFrom-Json
    $tools = $parsed.result.tools
    $count = ($tools | Measure-Object).Count

    Write-Host "Server: $($parsed.id) of $($parsed.jsonrpc)" -ForegroundColor DarkGray
    Write-Host "Tools discovered ($count):" -ForegroundColor Cyan
    $tools | Sort-Object name | ForEach-Object {
        Write-Host "  - $($_.name)"
    }

    if ($count -ne $ExpectedToolCount) {
        Write-Error "Expected $ExpectedToolCount tools, got $count."
        exit 4
    }

    Write-Host ""
    Write-Host "OK." -ForegroundColor Green
}
finally {
    if (-not $proc.HasExited) {
        $proc.StandardInput.Close()
        $proc.WaitForExit(3000) | Out-Null
        if (-not $proc.HasExited) {
            $proc.Kill()
        }
    }
}
