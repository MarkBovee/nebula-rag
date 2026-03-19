#!/usr/bin/env pwsh

[CmdletBinding()]
param(
    [ValidateSet("Both", "User", "Project")]
    [string]$Mode = "Both",

    [string]$TargetPath,

    [ValidateSet("Both", "Copilot", "ClaudeCode", "VSCode")]
    [string]$ClientTargets = "Both",

    [Alias("UserConfigPath")]
    [string]$CopilotConfigPath,

    [Alias("ClaudeUserConfig")]
    [string]$ClaudeUserConfigPath,

    [string]$ClaudeProjectConfigPath,
    [string]$ClaudeSettingsPath,

    [ValidateSet("Auto", "Code", "Insiders", "Both")]
    [string]$Channel = "Auto",

    [string]$ServerName = "nebula-rag",
    [string]$ImageName = "localhost/nebula-rag-mcp:latest",
    [string]$TemplateRawBaseUrl = "https://raw.githubusercontent.com/MarkBovee/NebulaRAG/main",
    [string]$HomeAssistantMcpUrl = "http://homeassistant.local:8099/nebula/mcp",
    [string]$ExternalHomeAssistantMcpUrl,
    [switch]$UseExternalHomeAssistantUrl,
    [switch]$ForceExternal,

    [ValidateSet("Ask", "LocalContainer", "HomeAssistantAddon")]
    [string]$InstallTarget = "Ask",

    [string]$EnvFilePath,
    [switch]$CreateEnvTemplate,
    [switch]$SkipSkill,
    [switch]$NoBackup,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Backup-File {
    param(
        [string]$Path,
        [switch]$SkipBackup
    )

    if ($SkipBackup -or -not (Test-Path -LiteralPath $Path)) {
        return
    }

    $timestamp = Get-Date -Format "yyyyMMddHHmmss"
    $backupPath = "$Path.$timestamp.bak"
    Copy-Item -LiteralPath $Path -Destination $backupPath -Force
    Write-Host "Backed up existing file to: $backupPath"
}

function Resolve-HomeAssistantMcpUrl {
    param(
        [string]$LocalUrl,
        [string]$ExternalUrl,
        [switch]$PreferExternalUrl,
        [switch]$ExternalConsent
    )

    if ($PreferExternalUrl) {
        if (-not $ExternalConsent) {
            throw "External MCP URL mode is blocked by default for privacy. Re-run with -UseExternalHomeAssistantUrl -ForceExternal -ExternalHomeAssistantMcpUrl <url> to opt in."
        }

        if ([string]::IsNullOrWhiteSpace($ExternalUrl)) {
            throw "-UseExternalHomeAssistantUrl requires -ExternalHomeAssistantMcpUrl."
        }

        return $ExternalUrl
    }

    return $LocalUrl
}

function Resolve-InstallTarget {
    param([string]$SelectedInstallTarget)

    if ($SelectedInstallTarget -ne "Ask") {
        return $SelectedInstallTarget
    }

    if ($null -eq $Host -or $null -eq $Host.UI) {
        Write-Host "No interactive host available; defaulting to HomeAssistantAddon."
        return "HomeAssistantAddon"
    }

    Write-Host ""
    Write-Host "Select NebulaRAG install target:"
    Write-Host "  1) Home Assistant add-on (recommended)"
    Write-Host "  2) Local container MCP (Podman env-file workflow)"

    while ($true) {
        $selection = Read-Host "Enter 1 or 2 (default 1)"
        if ([string]::IsNullOrWhiteSpace($selection) -or $selection -eq "1") {
            return "HomeAssistantAddon"
        }

        if ($selection -eq "2") {
            return "LocalContainer"
        }

        Write-Host "Invalid selection. Enter 1 or 2."
    }
}

function Resolve-TemplateFile {
    param(
        [string]$TemplateRoot,
        [string]$RelativePath,
        [string]$RawBaseUrl,
        [switch]$Required
    )

    $localPath = Join-Path $TemplateRoot $RelativePath
    if (Test-Path -LiteralPath $localPath) {
        return [System.IO.Path]::GetFullPath($localPath)
    }

    if ([string]::IsNullOrWhiteSpace($RawBaseUrl)) {
        if ($Required) {
            throw "Required template file not found locally and no raw template base URL provided: $RelativePath"
        }

        return $null
    }

    $normalizedRelativePath = ($RelativePath -replace '\\', '/').TrimStart('/')
    $downloadRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nebula-rag-setup-template"
    $downloadPath = Join-Path $downloadRoot ($normalizedRelativePath -replace '/', '\\')

    if (-not (Test-Path -LiteralPath $downloadPath)) {
        Ensure-Directory -Path (Split-Path -Parent $downloadPath)
        $downloadUrl = "$($RawBaseUrl.TrimEnd('/'))/$normalizedRelativePath"

        try {
            Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath -ErrorAction Stop
            Write-Host "Downloaded template file: $normalizedRelativePath"
        }
        catch {
            if ($Required) {
                throw "Failed to download required template file '$normalizedRelativePath' from '$downloadUrl'. $($_.Exception.Message)"
            }

            Write-Host "Skip optional template file: failed to download $normalizedRelativePath"
            return $null
        }
    }

    return [System.IO.Path]::GetFullPath($downloadPath)
}

function Read-JsonObject {
    param(
        [string]$Path,
        [hashtable]$DefaultRoot,
        [switch]$SkipBackup
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $DefaultRoot
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $DefaultRoot
    }

    try {
        $parsed = $raw | ConvertFrom-Json -AsHashtable -Depth 64
    }
    catch {
        $sanitized = $raw -replace '(?s)/\*.*?\*/', ''
        $sanitized = $sanitized -replace '(?m)^\s*//.*$', ''
        $sanitized = $sanitized -replace ',(\s*[}\]])', '$1'

        try {
            $parsed = $sanitized | ConvertFrom-Json -AsHashtable -Depth 64
        }
        catch {
            Backup-File -Path $Path -SkipBackup:$SkipBackup
            Write-Warning "Invalid JSON in config: $Path"
            Write-Warning "Continuing with a fresh root for this path."
            return $DefaultRoot
        }
    }

    if (-not ($parsed -is [System.Collections.IDictionary])) {
        return $DefaultRoot
    }

    return $parsed
}

function Write-JsonObject {
    param(
        [string]$Path,
        [hashtable]$Root,
        [switch]$SkipBackup
    )

    Ensure-Directory -Path (Split-Path -Parent $Path)
    Backup-File -Path $Path -SkipBackup:$SkipBackup
    $json = $Root | ConvertTo-Json -Depth 64
    Set-Content -LiteralPath $Path -Value $json -Encoding utf8
    Write-Host "Wrote config: $Path"
}

function Copy-FileSafe {
    param(
        [string]$Source,
        [string]$Destination,
        [switch]$ForceWrite
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Source file not found: $Source"
    }

    $sourceFullPath = [System.IO.Path]::GetFullPath($Source)
    $destinationFullPath = [System.IO.Path]::GetFullPath($Destination)
    if ([System.StringComparer]::OrdinalIgnoreCase.Equals($sourceFullPath, $destinationFullPath)) {
        Write-Host "Skip same source/destination: $Destination"
        return
    }

    if (Test-Path -LiteralPath $Destination) {
        $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
        if ([System.StringComparer]::OrdinalIgnoreCase.Equals($sourceHash, $destinationHash)) {
            Write-Host "Skip unchanged file: $Destination"
            return
        }
    }

    if ((Test-Path -LiteralPath $Destination) -and -not $ForceWrite) {
        Write-Host "Skip existing file: $Destination"
        return
    }

    Ensure-Directory -Path (Split-Path -Parent $Destination)
    Copy-Item -LiteralPath $Source -Destination $Destination -Force -ErrorAction Stop
    Write-Host "Copied file: $Destination"
}

function Ensure-GitignoreEntry {
    param(
        [string]$GitignorePath,
        [string]$Entry
    )

    if (-not (Test-Path -LiteralPath $GitignorePath)) {
        Set-Content -LiteralPath $GitignorePath -Value $Entry -Encoding utf8
        Write-Host "Wrote file: $GitignorePath"
        return
    }

    $entries = Get-Content -LiteralPath $GitignorePath
    if ($entries -contains $Entry) {
        Write-Host "Skip $Entry update: already present in $GitignorePath"
        return
    }

    Add-Content -LiteralPath $GitignorePath -Value $Entry
    Write-Host "Updated .gitignore with $Entry"
}

function Resolve-ClientTargetList {
    param([string]$SelectedTargets)

    $targets = if ($SelectedTargets -eq "Both") {
        @("Copilot", "ClaudeCode")
    }
    elseif ($SelectedTargets -eq "VSCode") {
        Write-Warning "VSCode is now treated as a compatibility alias for Copilot CLI. The installer writes Copilot CLI MCP config plus project-local hooks."
        @("Copilot")
    }
    else {
        @($SelectedTargets)
    }

    if ($Channel -ne "Auto") {
        Write-Warning "-Channel is retained for compatibility but no longer changes setup behavior. Copilot CLI hooks are project-local and user-level MCP config lives in ~/.copilot/mcp-config.json."
    }

    return $targets
}

function Get-DefaultCopilotConfigPath {
    $copilotHome = if (-not [string]::IsNullOrWhiteSpace($env:COPILOT_HOME)) {
        $env:COPILOT_HOME
    }
    else {
        Join-Path $HOME ".copilot"
    }

    Ensure-Directory -Path $copilotHome
    return [System.IO.Path]::GetFullPath((Join-Path $copilotHome "mcp-config.json"))
}

function Get-DefaultClaudeUserConfigPath {
    if ([string]::IsNullOrWhiteSpace($HOME)) {
        throw "HOME is not set. Provide -ClaudeUserConfigPath explicitly."
    }

    return [System.IO.Path]::GetFullPath((Join-Path $HOME ".claude.json"))
}

function Get-McpRoot {
    param([string]$Path)

    $root = Read-JsonObject -Path $Path -DefaultRoot ([ordered]@{ mcpServers = [ordered]@{} }) -SkipBackup:$NoBackup
    if (-not $root.Contains("mcpServers") -or -not ($root["mcpServers"] -is [System.Collections.IDictionary])) {
        $root["mcpServers"] = [ordered]@{}
    }

    return $root
}

function Upsert-McpServer {
    param(
        [hashtable]$Root,
        [string]$ConfiguredServerName,
        [hashtable]$ServerDefinition,
        [switch]$ForceWrite
    )

    if ($Root["mcpServers"].Contains($ConfiguredServerName) -and -not $ForceWrite) {
        Write-Host "Skip existing '$ConfiguredServerName' in mcpServers (use -Force to overwrite)."
        return
    }

    $Root["mcpServers"][$ConfiguredServerName] = $ServerDefinition
    Write-Host "Configured '$ConfiguredServerName' in mcpServers."
}

function New-ServerDefinition {
    param(
        [string]$SelectedInstallTarget,
        [string]$ConfiguredHomeAssistantMcpUrl,
        [string]$ConfiguredImage,
        [string]$ConfiguredEnvFile,
        [string]$HostSourcePath
    )

    if ($SelectedInstallTarget -eq "HomeAssistantAddon") {
        return [ordered]@{
            type = "http"
            url = $ConfiguredHomeAssistantMcpUrl
        }
    }

    $containerArgs = @(
        "run",
        "--rm",
        "-i",
        "--pull=never",
        "--memory=2g",
        "--cpus=1.0"
    )

    $environment = [ordered]@{}
    if (-not [string]::IsNullOrWhiteSpace($HostSourcePath)) {
        $environment["NEBULARAG_PathMappings"] = "$HostSourcePath=/workspace"
        $containerArgs += @(
            "--mount",
            "type=bind,source=$HostSourcePath,target=/workspace",
            "--workdir",
            "/workspace"
        )
    }

    $containerArgs += @(
        "--env-file",
        $ConfiguredEnvFile,
        $ConfiguredImage,
        "--skip-self-test"
    )

    $server = [ordered]@{
        type = "stdio"
        command = "podman"
        args = $containerArgs
    }

    if ($environment.Count -gt 0) {
        $server["env"] = $environment
    }

    return $server
}

function Ensure-EnvTemplate {
    param(
        [string]$ConfiguredEnvPath,
        [string]$TemplateRoot,
        [string]$RawBaseUrl,
        [switch]$ForceWrite
    )

    $sourceEnvPath = Resolve-TemplateFile -TemplateRoot $TemplateRoot -RelativePath ".env" -RawBaseUrl $RawBaseUrl
    if ([string]::IsNullOrWhiteSpace($sourceEnvPath)) {
        $sourceEnvPath = Resolve-TemplateFile -TemplateRoot $TemplateRoot -RelativePath ".env.example" -RawBaseUrl $RawBaseUrl
    }

    if ([string]::IsNullOrWhiteSpace($sourceEnvPath)) {
        Write-Host "Skip env template: no source file found (.env or .env.example)."
        return
    }

    if ((Test-Path -LiteralPath $ConfiguredEnvPath) -and -not $ForceWrite) {
        Write-Host "Skip existing env file: $ConfiguredEnvPath"
        return
    }

    Ensure-Directory -Path (Split-Path -Parent $ConfiguredEnvPath)
    Copy-Item -LiteralPath $sourceEnvPath -Destination $ConfiguredEnvPath -Force
    Write-Host "Wrote env file from $sourceEnvPath to: $ConfiguredEnvPath"
}

function Merge-ClaudeSettings {
    param(
        [string]$TargetSettingsPath,
        [string]$SourceSettingsPath,
        [switch]$ForceWrite,
        [switch]$SkipBackup
    )

    $sourceRoot = Read-JsonObject -Path $SourceSettingsPath -DefaultRoot ([ordered]@{}) -SkipBackup:$SkipBackup
    $targetRoot = Read-JsonObject -Path $TargetSettingsPath -DefaultRoot ([ordered]@{}) -SkipBackup:$SkipBackup

    if (-not $targetRoot.Contains('$schema') -and $sourceRoot.Contains('$schema')) {
        $targetRoot['$schema'] = $sourceRoot['$schema']
    }

    if (-not $sourceRoot.Contains('hooks') -or -not ($sourceRoot['hooks'] -is [System.Collections.IDictionary])) {
        throw "Source Claude settings do not contain a hooks object: $SourceSettingsPath"
    }

    if ($ForceWrite -or -not $targetRoot.Contains('hooks') -or -not ($targetRoot['hooks'] -is [System.Collections.IDictionary])) {
        $targetRoot['hooks'] = [ordered]@{}
    }

    foreach ($eventName in $sourceRoot['hooks'].Keys) {
        $existingGroups = @()
        if ($targetRoot['hooks'].Contains($eventName) -and $null -ne $targetRoot['hooks'][$eventName]) {
            $existingGroups = @($targetRoot['hooks'][$eventName])
        }

        $preservedGroups = @()
        foreach ($group in $existingGroups) {
            $isNebulaGroup = $false
            if ($group -is [System.Collections.IDictionary] -and $group.Contains('hooks')) {
                foreach ($hook in @($group['hooks'])) {
                    if ($hook -is [System.Collections.IDictionary] -and ($hook.Contains('command')) -and ([string]$hook['command'] -like '*Invoke-NebulaAgentHook*')) {
                        $isNebulaGroup = $true
                        break
                    }
                }
            }

            if (-not $isNebulaGroup) {
                $preservedGroups += $group
            }
        }

        $sourceGroups = @($sourceRoot['hooks'][$eventName])
        $targetRoot['hooks'][$eventName] = @($preservedGroups + $sourceGroups)
    }

    Write-JsonObject -Path $TargetSettingsPath -Root $targetRoot -SkipBackup:$SkipBackup
}

function Setup-CopilotUser {
    param(
        [string]$ConfiguredServerName,
        [hashtable]$ServerDefinition,
        [switch]$ForceWrite,
        [switch]$SkipBackup
    )

    $configPath = if ([string]::IsNullOrWhiteSpace($CopilotConfigPath)) {
        Get-DefaultCopilotConfigPath
    }
    else {
        [System.IO.Path]::GetFullPath($CopilotConfigPath)
    }

    $root = Get-McpRoot -Path $configPath
    Upsert-McpServer -Root $root -ConfiguredServerName $ConfiguredServerName -ServerDefinition $ServerDefinition -ForceWrite:$ForceWrite
    Write-JsonObject -Path $configPath -Root $root -SkipBackup:$SkipBackup

    Write-Host ""
    Write-Host "Copilot user-level MCP setup complete."
}

function Setup-ClaudeUser {
    param(
        [string]$ConfiguredServerName,
        [hashtable]$ServerDefinition,
        [switch]$ForceWrite,
        [switch]$SkipBackup
    )

    $configPath = if ([string]::IsNullOrWhiteSpace($ClaudeUserConfigPath)) {
        Get-DefaultClaudeUserConfigPath
    }
    else {
        [System.IO.Path]::GetFullPath($ClaudeUserConfigPath)
    }

    $root = Get-McpRoot -Path $configPath
    Upsert-McpServer -Root $root -ConfiguredServerName $ConfiguredServerName -ServerDefinition $ServerDefinition -ForceWrite:$ForceWrite
    Write-JsonObject -Path $configPath -Root $root -SkipBackup:$SkipBackup

    Write-Host ""
    Write-Host "Claude user-level MCP setup complete."
}

function Setup-ClaudeProjectMcp {
    param(
        [string]$ProjectPath,
        [string]$ConfiguredServerName,
        [hashtable]$ServerDefinition,
        [switch]$ForceWrite,
        [switch]$SkipBackup
    )

    $projectRoot = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ProjectPath -ErrorAction Stop).Path)
    $configPath = if ([string]::IsNullOrWhiteSpace($ClaudeProjectConfigPath)) {
        [System.IO.Path]::GetFullPath((Join-Path $projectRoot ".mcp.json"))
    }
    else {
        [System.IO.Path]::GetFullPath($ClaudeProjectConfigPath)
    }

    $root = Get-McpRoot -Path $configPath
    Upsert-McpServer -Root $root -ConfiguredServerName $ConfiguredServerName -ServerDefinition $ServerDefinition -ForceWrite:$ForceWrite
    Write-JsonObject -Path $configPath -Root $root -SkipBackup:$SkipBackup

    Write-Host ""
    Write-Host "Claude project-level MCP setup complete: $configPath"
}

function Setup-Project {
    param(
        [string]$ProjectPath,
        [string[]]$ResolvedClientTargets,
        [switch]$ForceWrite,
        [switch]$SkipSkillFile,
        [switch]$SkipBackup,
        [string]$TemplateRoot,
        [string]$RawBaseUrl
    )

    $resolvedTargetPath = Resolve-Path -LiteralPath $ProjectPath -ErrorAction SilentlyContinue
    if (-not $resolvedTargetPath) {
        throw "Target path does not exist: $ProjectPath"
    }

    $targetRoot = $resolvedTargetPath.Path
    $targetFullPath = [System.IO.Path]::GetFullPath($targetRoot)
    $scriptDirectory = [System.IO.Path]::GetFullPath($PSScriptRoot)
    if ([System.StringComparer]::OrdinalIgnoreCase.Equals($targetFullPath, $scriptDirectory)) {
        throw "Refusing to scaffold into the scripts directory. Use -TargetPath to point at your project root."
    }

    $sharedTemplates = @(
        'AGENTS.md',
        '.github/nebula.instructions.md',
        '.github/instructions/rag.instructions.md',
        '.github/instructions/coding.instructions.md',
        '.github/instructions/documentation.instructions.md',
        '.github/nebula/hooks/Invoke-NebulaAgentHook.ps1',
        '.github/nebula/hooks/Invoke-NebulaAgentHook.sh'
    )

    foreach ($relativePath in $sharedTemplates) {
        $sourcePath = Resolve-TemplateFile -TemplateRoot $TemplateRoot -RelativePath $relativePath -RawBaseUrl $RawBaseUrl -Required
        $destinationPath = Join-Path $targetRoot $relativePath
        Copy-FileSafe -Source $sourcePath -Destination $destinationPath -ForceWrite:$ForceWrite
    }

    if ($ResolvedClientTargets -contains 'Copilot') {
        $copilotInstructionSource = Resolve-TemplateFile -TemplateRoot $TemplateRoot -RelativePath '.github/copilot-instructions.md' -RawBaseUrl $RawBaseUrl -Required
        Copy-FileSafe -Source $copilotInstructionSource -Destination (Join-Path $targetRoot '.github/copilot-instructions.md') -ForceWrite:$ForceWrite

        $copilotHooksSource = Resolve-TemplateFile -TemplateRoot $TemplateRoot -RelativePath '.github/hooks/nebula-balanced.json' -RawBaseUrl $RawBaseUrl -Required
        Copy-FileSafe -Source $copilotHooksSource -Destination (Join-Path $targetRoot '.github/hooks/nebula-balanced.json') -ForceWrite:$ForceWrite
    }

    if ($ResolvedClientTargets -contains 'ClaudeCode') {
        $claudeSettingsSource = Resolve-TemplateFile -TemplateRoot $TemplateRoot -RelativePath '.claude/settings.json' -RawBaseUrl $RawBaseUrl -Required
        $projectClaudeSettingsPath = if ([string]::IsNullOrWhiteSpace($ClaudeSettingsPath)) {
            Join-Path $targetRoot '.claude/settings.json'
        }
        else {
            [System.IO.Path]::GetFullPath($ClaudeSettingsPath)
        }

        Merge-ClaudeSettings -TargetSettingsPath $projectClaudeSettingsPath -SourceSettingsPath $claudeSettingsSource -ForceWrite:$ForceWrite -SkipBackup:$SkipBackup
    }

    if (-not $SkipSkillFile) {
        $skillSource = Resolve-TemplateFile -TemplateRoot $TemplateRoot -RelativePath '.github/skills/nebularag/SKILL.md' -RawBaseUrl $RawBaseUrl -Required
        Copy-FileSafe -Source $skillSource -Destination (Join-Path $targetRoot '.github/skills/nebularag/SKILL.md') -ForceWrite:$ForceWrite
    }

    $envExampleSource = Resolve-TemplateFile -TemplateRoot $TemplateRoot -RelativePath '.env.example' -RawBaseUrl $RawBaseUrl
    if (-not [string]::IsNullOrWhiteSpace($envExampleSource)) {
        Copy-FileSafe -Source $envExampleSource -Destination (Join-Path $targetRoot '.env.example') -ForceWrite:$ForceWrite
    }

    Ensure-GitignoreEntry -GitignorePath (Join-Path $targetRoot '.gitignore') -Entry '.env'
    Ensure-GitignoreEntry -GitignorePath (Join-Path $targetRoot '.gitignore') -Entry '.claude/settings.local.json'

    Write-Host ""
    Write-Host "Project setup complete: $targetRoot"
}

$templateRoot = Split-Path -Parent $PSScriptRoot
$resolvedInstallTarget = Resolve-InstallTarget -SelectedInstallTarget $InstallTarget
$resolvedHomeAssistantMcpUrl = Resolve-HomeAssistantMcpUrl -LocalUrl $HomeAssistantMcpUrl -ExternalUrl $ExternalHomeAssistantMcpUrl -PreferExternalUrl:$UseExternalHomeAssistantUrl -ExternalConsent:$ForceExternal
$resolvedClientTargets = Resolve-ClientTargetList -SelectedTargets $ClientTargets

if ([string]::IsNullOrWhiteSpace($EnvFilePath)) {
    $EnvFilePath = Join-Path $HOME '.nebula-rag/.env'
}

if ($resolvedInstallTarget -eq 'LocalContainer') {
    if (-not (Get-Command podman -ErrorAction SilentlyContinue)) {
        Write-Warning "podman was not found in PATH. LocalContainer registrations will still be written, but they will not run until podman is installed."
    }
}

if ($Mode -in @('Both', 'User')) {
    if ($resolvedClientTargets -contains 'Copilot') {
        $copilotServerDefinition = New-ServerDefinition -SelectedInstallTarget $resolvedInstallTarget -ConfiguredHomeAssistantMcpUrl $resolvedHomeAssistantMcpUrl -ConfiguredImage $ImageName -ConfiguredEnvFile $EnvFilePath -HostSourcePath ''
        Setup-CopilotUser -ConfiguredServerName $ServerName -ServerDefinition $copilotServerDefinition -ForceWrite:$Force -SkipBackup:$NoBackup
    }

    if ($resolvedClientTargets -contains 'ClaudeCode') {
        $claudeUserServerDefinition = New-ServerDefinition -SelectedInstallTarget $resolvedInstallTarget -ConfiguredHomeAssistantMcpUrl $resolvedHomeAssistantMcpUrl -ConfiguredImage $ImageName -ConfiguredEnvFile $EnvFilePath -HostSourcePath ''
        Setup-ClaudeUser -ConfiguredServerName $ServerName -ServerDefinition $claudeUserServerDefinition -ForceWrite:$Force -SkipBackup:$NoBackup
    }

    if ($CreateEnvTemplate -and $resolvedInstallTarget -eq 'LocalContainer') {
        Ensure-EnvTemplate -ConfiguredEnvPath $EnvFilePath -TemplateRoot $templateRoot -RawBaseUrl $TemplateRawBaseUrl -ForceWrite:$Force
    }
}

if ($Mode -in @('Both', 'Project')) {
    if ([string]::IsNullOrWhiteSpace($TargetPath)) {
        if ($Mode -eq 'Both') {
            $TargetPath = $templateRoot
        }
        else {
            throw '-TargetPath is required when -Mode Project.'
        }
    }

    Setup-Project -ProjectPath $TargetPath -ResolvedClientTargets $resolvedClientTargets -ForceWrite:$Force -SkipSkillFile:$SkipSkill -SkipBackup:$NoBackup -TemplateRoot $templateRoot -RawBaseUrl $TemplateRawBaseUrl

    if ($resolvedClientTargets -contains 'ClaudeCode') {
        $claudeProjectServerDefinition = New-ServerDefinition -SelectedInstallTarget $resolvedInstallTarget -ConfiguredHomeAssistantMcpUrl $resolvedHomeAssistantMcpUrl -ConfiguredImage $ImageName -ConfiguredEnvFile $EnvFilePath -HostSourcePath '${PWD}'
        Setup-ClaudeProjectMcp -ProjectPath $TargetPath -ConfiguredServerName $ServerName -ServerDefinition $claudeProjectServerDefinition -ForceWrite:$Force -SkipBackup:$NoBackup
    }
}

Write-Host ''
Write-Host 'NebulaRAG setup finished.'
Write-Host "Clients: $($resolvedClientTargets -join ', ')"
Write-Host "Install target: $resolvedInstallTarget"
Write-Host "Server: $ServerName"
if ($resolvedInstallTarget -eq 'HomeAssistantAddon') {
    Write-Host "MCP URL: $resolvedHomeAssistantMcpUrl"
}
else {
    Write-Host "Image: $ImageName"
    Write-Host "Env file: $EnvFilePath"
    Write-Host 'Note: Copilot CLI MCP registration is user-level. Project-local hooks are scaffolded into .github/hooks and Claude project MCP config is written to .mcp.json.'
}
