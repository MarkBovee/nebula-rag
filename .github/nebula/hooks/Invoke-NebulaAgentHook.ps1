#!/usr/bin/env pwsh

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Claude", "Copilot")]
    [string]$Agent,

    [Parameter(Mandatory = $true)]
    [ValidateSet("SessionStart", "PreToolUse", "PostToolUse", "PostToolUseFailure", "ErrorOccurred", "StopFailure")]
    [string]$Event
)

$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Read-HookPayload {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return [ordered]@{}
    }

    try {
        return $raw | ConvertFrom-Json -AsHashtable -Depth 32
    }
    catch {
        return [ordered]@{ raw = $raw }
    }
}

function Get-Value {
    param(
        [hashtable]$Payload,
        [string]$Key
    )

    if ($Payload.ContainsKey($Key)) {
        return $Payload[$Key]
    }

    return $null
}

function Get-ProjectRoot {
    param([hashtable]$Payload)

    $cwd = Get-Value -Payload $Payload -Key "cwd"
    if (-not [string]::IsNullOrWhiteSpace($cwd)) {
        return [System.IO.Path]::GetFullPath($cwd)
    }

    return (Get-Location).Path
}

function Get-HookHome {
    $homeDirectory = $HOME
    if ([string]::IsNullOrWhiteSpace($homeDirectory)) {
        $homeDirectory = [System.IO.Path]::GetTempPath()
    }

    $hookHome = Join-Path $homeDirectory ".nebula-rag/hooks"
    Ensure-Directory -Path $hookHome
    return $hookHome
}

function Write-HookLog {
    param(
        [string]$Category,
        [hashtable]$Payload,
        [hashtable]$Extra
    )

    $hookHome = Get-HookHome
    $logPath = Join-Path $hookHome "$Category.jsonl"
    $entry = [ordered]@{
        timestampUtc = [DateTimeOffset]::UtcNow.ToString("O")
        agent = $Agent
        event = $Event
        cwd = Get-ProjectRoot -Payload $Payload
    }

    foreach ($key in $Extra.Keys) {
        $entry[$key] = $Extra[$key]
    }

    Add-Content -LiteralPath $logPath -Value (($entry | ConvertTo-Json -Depth 16 -Compress))
}

function Get-NebulaServerDefinition {
    param([string]$ProjectRoot)

    $projectConfigPath = Join-Path $ProjectRoot ".mcp.json"
    if (-not (Test-Path -LiteralPath $projectConfigPath)) {
        return $null
    }

    try {
        $projectConfig = Get-Content -LiteralPath $projectConfigPath -Raw | ConvertFrom-Json -AsHashtable
    }
    catch {
        return $null
    }

    if ($null -eq $projectConfig -or -not $projectConfig.ContainsKey("mcpServers")) {
        return $null
    }

    if (-not ($projectConfig["mcpServers"] -is [System.Collections.IDictionary])) {
        return $null
    }

    if (-not $projectConfig["mcpServers"].Contains("nebula-rag")) {
        return $null
    }

    return $projectConfig["mcpServers"]["nebula-rag"]
}

function Test-NebulaHttpEndpoint {
    param([string]$Url)

    try {
        $requestBody = @{
            jsonrpc = "2.0"
            id = "hook-health"
            method = "ping"
        } | ConvertTo-Json -Compress

        $null = Invoke-RestMethod -Method Post -Uri $Url -ContentType "application/json" -Body $requestBody -TimeoutSec 3
        return "healthy"
    }
    catch {
        return "unreachable"
    }
}

function Get-ClaudeToolName {
    param([hashtable]$Payload)

    return Get-Value -Payload $Payload -Key "tool_name"
}

function Get-CopilotToolName {
    param([hashtable]$Payload)

    return Get-Value -Payload $Payload -Key "toolName"
}

function Get-CopilotToolArgs {
    param([hashtable]$Payload)

    $toolArgs = Get-Value -Payload $Payload -Key "toolArgs"
    if ([string]::IsNullOrWhiteSpace($toolArgs)) {
        return [ordered]@{}
    }

    try {
        return $toolArgs | ConvertFrom-Json -AsHashtable -Depth 32
    }
    catch {
        return [ordered]@{ raw = $toolArgs }
    }
}

function Get-CommandText {
    param([hashtable]$Payload)

    if ($Agent -eq "Claude") {
        $toolInput = Get-Value -Payload $Payload -Key "tool_input"
        if ($toolInput -is [System.Collections.IDictionary] -and $toolInput.Contains("command")) {
            return $toolInput["command"]
        }

        return $null
    }

    $toolArgs = Get-CopilotToolArgs -Payload $Payload
    if ($toolArgs.ContainsKey("command")) {
        return $toolArgs["command"]
    }

    return $null
}

function Write-Decision {
    param(
        [string]$Behavior,
        [string]$Reason
    )

    if ($Agent -eq "Claude") {
        $output = @{
            hookSpecificOutput = @{
                hookEventName = "PreToolUse"
                permissionDecision = $Behavior
                permissionDecisionReason = $Reason
            }
        }
    }
    else {
        $output = @{
            permissionDecision = $Behavior
            permissionDecisionReason = $Reason
        }
    }

    Write-Output ($output | ConvertTo-Json -Depth 10 -Compress)
}

function Invoke-SessionStartHook {
    param([hashtable]$Payload)

    $projectRoot = Get-ProjectRoot -Payload $Payload
    $serverDefinition = Get-NebulaServerDefinition -ProjectRoot $projectRoot
    $transport = "not-configured"
    $health = "unknown"

    if ($serverDefinition -is [System.Collections.IDictionary]) {
        $transport = [string]$serverDefinition["type"]
        if ($transport -eq "http" -and $serverDefinition.Contains("url")) {
            $health = Test-NebulaHttpEndpoint -Url ([string]$serverDefinition["url"])
        }
    }

    Write-HookLog -Category "agent-events" -Payload $Payload -Extra ([ordered]@{
        phase = "session-start"
        transport = $transport
        health = $health
    })

    if ($Agent -eq "Claude") {
        $message = "NebulaRAG balanced hooks active. "
        if ($transport -eq "http") {
            $message += "Project MCP transport: http ($health). "
        }
        elseif ($transport -eq "stdio") {
            $message += "Project MCP transport: stdio. "
        }
        else {
            $message += "Nebula MCP config not detected in .mcp.json. "
        }

        $message += "For project-specific work, prefer Nebula memory recall for prior decisions and rag_query for current source context. Use tier='long_term' for durable decisions; tier='short_term' is the default for session notes. If Nebula fails, capture the issue and fall back to direct source inspection."
        Write-Output $message
    }
}

function Invoke-PreToolUseHook {
    param([hashtable]$Payload)

    $toolName = if ($Agent -eq "Claude") { Get-ClaudeToolName -Payload $Payload } else { Get-CopilotToolName -Payload $Payload }
    $commandText = Get-CommandText -Payload $Payload

    if ([string]::IsNullOrWhiteSpace($commandText)) {
        return
    }

    $blockedRules = @(
        @{ Pattern = '(?i)\brm\s+-rf\s+/(?:\s|$)'; Reason = "Refusing destructive root delete command." },
        @{ Pattern = '(?i)\bgit\s+reset\s+--hard\b'; Reason = "Refusing destructive git hard reset command." },
        @{ Pattern = '(?i)\bgit\s+checkout\s+--\s'; Reason = "Refusing destructive git checkout restore command." },
        @{ Pattern = '(?i)\bmkfs(?:\.\w+)?\b'; Reason = "Refusing filesystem formatting command." },
        @{ Pattern = ':\(\)\s*\{\s*:\|\:&\s*;\s*\}\s*;'; Reason = "Refusing fork bomb command." },
        @{ Pattern = '(?i)\b(?:shutdown|reboot|poweroff)\b'; Reason = "Refusing system power command." }
    )

    foreach ($rule in $blockedRules) {
        if ($commandText -match $rule.Pattern) {
            Write-HookLog -Category "policy-events" -Payload $Payload -Extra ([ordered]@{
                tool = $toolName
                command = $commandText
                decision = "deny"
                reason = $rule.Reason
            })

            Write-Decision -Behavior "deny" -Reason $rule.Reason
            return
        }
    }
}

function Invoke-PostToolHook {
    param([hashtable]$Payload)

    $toolName = if ($Agent -eq "Claude") { Get-ClaudeToolName -Payload $Payload } else { Get-CopilotToolName -Payload $Payload }
    $commandText = Get-CommandText -Payload $Payload
    $resultType = $null
    $resultText = $null

    if ($Agent -eq "Copilot") {
        $toolResult = Get-Value -Payload $Payload -Key "toolResult"
        if ($toolResult -is [System.Collections.IDictionary]) {
            $resultType = if ($toolResult.Contains("resultType")) { $toolResult["resultType"] } else { $null }
            $resultText = if ($toolResult.Contains("textResultForLlm")) { $toolResult["textResultForLlm"] } else { $null }
        }
    }

    $looksLikeNebulaIssue =
        (($toolName -as [string]) -match 'nebula-rag') -or
        (($commandText -as [string]) -match 'nebula')

    Write-HookLog -Category "tool-events" -Payload $Payload -Extra ([ordered]@{
        tool = $toolName
        command = $commandText
        resultType = $resultType
        resultText = $resultText
        nebulaRelated = $looksLikeNebulaIssue
    })
}

function Invoke-ErrorHook {
    param([hashtable]$Payload)

    $errorPayload = Get-Value -Payload $Payload -Key "error"
    $message = $null
    $name = $null

    if ($errorPayload -is [System.Collections.IDictionary]) {
        $message = if ($errorPayload.Contains("message")) { $errorPayload["message"] } else { $null }
        $name = if ($errorPayload.Contains("name")) { $errorPayload["name"] } else { $null }
    }

    Write-HookLog -Category "errors" -Payload $Payload -Extra ([ordered]@{
        errorName = $name
        errorMessage = $message
    })
}

$payload = Read-HookPayload

switch ($Event) {
    "SessionStart" {
        Invoke-SessionStartHook -Payload $payload
    }
    "PreToolUse" {
        Invoke-PreToolUseHook -Payload $payload
    }
    "PostToolUse" {
        Invoke-PostToolHook -Payload $payload
    }
    "PostToolUseFailure" {
        Invoke-PostToolHook -Payload $payload
    }
    "ErrorOccurred" {
        Invoke-ErrorHook -Payload $payload
    }
    "StopFailure" {
        Invoke-ErrorHook -Payload $payload
    }
}
