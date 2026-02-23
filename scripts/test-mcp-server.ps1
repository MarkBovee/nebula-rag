#!/usr/bin/env pwsh

<#!
.SYNOPSIS
Run MCP smoke tests against the NebulaRAG server using MCP Inspector CLI.

.DESCRIPTION
Validates:
1. initialize handshake
2. tools/list exposes expected tools
3. tools/call works for rag_server_info
#>

param(
    [string]$Engine = "podman",
    [string]$ImageName = "localhost/nebula-rag-mcp:latest",
    [string]$EnvFilePath = ".env"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if (-not (Test-Path $EnvFilePath)) {
    throw "Environment file not found: $EnvFilePath"
}

function Invoke-InspectorCli {
    param(
        [string]$Method,
        [string[]]$ExtraArgs = @()
    )

    $baseArgs = @(
        "-y",
        "@modelcontextprotocol/inspector",
        "--cli",
        $Engine,
        "run",
        "--rm",
        "-i",
        "--pull=never",
        "--env-file",
        $EnvFilePath,
        $ImageName,
        "--skip-self-test",
        "--method",
        $Method
    )

    $allArgs = $baseArgs + $ExtraArgs
    $result = & npx @allArgs 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Inspector command failed for method '$Method': $result"
    }

    return $result
}

Write-Host "NebulaRAG MCP smoke tests"
Write-Host "Engine: $Engine"
Write-Host "Image: $ImageName"
Write-Host ""

$toolsListOutput = Invoke-InspectorCli -Method "tools/list"

$requiredTools = @(
    "query_project_rag",
    "rag_health_check",
    "rag_server_info",
    "rag_index_stats",
    "rag_recent_sources",
    "rag_list_sources",
    "rag_delete_source",
    "rag_purge_all"
)

foreach ($toolName in $requiredTools) {
    if ($toolsListOutput -notmatch [Regex]::Escape($toolName)) {
        throw "Missing expected tool: $toolName"
    }
}

Write-Host "PASS tools/list includes required tools"

$serverInfoOutput = Invoke-InspectorCli -Method "tools/call" -ExtraArgs @(
    "--tool-name",
    "rag_server_info"
)

if ($serverInfoOutput -notmatch "Nebula RAG MCP server information|serverName") {
    throw "rag_server_info returned unexpected output. Output: $serverInfoOutput"
}

Write-Host "PASS rag_server_info responded"
Write-Host ""
Write-Host "All MCP smoke tests passed."
