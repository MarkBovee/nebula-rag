param(
    [ValidateSet("auto", "podman", "docker")]
    [string]$Engine = "auto",
    [string]$ImageName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "openpencil-common.ps1")

function Write-Step {
    param([string]$Message)
    Write-Host "[OpenPencil Build] $Message" -ForegroundColor Cyan
}

function Resolve-ContainerEngine {
    param([string]$RequestedEngine)

    if ($RequestedEngine -eq "podman" -or $RequestedEngine -eq "docker") {
        if ($null -eq (Get-Command $RequestedEngine -ErrorAction SilentlyContinue)) {
            throw "$RequestedEngine is not available."
        }

        return $RequestedEngine
    }

    if ($null -ne (Get-Command podman -ErrorAction SilentlyContinue)) {
        return "podman"
    }

    if ($null -ne (Get-Command docker -ErrorAction SilentlyContinue)) {
        return "docker"
    }

    throw "No container engine found. Install Podman or Docker first."
}

$settings = Get-OpenPencilSettings -ScriptPath $PSCommandPath
if ([string]::IsNullOrWhiteSpace($ImageName)) {
    $ImageName = if ([string]::IsNullOrWhiteSpace($settings.PodmanImage)) { "nebula-openpencil-mcp:latest" } else { $settings.PodmanImage }
}

$containerEngine = Resolve-ContainerEngine -RequestedEngine $Engine
$containerfilePath = Join-Path $PSScriptRoot "Containerfile"

Write-Step "Using engine: $containerEngine"
Write-Step "Building image: $ImageName"

& $containerEngine build -f $containerfilePath -t $ImageName $settings.RepoRoot
if ($LASTEXITCODE -ne 0) {
    throw "Failed to build OpenPencil MCP image."
}

Write-Host "Built image successfully: $ImageName" -ForegroundColor Green