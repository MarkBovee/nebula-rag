#!/usr/bin/env pwsh

<#!
.SYNOPSIS
Build container image (optional) and run MCP smoke tests.
#>

param(
    [switch]$SkipBuild = $false,
    [string]$ImageName = "localhost/nebula-rag-mcp:latest",
    [string]$Engine = "podman"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if (-not (Get-Command $Engine -ErrorAction SilentlyContinue)) {
    throw "$Engine was not found in PATH."
}

if (-not $SkipBuild) {
    Write-Host "Building image '$ImageName' with $Engine"
    & $Engine build -f "Dockerfile" -t $ImageName "."
    if ($LASTEXITCODE -ne 0) {
        throw "Image build failed with exit code $LASTEXITCODE"
    }
}

Write-Host "Running MCP smoke tests via Inspector CLI"
& pwsh -File ".\scripts\test-mcp-server.ps1" -Engine $Engine -ImageName $ImageName
if ($LASTEXITCODE -ne 0) {
    throw "MCP smoke tests failed with exit code $LASTEXITCODE"
}

Write-Host "MCP test workflow passed."
