Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "openpencil-common.ps1")

$processes = Get-OpenPencilMcpLocalProcesses
$containerId = Get-OpenPencilMcpContainerId

if (-not $processes -and [string]::IsNullOrWhiteSpace($containerId)) {
    Write-Host "No OpenPencil MCP process is running." -ForegroundColor Yellow
    exit 0
}

$killed = @()
foreach ($process in $processes) {
    Stop-Process -Id $process.ProcessId -Force
    $killed += $process.ProcessId
}

if (-not [string]::IsNullOrWhiteSpace($containerId)) {
    & podman stop $containerId | Out-Null
}

$parts = @()
if ($killed.Count -gt 0) {
    $parts += "process(es): $($killed -join ', ')"
}

if (-not [string]::IsNullOrWhiteSpace($containerId)) {
    $parts += "container: $containerId"
}

Write-Host "Stopped OpenPencil MCP $($parts -join '; ')" -ForegroundColor Green
