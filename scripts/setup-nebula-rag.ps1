#!/usr/bin/env pwsh

[CmdletBinding()]
param(
    [ValidateSet("Both", "User", "Project")]
    [string]$Mode = "Both",

    [string]$TargetPath,

    [ValidateSet("Auto", "Code", "Insiders", "Both")]
    [string]$Channel = "Auto",
    [string]$UserConfigPath,

    [string]$ServerName = "nebula-rag",
    [string]$ImageName = "localhost/nebula-rag-mcp:latest",
    [string]$HomeAssistantMcpUrl = "http://homeassistant.local:8099/nebula/mcp",
    [string]$ExternalHomeAssistantMcpUrl,
    [switch]$UseExternalHomeAssistantUrl,

    [string]$EnvFileName = ".nebula.env",
    [string]$EnvFilePath,

    [ValidateSet("Ask", "LocalContainer", "HomeAssistantAddon")]
    [string]$InstallTarget = "Ask",

    [switch]$CreateEnvTemplate,
    [switch]$SkipSkill,
    [switch]$NoBackup,
    [switch]$ForceExternal,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

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

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function Write-TextFile {
    param(
        [string]$Path,
        [string]$Content,
        [switch]$ForceWrite
    )

    if ((Test-Path -LiteralPath $Path) -and -not $ForceWrite) {
        Write-Host "Skip existing file: $Path"
        return
    }

    Ensure-Directory -Path (Split-Path -Parent $Path)
    Set-Content -LiteralPath $Path -Value $Content -Encoding utf8
    Write-Host "Wrote file: $Path"
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

    if ((Test-Path -LiteralPath $Destination) -and -not $ForceWrite) {
        Write-Host "Skip existing file: $Destination"
        return
    }

    Ensure-Directory -Path (Split-Path -Parent $Destination)
    Copy-Item -LiteralPath $Source -Destination $Destination -Force -ErrorAction Stop
    Write-Host "Copied file: $Destination"
}

function New-ServerDefinition {
    param(
        [string]$ConfiguredImage,
        [string]$ConfiguredEnvFile,
        [string]$WorkspaceFolderScope,
        [string]$SelectedInstallTarget,
        [string]$ConfiguredHomeAssistantMcpUrl
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

    $envMap = [ordered]@{}

    if (-not [string]::IsNullOrWhiteSpace($WorkspaceFolderScope)) {
        $envMap["NEBULARAG_PathMappings"] = "${workspaceFolder:$WorkspaceFolderScope}=/workspace"
        $containerArgs += @(
            "--mount",
            "type=bind,source=${workspaceFolder:$WorkspaceFolderScope},target=/workspace",
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

    if ($envMap.Count -gt 0) {
        $server["env"] = $envMap
    }

    return $server
}

function Build-McpConfigJson {
    param(
        [string]$ConfiguredServerName,
        [string]$ConfiguredImageName,
        [string]$ConfiguredEnvFile,
        [string]$WorkspaceFolderScope,
        [string]$SelectedInstallTarget,
        [string]$ConfiguredHomeAssistantMcpUrl
    )

    $server = New-ServerDefinition -ConfiguredImage $ConfiguredImageName -ConfiguredEnvFile $ConfiguredEnvFile -WorkspaceFolderScope $WorkspaceFolderScope -SelectedInstallTarget $SelectedInstallTarget -ConfiguredHomeAssistantMcpUrl $ConfiguredHomeAssistantMcpUrl
    $root = [ordered]@{
        mcpServers = [ordered]@{ $ConfiguredServerName = $server }
        servers = [ordered]@{ $ConfiguredServerName = $server }
    }

    return ($root | ConvertTo-Json -Depth 10)
}

function Ensure-GitignoreEnv {
    param([string]$GitignorePath)

    $envFileName = ".nebula.env"

    if (-not (Test-Path -LiteralPath $GitignorePath)) {
        Set-Content -LiteralPath $GitignorePath -Value $envFileName -Encoding utf8
        Write-Host "Wrote file: $GitignorePath"
        return
    }

    $entries = Get-Content -LiteralPath $GitignorePath
    if ($entries -contains $envFileName) {
        Write-Host "Skip $envFileName update: already present in $GitignorePath"
        return
    }

    Add-Content -LiteralPath $GitignorePath -Value $envFileName
    Write-Host "Updated .gitignore with $envFileName"
}

function Get-DefaultUserConfigPath {
    param([string]$SelectedChannel)

    $appData = $env:APPDATA
    if ([string]::IsNullOrWhiteSpace($appData)) {
        throw "APPDATA is not set. Provide -UserConfigPath explicitly."
    }

    $codePath = Join-Path $appData "Code/User/mcp.json"
    $insidersPath = Join-Path $appData "Code - Insiders/User/mcp.json"

    if ($SelectedChannel -eq "Code") { return @($codePath) }
    if ($SelectedChannel -eq "Insiders") { return @($insidersPath) }
    if ($SelectedChannel -eq "Both") { return @($codePath, $insidersPath) }

    if (Test-Path -LiteralPath $insidersPath) {
        return @($insidersPath)
    }

    return @($codePath)
}

function Get-OrCreateRootObject {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return [ordered]@{
            mcpServers = [ordered]@{}
            servers = [ordered]@{}
        }
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return [ordered]@{
            mcpServers = [ordered]@{}
            servers = [ordered]@{}
        }
    }

    function New-EmptyRoot {
        return [ordered]@{
            mcpServers = [ordered]@{}
            servers = [ordered]@{}
        }
    }

    function Try-ParseJsonLike {
        param([string]$JsonText)

        try {
            return $JsonText | ConvertFrom-Json -AsHashtable
        }
        catch {
            # Best-effort JSONC cleanup: strip block/line comments and trailing commas.
            $sanitized = $JsonText -replace '(?s)/\*.*?\*/', ''
            $sanitized = $sanitized -replace '(?m)^\s*//.*$', ''
            $sanitized = $sanitized -replace '(?m)([^:])//.*$', '$1'
            $sanitized = $sanitized -replace ',(\s*[}\]])', '$1'

            try {
                return $sanitized | ConvertFrom-Json -AsHashtable
            }
            catch {
                return $null
            }
        }
    }

    $parsed = Try-ParseJsonLike -JsonText $raw
    if ($null -eq $parsed) {
        $timestamp = Get-Date -Format "yyyyMMddHHmmss"
        $backupPath = "$Path.invalid.$timestamp.bak"
        Copy-Item -LiteralPath $Path -Destination $backupPath -Force
        Write-Warning "Invalid JSON in user MCP config: $Path"
        Write-Warning "Backed up invalid file to: $backupPath"
        Write-Warning "Continuing with a fresh MCP config root for this path."
        return New-EmptyRoot
    }

    if (-not ($parsed -is [System.Collections.IDictionary])) {
        $parsed = [ordered]@{}
    }

    if (-not $parsed.Contains("mcpServers") -or $null -eq $parsed["mcpServers"]) {
        $parsed["mcpServers"] = [ordered]@{}
    }

    if (-not $parsed.Contains("servers") -or $null -eq $parsed["servers"]) {
        $parsed["servers"] = [ordered]@{}
    }

    return $parsed
}

function Upsert-ServerConfig {
    param(
        [hashtable]$Root,
        [string]$ConfiguredServerName,
        [hashtable]$ServerDefinition,
        [switch]$ForceWrite
    )

    foreach ($key in @("mcpServers", "servers")) {
        if (-not ($Root[$key] -is [System.Collections.IDictionary])) {
            $Root[$key] = [ordered]@{}
        }

        if ($Root[$key].Contains($ConfiguredServerName) -and -not $ForceWrite) {
            Write-Host "Skip existing '$ConfiguredServerName' in $key (use -Force to overwrite)."
            continue
        }

        $Root[$key][$ConfiguredServerName] = $ServerDefinition
        Write-Host "Configured '$ConfiguredServerName' in $key."
    }
}

function Write-RootObject {
    param(
        [string]$Path,
        [hashtable]$Root,
        [switch]$SkipBackup
    )

    Ensure-Directory -Path (Split-Path -Parent $Path)

    if ((Test-Path -LiteralPath $Path) -and -not $SkipBackup) {
        Copy-Item -LiteralPath $Path -Destination "$Path.bak" -Force
        Write-Host "Backed up existing config to: $Path.bak"
    }

    $json = $Root | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $Path -Value $json -Encoding utf8
    Write-Host "Wrote user MCP config: $Path"
}

function Ensure-EnvTemplate {
    param(
        [string]$ConfiguredEnvPath,
        [string]$TemplateRoot,
        [switch]$ForceWrite
    )

    $rootEnvPath = Join-Path $TemplateRoot ".nebula.env"
    $exampleEnvPath = Join-Path $TemplateRoot ".env.example"

    $sourceEnvPath = $null
    if (Test-Path -LiteralPath $rootEnvPath) {
        # Prefer exact local runtime values when available.
        $sourceEnvPath = $rootEnvPath
    }
    elseif (Test-Path -LiteralPath $exampleEnvPath) {
        $sourceEnvPath = $exampleEnvPath
    }

    if ([string]::IsNullOrWhiteSpace($sourceEnvPath)) {
        Write-Host "Skip env template: no source file found (.nebula.env or .env.example)."
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

function Ensure-GlobalAgentsGuide {
    param(
        [string]$TemplateRoot,
        [switch]$ForceWrite
    )

    $sourceAgentsPath = Join-Path $TemplateRoot "AGENTS.md"
    if (-not (Test-Path -LiteralPath $sourceAgentsPath)) {
        Write-Host "Skip global AGENTS setup: source file not found at $sourceAgentsPath"
        return
    }

    $homeDirectory = $HOME
    if ([string]::IsNullOrWhiteSpace($homeDirectory)) {
        Write-Host "Skip global AGENTS setup: HOME is not set."
        return
    }

    $globalAgentsPath = Join-Path $homeDirectory "AGENTS.md"
    Copy-FileSafe -Source $sourceAgentsPath -Destination $globalAgentsPath -ForceWrite:$ForceWrite
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

function Setup-Project {
    param(
        [string]$ProjectPath,
        [switch]$ForceWrite,
        [switch]$SkipSkillFile,
        [string]$TemplateRoot
    )

    $resolvedTargetPath = Resolve-Path -LiteralPath $ProjectPath -ErrorAction SilentlyContinue
    if (-not $resolvedTargetPath) {
        throw "Target path does not exist: $ProjectPath"
    }

    $targetRoot = $resolvedTargetPath.Path
    $scriptDirectory = [System.IO.Path]::GetFullPath($PSScriptRoot)
    $targetFullPath = [System.IO.Path]::GetFullPath($targetRoot)

    if ([System.StringComparer]::OrdinalIgnoreCase.Equals($targetFullPath, $scriptDirectory)) {
        throw "Refusing to scaffold into the scripts directory. Use -TargetPath to point at your project root."
    }

    Copy-FileSafe -Source (Join-Path $TemplateRoot ".github/copilot-instructions.md") -Destination (Join-Path $targetRoot ".github/copilot-instructions.md") -ForceWrite:$ForceWrite
    Copy-FileSafe -Source (Join-Path $TemplateRoot ".github/instructions/rag.instructions.md") -Destination (Join-Path $targetRoot ".github/instructions/rag.instructions.md") -ForceWrite:$ForceWrite
    Copy-FileSafe -Source (Join-Path $TemplateRoot "AGENTS.md") -Destination (Join-Path $targetRoot "AGENTS.md") -ForceWrite:$ForceWrite

    if (-not $SkipSkillFile) {
        Copy-FileSafe -Source (Join-Path $TemplateRoot ".github/skills/nebularag/SKILL.md") -Destination (Join-Path $targetRoot ".github/skills/nebularag/SKILL.md") -ForceWrite:$ForceWrite
    }

    $sourceEnvExample = Join-Path $TemplateRoot ".env.example"
    if (Test-Path -LiteralPath $sourceEnvExample) {
        Copy-FileSafe -Source $sourceEnvExample -Destination (Join-Path $targetRoot ".env.example") -ForceWrite:$ForceWrite
    }

    Ensure-GitignoreEnv -GitignorePath (Join-Path $targetRoot ".gitignore")

    Write-Host ""
    Write-Host "Project setup complete: $targetRoot"
}

function Setup-User {
    param(
        [string]$SelectedChannel,
        [string]$ExplicitUserConfigPath,
        [string]$ConfiguredServerName,
        [string]$ConfiguredImageName,
        [string]$ConfiguredEnvFilePath,
        [string]$SelectedInstallTarget,
        [string]$ConfiguredHomeAssistantMcpUrl,
        [switch]$WriteEnvTemplate,
        [switch]$ForceWrite,
        [switch]$SkipBackup,
        [string]$TemplateRoot
    )

    $configPaths = @()
    if (-not [string]::IsNullOrWhiteSpace($ExplicitUserConfigPath)) {
        $configPaths = @($ExplicitUserConfigPath)
    }
    else {
        $configPaths = Get-DefaultUserConfigPath -SelectedChannel $SelectedChannel
    }

    # User-level config is workspace-agnostic; omit workspace mount to avoid multi-root variable resolution failures.
    $serverDefinition = New-ServerDefinition -ConfiguredImage $ConfiguredImageName -ConfiguredEnvFile $ConfiguredEnvFilePath -WorkspaceFolderScope "" -SelectedInstallTarget $SelectedInstallTarget -ConfiguredHomeAssistantMcpUrl $ConfiguredHomeAssistantMcpUrl

    foreach ($configPath in $configPaths) {
        $resolvedConfigPath = [System.IO.Path]::GetFullPath($configPath)
        $root = Get-OrCreateRootObject -Path $resolvedConfigPath
        Upsert-ServerConfig -Root $root -ConfiguredServerName $ConfiguredServerName -ServerDefinition $serverDefinition -ForceWrite:$ForceWrite
        Write-RootObject -Path $resolvedConfigPath -Root $root -SkipBackup:$SkipBackup
    }

    if ($WriteEnvTemplate) {
        Ensure-EnvTemplate -ConfiguredEnvPath $ConfiguredEnvFilePath -TemplateRoot $TemplateRoot -ForceWrite:$ForceWrite
    }

    Write-Host ""
    Write-Host "User-level setup complete."
}

$templateRoot = Split-Path -Parent $PSScriptRoot
$resolvedInstallTarget = Resolve-InstallTarget -SelectedInstallTarget $InstallTarget
$resolvedHomeAssistantMcpUrl = Resolve-HomeAssistantMcpUrl -LocalUrl $HomeAssistantMcpUrl -ExternalUrl $ExternalHomeAssistantMcpUrl -PreferExternalUrl:$UseExternalHomeAssistantUrl -ExternalConsent:$ForceExternal

if ([string]::IsNullOrWhiteSpace($EnvFilePath)) {
    $EnvFilePath = Join-Path $HOME ".nebula-rag/.nebula.env"
}

if ($Mode -in @("Both", "User")) {
    Setup-User -SelectedChannel $Channel -ExplicitUserConfigPath $UserConfigPath -ConfiguredServerName $ServerName -ConfiguredImageName $ImageName -ConfiguredEnvFilePath $EnvFilePath -SelectedInstallTarget $resolvedInstallTarget -ConfiguredHomeAssistantMcpUrl $resolvedHomeAssistantMcpUrl -WriteEnvTemplate:($CreateEnvTemplate -and $resolvedInstallTarget -eq "LocalContainer") -ForceWrite:$Force -SkipBackup:$NoBackup -TemplateRoot $templateRoot
    Ensure-GlobalAgentsGuide -TemplateRoot $templateRoot -ForceWrite:$Force
}

if ($Mode -in @("Both", "Project")) {
    if ([string]::IsNullOrWhiteSpace($TargetPath)) {
        if ($Mode -eq "Both") {
            $TargetPath = $templateRoot
        }
        else {
            throw "-TargetPath is required when -Mode Project."
        }
    }

    Setup-Project -ProjectPath $TargetPath -ForceWrite:$Force -SkipSkillFile:$SkipSkill -TemplateRoot $templateRoot
}

Write-Host ""
Write-Host "NebulaRAG setup finished."
Write-Host "Install target: $resolvedInstallTarget"
Write-Host "Server: $ServerName"
Write-Host "Image:  $ImageName"
if ($resolvedInstallTarget -eq "HomeAssistantAddon") {
    Write-Host "MCP URL: $resolvedHomeAssistantMcpUrl"
    if ($UseExternalHomeAssistantUrl) {
        Write-Host "URL mode: External"
    }
    else {
        Write-Host "URL mode: Local network"
    }
}
