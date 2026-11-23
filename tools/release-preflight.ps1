#Requires -Version 5.1
# Copyright 2026 Julien Bombled
# Licensed under the Apache License, Version 2.0.
<#
.SYNOPSIS
Check that the repository is ready to tag and publish the version in Directory.Build.props.
.DESCRIPTION
Runs every check, prints one pass/fail line each, then exits. Nothing is
modified: the only network calls are git fetch, git ls-remote and gh run list.
.PARAMETER Workflow
CI workflow file whose run for HEAD must have succeeded.
.EXAMPLE
.\tools\release-preflight.ps1
.NOTES
Exit codes: 0 all checks passed, 2 prerequisite missing (git or gh), 3 a check failed.
#>
[CmdletBinding()]
param(
    [Parameter()][string]$Workflow = 'ci.yml'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExitOk = 0
$ExitPrerequisite = 2
$ExitCheckFailed = 3
$RemoteName = 'origin'
$MainRef = 'origin/main'
$ShortShaLength = 7

$script:failures = 0

function Write-Check {
    param([string]$Status, [string]$Name, [string]$Detail = '')
    $line = '[{0}] {1}' -f $Status, $Name
    if ($Detail) { $line += ' - ' + $Detail }
    $color = switch ($Status) { 'PASS' { 'Green' } 'WARN' { 'Yellow' } default { 'Red' } }
    Write-Host $line -ForegroundColor $color
    if ($Status -eq 'FAIL') { $script:failures++ }
}

function Invoke-Check {
    param([string]$Name, [scriptblock]$Body, [switch]$WarnOnly)
    try {
        $detail = & $Body
        Write-Check 'PASS' $Name ([string]$detail)
    } catch {
        $status = if ($WarnOnly) { 'WARN' } else { 'FAIL' }
        Write-Check $status $Name $_.Exception.Message
    }
}

# Native commands do not throw on a non-zero exit code: check it explicitly.
function Invoke-Native {
    param([string]$Command, [string[]]$Arguments)
    # Windows PowerShell 5.1 turns native stderr into terminating errors under 'Stop'.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = @(& $Command @Arguments 2>&1) } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "$Command $($Arguments -join ' ') failed (exit $LASTEXITCODE): $(($output | Out-String).Trim())" }
    # Return stdout only: stderr notices (gh, git line-ending warnings) must not corrupt parsed text.
    return @($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] })
}

foreach ($tool in @('git', 'gh')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Error "Prerequisite missing: $tool" -ErrorAction Continue
        exit $ExitPrerequisite
    }
}

try {
    $repository = Split-Path -Parent $PSScriptRoot
    [xml]$properties = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
    $version = [string]$properties.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($version)) { throw 'No Version in Directory.Build.props.' }
    $tag = "v$version"
    Write-Host "Release preflight for $tag" -ForegroundColor Cyan
    Push-Location -LiteralPath $repository
    try {
        $head = ''
        try { $head = ([string](Invoke-Native git @('rev-parse', 'HEAD'))).Trim() } catch { Write-Check 'FAIL' 'HEAD resolves' $_.Exception.Message }

        Invoke-Check 'git fetch origin --tags' {
            Invoke-Native git @('fetch', $RemoteName, '--tags') | Out-Null
        }

        Invoke-Check 'Working tree clean' {
            $status = ([string](Invoke-Native git @('status', '--porcelain') | Out-String)).Trim()
            if ($status) { throw 'uncommitted changes present' }
        }

        Invoke-Check "HEAD equals $MainRef" {
            $main = ([string](Invoke-Native git @('rev-parse', $MainRef))).Trim()
            if ($head -ne $main) { throw "HEAD $($head.Substring(0, $ShortShaLength)) differs from $MainRef $($main.Substring(0, $ShortShaLength))" }
            $head.Substring(0, $ShortShaLength)
        }

        $notes = Join-Path $repository "docs\release-notes-$tag.md"
        Invoke-Check "docs\release-notes-$tag.md exists" {
            if (-not (Test-Path -LiteralPath $notes)) { throw 'file not found' }
        }

        $mirror = Join-Path $repository "docs\fr\release-notes\$tag.md"
        Invoke-Check "docs\fr\release-notes\$tag.md exists (optional mirror)" {
            if (-not (Test-Path -LiteralPath $mirror)) { throw 'French mirror not found' }
        } -WarnOnly

        Invoke-Check "CHANGELOG.md has a dated entry for $version" {
            $pattern = '^## \[' + [regex]::Escape($version) + '\] - \d{4}-\d{2}-\d{2}'
            $found = Select-String -LiteralPath (Join-Path $repository 'CHANGELOG.md') -Pattern $pattern -Quiet
            if (-not $found) { throw 'no matching "## [version] - date" line' }
        }

        Invoke-Check "Tag $tag does not exist locally" {
            $local = ([string](Invoke-Native git @('tag', '--list', $tag) | Out-String)).Trim()
            if ($local) { throw 'tag already exists locally' }
        }

        Invoke-Check "Tag $tag does not exist on $RemoteName" {
            $remote = ([string](Invoke-Native git @('ls-remote', '--tags', $RemoteName, "refs/tags/$tag") | Out-String)).Trim()
            if ($remote) { throw "tag already exists on $RemoteName" }
        }

        Invoke-Check "$Workflow run for HEAD succeeded" {
            $json = [string](Invoke-Native gh @('run', 'list', '--commit', $head, '--workflow', $Workflow, '--json', 'status,conclusion') | Out-String)
            # Inner parentheses enumerate the array in Windows PowerShell 5.1.
            $runs = @(($json | ConvertFrom-Json))
            if ($runs.Count -eq 0) { throw 'no run found for this commit' }
            # gh run list returns the newest run first.
            $latest = $runs[0]
            $state = '{0}/{1}' -f $latest.status, $latest.conclusion
            if ($latest.status -ne 'completed' -or $latest.conclusion -ne 'success') { throw "latest run is $state" }
            "latest run $state"
        }
    } finally {
        Pop-Location
    }
} catch {
    Write-Check 'FAIL' 'Preflight setup' $_.Exception.Message
}

if ($script:failures -gt 0) {
    Write-Host "$($script:failures) check(s) failed." -ForegroundColor Red
    exit $ExitCheckFailed
}
Write-Host 'All checks passed.' -ForegroundColor Green
exit $ExitOk
