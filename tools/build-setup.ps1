#Requires -Version 5.1
# Copyright 2026 Julien Bombled
# Licensed under the Apache License, Version 2.0.
<#
.SYNOPSIS
Build the self-contained Windows installer and portable archive.
.PARAMETER OutputDirectory
Destination for release assets and temporary publish output.
.PARAMETER CompilerPath
Path to the Inno Setup 6 compiler.
.EXAMPLE
.\tools\build-setup.ps1
.NOTES
Exit codes: 0 success, 2 prerequisite missing, 3 build failed.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter()][string]$OutputDirectory = '',
    [Parameter()][string]$CompilerPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try {
    if (-not (Test-Path -LiteralPath $CompilerPath)) { Write-Error 'Install Inno Setup 6 or pass -CompilerPath.' -ErrorAction Continue; exit 2 }
    $repository = Split-Path -Parent $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repository 'artifacts\setup' }
    [xml]$properties = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
    $version = [string]$properties.Project.PropertyGroup.Version
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    if (-not $PSCmdlet.ShouldProcess($output, 'Build Winsight release assets')) { exit 0 }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $log = Join-Path $output ('build-' + (Get-Date -Format 'yyyyMMdd') + '.log')
    $payload = Join-Path $output ('winsight-' + $version + '-win-x64')
    # A fresh payload prevents files removed from the product leaking into a later package.
    if (Test-Path -LiteralPath $payload) { throw 'Payload directory already exists. Choose a fresh OutputDirectory.' }
    foreach ($project in @('Winsight.Mcp', 'Winsight.Setup')) {
        $destination = $payload
        if ($project -eq 'Winsight.Setup') { $destination = Join-Path $payload 'configure' }
        & dotnet publish (Join-Path $repository "src\$project") -c Release -r win-x64 --self-contained true --artifacts-path (Join-Path $output 'build') -o $destination -p:PublishSingleFile=false -p:PublishTrimmed=false >> $log 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $project. See $log" }
    }
    foreach ($name in @('LICENSE', 'NOTICE', 'README.md', 'CHANGELOG.md')) {
        Copy-Item -LiteralPath (Join-Path $repository $name) -Destination $payload
    }
    New-Item -ItemType Directory -Path (Join-Path $payload 'docs') | Out-Null
    foreach ($name in @('installation.html', 'tools-reference.md', 'runbook.md', 'architecture.md', 'audit-2026-09-05.md', 'verified-control.md', 'verified-control-validation.md', "release-notes-v$version.md")) {
        Copy-Item -LiteralPath (Join-Path $repository "docs\$name") -Destination (Join-Path $payload 'docs')
    }
    $commit = & git -C $repository rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not identify build commit.' }
    [IO.File]::WriteAllText((Join-Path $payload 'BUILD-COMMIT.txt'), $commit + "`n", [Text.UTF8Encoding]::new($false))
    & $CompilerPath /Qp "/DPayloadDir=$payload" "/DSetupOutputDir=$output" "/DAppVersion=$version" (Join-Path $repository 'installer\winsight.iss') >> $log 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed. See $log" }
    $archive = Join-Path $output ("winsight-$version-win-x64.zip")
    Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $archive
    $assets = @((Join-Path $output "Winsight-Setup-$version-win-x64.exe"), $archive)
    $lines = foreach ($asset in $assets) {
        (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($asset)
    }
    [IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
    Add-Content -LiteralPath $log -Encoding UTF8 -Value ('[' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '] [INFO] Release assets built for ' + $commit)
    Write-Output $assets
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 3
}
