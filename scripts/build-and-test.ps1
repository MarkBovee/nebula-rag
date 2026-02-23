#!/usr/bin/env pwsh

<#
.SYNOPSIS
Build and test the NebulaRAG MCP Docker container

.DESCRIPTION
Builds the Docker image and validates it can start with proper environment variables.

.EXAMPLE
./scripts/build-and-test.ps1
#>

$ErrorActionPreference = "Stop"

# Colors
$green = "`e[32m"
$red = "`e[31m"
$blue = "`e[34m"
$reset = "`e[0m"

Write-Host ""
Write-Host "$blue╔═══════════════════════════════════════════════════╗$reset"
Write-Host "$blue║  NebulaRAG MCP Docker Build & Test               ║$reset"
Write-Host "$blue╚═══════════════════════════════════════════════════╝$reset"
Write-Host ""

# Step 1: Build
Write-Host "$blue→$reset Building Docker image..."
Write-Host ""

docker build -t nebula-rag-mcp:latest -f Dockerfile .

if ($LASTEXITCODE -ne 0) {
    Write-Host "$red✗$reset Build failed"
    exit 1
}

Write-Host "$green✓$reset Docker image built successfully"
Write-Host ""

# Step 2: Test container startup
Write-Host "$blue→$reset Testing container startup with environment variables..."
Write-Host ""

$testStarted = $false
try {
    # Start container with timeout - it should initialize and wait for JSON-RPC on stdin
    $process = Start-Process -FilePath "docker" -ArgumentList `
        "run", "--rm", "-i", `
        "-e", "NEBULARAG_Database__Host=192.168.1.135", `
        "-e", "NEBULARAG_Database__Database=brewmind", `
        "-e", "NEBULARAG_Database__Password=ENRZpeMpfHPXfw8PN8mi", `
        "nebula-rag-mcp:latest" `
        -PassThru -WindowStyle Hidden
    
    $testStarted = $true
    
    # Wait a moment for it to start
    Start-Sleep -Milliseconds 2000
    
    # Check if still running
    if ($process.HasExited) {
        Write-Host "$red✗$reset Container exited immediately"
        exit 1
    }
    
    Write-Host "$green✓$reset Container started successfully"
    Write-Host "$green✓$reset Waiting for JSON-RPC input (expected behavior)"
    
    # Kill the container
    $process | Stop-Process -Force -ErrorAction SilentlyContinue
    
}
catch {
    Write-Host "$red✗$reset Failed to start container: $_"
    exit 1
}

Write-Host ""
Write-Host "$green✓$reset All tests passed!"
Write-Host ""
Write-Host "Run the following to test with mcp.json:"
Write-Host "$blue  • Make sure Docker is running$reset"
Write-Host "$blue  • Connect Copilot to NebulaRAG MCP server$reset"
Write-Host ""
Write-Host "To manually test the container:"
Write-Host "$blue  docker run --rm -it \$reset"
Write-Host "$blue    -e NEBULARAG_Database__Host=192.168.1.135 \$reset"
Write-Host "$blue    -e NEBULARAG_Database__Database=brewmind \$reset"
Write-Host "$blue    -e NEBULARAG_Database__Password=ENRZpeMpfHPXfw8PN8mi \$reset"
Write-Host "$blue    nebula-rag-mcp:latest$reset"
Write-Host ""
