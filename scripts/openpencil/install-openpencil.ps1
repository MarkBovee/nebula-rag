param(
    [switch]$SkipBunInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[OpenPencil Setup] $Message" -ForegroundColor Cyan
}

function Test-CommandExists {
    param([string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

Write-Step "Validating Bun runtime"
$hasBun = Test-CommandExists -Name "bun"

if (-not $hasBun) {
    if ($SkipBunInstall) {
        throw "Bun is not installed. Install Bun first or rerun without -SkipBunInstall."
    }

    throw "Bun is not installed. Install Bun using the official instructions at https://bun.sh and rerun this script."
}

Write-Step "Installing OpenPencil MCP server and CLI"
bun add -g @open-pencil/mcp @open-pencil/cli

Write-Step "Verifying installed commands"
if (-not (Test-CommandExists -Name "openpencil-mcp-http")) {
    throw "openpencil-mcp-http command not found after install."
}

if (-not (Test-CommandExists -Name "openpencil")) {
    throw "openpencil command not found after install."
}

Write-Step "Setup complete"
Write-Host "Installed successfully. Next: run scripts/openpencil/start-openpencil-mcp.ps1" -ForegroundColor Green
