[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('Major', 'Minor', 'Patch')]
    [string]$Part = 'Patch',

    [Parameter(Mandatory = $false)]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$configPath = Join-Path $PSScriptRoot '..\nebula-rag\config.json'
$configPath = [System.IO.Path]::GetFullPath($configPath)

if (-not (Test-Path $configPath)) {
    throw "Could not find add-on config file: $configPath"
}

$config = Get-Content -Path $configPath -Raw | ConvertFrom-Json
$currentVersion = [Version]$config.version

if ([string]::IsNullOrWhiteSpace($Version)) {
    $newVersion = switch ($Part) {
        'Major' { [Version]::new($currentVersion.Major + 1, 0, 0) }
        'Minor' { [Version]::new($currentVersion.Major, $currentVersion.Minor + 1, 0) }
        default { [Version]::new($currentVersion.Major, $currentVersion.Minor, $currentVersion.Build + 1) }
    }
}
else {
    try {
        $newVersion = [Version]$Version
    }
    catch {
        throw "Invalid semantic version: '$Version'. Use format like 1.2.3"
    }
}

$config.version = $newVersion.ToString()
$config | ConvertTo-Json -Depth 20 | Set-Content -Path $configPath -Encoding utf8

Write-Host "Updated Home Assistant add-on version: $currentVersion -> $($config.version)"
Write-Host "File: $configPath"
