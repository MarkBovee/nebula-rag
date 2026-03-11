param(
    [int]$Port = 3100
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[OpenPencil MCP Check] $Message" -ForegroundColor Cyan
}

$baseUrl = "http://localhost:$Port/mcp"
$acceptHeader = "application/json, text/event-stream"

Write-Step "Initializing MCP session on $baseUrl"
$initializeBody = '{"jsonrpc":"2.0","id":"init-1","method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"nebula-rag-check","version":"1.0.0"}}}'
$initializeResponse = Invoke-WebRequest -Uri $baseUrl -Method Post -Headers @{ Accept = $acceptHeader } -ContentType "application/json" -Body $initializeBody -TimeoutSec 15
$sessionId = $initializeResponse.Headers["mcp-session-id"]

if ([string]::IsNullOrWhiteSpace($sessionId)) {
    throw "MCP session id missing in initialize response."
}

Write-Step "Session created: $sessionId"
Write-Step "Initialize handshake succeeded"
Write-Host $initializeResponse.Content
