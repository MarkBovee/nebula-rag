param(
    [int]$Port = 3100,
    [string]$OpenUiUrl,
    [switch]$UsePodman,
    [string]$PodmanImage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "openpencil-common.ps1")

function Write-Step {
    param([string]$Message)
    Write-Host "[OpenPencil MCP] $Message" -ForegroundColor Cyan
}

function Start-LocalMcpServer {
    param([int]$McpPort)

    if ($null -eq (Get-Command openpencil-mcp-http -ErrorAction SilentlyContinue)) {
        throw "openpencil-mcp-http not found. Run scripts/openpencil/install-openpencil.ps1 first."
    }

    Write-Step "Starting local OpenPencil MCP HTTP server on port $McpPort"
    $previousPort = $env:PORT
    $env:PORT = "$McpPort"

    try {
        return Start-Process -FilePath "openpencil-mcp-http" -ArgumentList @() -PassThru -WorkingDirectory (Get-Location)
    }
    finally {
        if ($null -eq $previousPort) {
            Remove-Item Env:PORT -ErrorAction SilentlyContinue
        }
        else {
            $env:PORT = $previousPort
        }
    }
}

function Start-PodmanMcpServer {
    param([int]$McpPort, [string]$Image)

    if ($null -eq (Get-Command podman -ErrorAction SilentlyContinue)) {
        throw "Podman not found. Install Podman or rerun without -UsePodman."
    }

    if ([string]::IsNullOrWhiteSpace($Image)) {
        throw "Podman image not specified. Provide -PodmanImage with an image that includes openpencil-mcp-http."
    }

    $workspacePath = (Get-Location).Path
    $containerName = Get-OpenPencilContainerName
    Write-Step "Starting OpenPencil MCP HTTP server in Podman on port $McpPort"
    $arguments = @(
        "run", "--rm", "-d",
        "--name", $containerName,
        "-p", "${McpPort}:3100",
        "-v", "${workspacePath}:/workspace",
        "-w", "/workspace",
        "-e", "HOST=0.0.0.0",
        "-e", "PORT=3100",
        "-e", "OPENPENCIL_MCP_ROOT=/workspace",
        $Image
    )

    $containerId = & podman @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Podman failed to start OpenPencil MCP HTTP container."
    }

    Write-Step "Podman container started: $containerId"
    return $containerId.Trim()
}

$settings = Get-OpenPencilSettings -ScriptPath $PSCommandPath
if (-not $PSBoundParameters.ContainsKey("OpenUiUrl")) {
    $OpenUiUrl = $settings.EditorUrl
}

if (-not $PSBoundParameters.ContainsKey("PodmanImage")) {
    $PodmanImage = $settings.PodmanImage
}

$shouldUsePodman = if ($PSBoundParameters.ContainsKey("UsePodman")) { $UsePodman.IsPresent } else { $settings.UsePodman }

$existingProcesses = Get-OpenPencilMcpLocalProcesses
$existingContainerId = Get-OpenPencilMcpContainerId

if ($existingProcesses -or $existingContainerId) {
    Write-Step "An OpenPencil MCP process is already running"
    if ($existingProcesses) {
        $existingProcesses | Select-Object ProcessId, CommandLine | Format-Table -AutoSize | Out-String | Write-Host
    }

    if ($existingContainerId) {
        Write-Host "Podman container: $existingContainerId" -ForegroundColor Yellow
    }

    Write-Host "Endpoint: http://localhost:$Port/mcp" -ForegroundColor Yellow
    exit 0
}

$result = if ($shouldUsePodman) {
    Start-PodmanMcpServer -McpPort $Port -Image $PodmanImage
}
else {
    Start-LocalMcpServer -McpPort $Port
}

Start-Sleep -Seconds 2

if ($result -is [System.Diagnostics.Process] -and $result.HasExited) {
    throw "OpenPencil MCP process exited immediately."
}

if ($result -is [System.Diagnostics.Process]) {
    Write-Step "Process started (PID: $($result.Id))"
}
elseif ($result -is [string]) {
    Write-Step "Container started: $result"
}

Write-Host "MCP endpoint: http://localhost:$Port/mcp" -ForegroundColor Green

if (-not [string]::IsNullOrWhiteSpace($OpenUiUrl)) {
    Write-Step "Opening configured OpenPencil web UI"
    Start-Process $OpenUiUrl
}
