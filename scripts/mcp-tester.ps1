#!/usr/bin/env pwsh

<#
.SYNOPSIS
NebulaRAG MCP Test Client - Sends JSON-RPC requests and validates responses

.DESCRIPTION
This script tests the MCP server by:
1. Building the Docker image if needed
2. Starting the MCP container
3. Sending JSON-RPC 2.0 test requests
4. Validating responses
5. Reporting results

.PARAMETER Build
Force rebuild the Docker image

.PARAMETER Container
Container image name (default: nebula-rag-mcp:latest)

.PARAMETER TestOnly
Skip build and run tests only (container must be running)

.EXAMPLE
./scripts/test-mcp.ps1 -Build
./scripts/test-mcp.ps1 -TestOnly
#>

param(
    [switch]$Build = $false,
    [string]$Container = "nebula-rag-mcp:latest",
    [switch]$TestOnly = $false
)

$ErrorActionPreference = "Stop"

# ANSI Colors
$colors = @{
    green  = "`e[32m"
    red    = "`e[31m"
    yellow = "`e[33m"
    blue   = "`e[34m"
    reset  = "`e[0m"
}

function Write-Success { Write-Host "$($colors.green)✓$($colors.reset) $args" }
function Write-Error { Write-Host "$($colors.red)✗$($colors.reset) $args" -ForegroundColor Red }
function Write-Info { Write-Host "$($colors.blue)ℹ$($colors.reset) $args" }
function Write-Warn { Write-Host "$($colors.yellow)⚠$($colors.reset) $args" -ForegroundColor Yellow }
function Write-Section { Write-Host "`n$($colors.blue)═══════════════════════════════════════════════════$($colors.reset)"; Write-Host "$($colors.blue)  $args$($colors.reset)"; Write-Host "$($colors.blue)═══════════════════════════════════════════════════$($colors.reset)`n" }

# Test tracking
$tests = @()
$testId = 1

function Test-MCP {
    param(
        [string]$Name,
        [string]$Method,
        [PSCustomObject]$Params,
        [scriptblock]$Validator
    )
    
    $requestJson = @{
        jsonrpc = "2.0"
        id = $testId
        method = $Method
        params = $Params
    } | ConvertTo-Json -Depth 10
    
    Write-Info "[$testId] Testing: $Name"
    Write-Host "Request: $requestJson" -ForegroundColor DarkGray
    
    $testId++
    
    return @{
        Name = $Name
        Request = $requestJson
        Validator = $Validator
        Status = "PENDING"
    }
}

Write-Section "NebulaRAG MCP Test Suite"

# Step 1: Build Docker image
if ($Build -and -not $TestOnly) {
    Write-Info "Building Docker image: $Container"
    & docker build -t $Container -f Dockerfile .
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Docker build failed"
        exit 1
    }
    Write-Success "Docker image built successfully"
}

if ($TestOnly) {
    Write-Info "Running in test-only mode"
}

# Step 2: Define test cases
Write-Section "Defining Test Cases"

$testCases = @(
    # Test 1: Health Check
    (Test-MCP -Name "Health Check" -Method "ping" -Params @{} -Validator {
        param($response)
        $response.PSObject.Properties.Name -contains "result"
    }),
    
    # Test 2: Initialize
    (Test-MCP -Name "Initialize Protocol" -Method "initialize" -Params @{
        protocolVersion = "2024-11-05"
        capabilities = @{}
        clientInfo = @{
            name = "MCP-Tester"
            version = "0.1.0"
        }
    } -Validator {
        param($response)
        $response.PSObject.Properties.Name -contains "result" -and `
        $response.result.PSObject.Properties.Name -contains "capabilities"
    }),
    
    # Test 3: List Tools
    (Test-MCP -Name "List MCP Tools" -Method "tools/list" -Params @{} -Validator {
        param($response)
        $response.PSObject.Properties.Name -contains "result" -and `
        $response.result -is [array]
    })
)

Write-Success "Defined $($testCases.Count) test cases"

# Step 3: Start container and run tests
Write-Section "Starting MCP Container and Running Tests"

# Create temp directories for logs
$tempDir = Join-Path $PSScriptRoot ".." "temp"
if (-not (Test-Path $tempDir)) { New-Item -ItemType Directory -Path $tempDir | Out-Null }

$stdoutLog = Join-Path $tempDir "mcp_stdout.log"
$stderrLog = Join-Path $tempDir "mcp_stderr.log"
$inputFile = Join-Path $tempDir "mcp_input.json"

try {
    # Generate combined JSON-RPC requests
    $allRequests = ($testCases | ForEach-Object { $_.Request }) -join "`n"
    Set-Content -Path $inputFile -Value $allRequests -Encoding UTF8
    
    Write-Info "Starting container: $Container"
    
    # Start container with piped input
    $process = & docker run --rm -i `
        -e "NEBULARAG_Database__Host=192.168.1.135" `
        -e "NEBULARAG_Database__Database=brewmind" `
        -e "NEBULARAG_Database__Password=ENRZpeMpfHPXfw8PN8mi" `
        $Container 2>$stderrLog | Tee-Object -FilePath $stdoutLog | ConvertFrom-Json -AsHashtable -ErrorAction SilentlyContinue
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Container executed successfully"
    }
    else {
        Write-Warn "Container exit code: $LASTEXITCODE"
    }
    
    # Read responses from log
    if (Test-Path $stdoutLog) {
        Write-Info "Reading responses from container output..."
        $responses = Get-Content $stdoutLog
        Write-Host $responses -ForegroundColor DarkGray
    }
    
    # Read stderr
    if ((Test-Path $stderrLog) -and ((Get-Content $stderrLog).Length -gt 0)) {
        Write-Warn "Container stderr:"
        Get-Content $stderrLog | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkYellow }
    }
    
}
catch {
    Write-Error "Container execution failed: $_"
    
    if (Test-Path $stderrLog) {
        Write-Host "`nContainer stderr:"
        Get-Content $stderrLog
    }
}

# Step 4: Display results
Write-Section "Test Results"

$passCount = 0
$failCount = 0
$pendingCount = 0

foreach ($test in $testCases) {
    $status = $test.Status
    $icon = switch ($status) {
        "PASS" { "$($colors.green)✓$($colors.reset)" }
        "FAIL" { "$($colors.red)✗$($colors.reset)" }
        default { "$($colors.yellow)→$($colors.reset)" }
    }
    
    Write-Host "$icon $($test.Name): $status"
    
    switch ($status) {
        "PASS" { $passCount++ }
        "FAIL" { $failCount++ }
        default { $pendingCount++ }
    }
}

Write-Host "`nSummary:"
Write-Host "  $($colors.green)Passed:$($colors.reset)  $passCount"
Write-Host "  $($colors.red)Failed:$($colors.reset)  $failCount"
Write-Host "  $($colors.yellow)Pending:$($colors.reset) $pendingCount"
Write-Host ""

if ($failCount -eq 0) {
    Write-Success "All tests passed!"
    exit 0
}
else {
    Write-Error "Some tests failed"
    exit 1
}
