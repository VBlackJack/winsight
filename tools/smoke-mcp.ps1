# Copyright 2026 Julien Bombled
# Licensed under the Apache License, Version 2.0.
#
# Smoke test for Winsight.Mcp.
# Spawns the server, exchanges initialize + tools/list over stdio, and reports
# the discovered tool count and names. Also requires the
# notifications/tools/list_changed the server sends after initialization, and
# checks that get_winsight_guide reports the same tools and a current build.
# Exits non-zero on any failure.
#
# Usage:
#   .\tools\smoke-mcp.ps1
#   .\tools\smoke-mcp.ps1 -Configuration Debug

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [int]$ExpectedToolCount = 0,
    [int]$ResponseTimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $repoRoot "src\Winsight.Mcp\bin\$Configuration\net10.0-windows10.0.19041.0\winsight-mcp.dll"

if (-not (Test-Path $dll)) {
    Write-Error "Build artifact not found: $dll`nBuild first: dotnet build -c $Configuration"
    exit 1
}

if ($ExpectedToolCount -le 0) {
    $toolNamePattern = 'McpServerTool\(\s*Name\s*=\s*"([a-z0-9_]+)"'
    $sourceRoot = Join-Path $repoRoot "src\Winsight.Mcp"
    $declared = Get-ChildItem -Path $sourceRoot -Filter *.cs -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object { [regex]::Matches((Get-Content -LiteralPath $_.FullName -Raw), $toolNamePattern) } |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique
    $ExpectedToolCount = ($declared | Measure-Object).Count
    if ($ExpectedToolCount -eq 0) {
        Write-Error "Could not derive the expected tool count: no [McpServerTool(Name = ...)] found under $sourceRoot."
        exit 1
    }
    Write-Host "Expected tool count derived from sources: $ExpectedToolCount" -ForegroundColor DarkGray
}

$psi = New-Object System.Diagnostics.ProcessStartInfo
$runner = Join-Path $repoRoot "tools\run-mcp.ps1"
if (-not (Test-Path $runner)) {
    Write-Error "MCP runner not found: $runner"
    exit 1
}

$psi.FileName = "powershell"
$psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$runner`" -Configuration $Configuration"
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
    $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_winsight_guide","arguments":{}}}')
    $proc.StandardInput.Flush()

    # Notifications may arrive before, between or after the responses: read
    # messages until both responses and the list_changed notification are in.
    $responses = @{}
    $listChanged = $false
    $deadline = [DateTime]::UtcNow.AddSeconds($ResponseTimeoutSeconds)
    while (($responses.Count -lt 2 -or -not $listChanged) -and [DateTime]::UtcNow -lt $deadline) {
        $lineTask = $proc.StandardOutput.ReadLineAsync()
        $remaining = $deadline - [DateTime]::UtcNow
        if ($remaining -lt [TimeSpan]::Zero -or -not $lineTask.Wait($remaining)) {
            break
        }
        $message = $lineTask.Result | ConvertFrom-Json
        if ($message.method -eq "notifications/tools/list_changed") {
            $listChanged = $true
        }
        elseif ($null -ne $message.id) {
            $responses[[int]$message.id] = $message
        }
    }
    if (-not $responses.ContainsKey(2)) {
        Write-Error "Timeout waiting for tools/list response."
        exit 3
    }

    $parsed = $responses[2]
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

    if (-not $listChanged) {
        Write-Error "No notifications/tools/list_changed after initialization."
        exit 5
    }

    if (-not $responses.ContainsKey(3)) {
        Write-Error "Timeout waiting for the get_winsight_guide response."
        exit 6
    }
    $guide = ($responses[3].result.content[0].text | ConvertFrom-Json).data.server
    $guideCount = ($guide.registeredTools | Measure-Object).Count
    Write-Host "Guide: version $($guide.version), $guideCount registered tools, newer build available: $($guide.newerBuildAvailable)" -ForegroundColor DarkGray
    if ($guideCount -ne $count) {
        Write-Error "get_winsight_guide lists $guideCount tools but tools/list returned $count."
        exit 6
    }
    if ($guide.newerBuildAvailable -ne $false) {
        Write-Error "get_winsight_guide should report newerBuildAvailable=false for the build it runs (got '$($guide.newerBuildAvailable)')."
        exit 6
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
