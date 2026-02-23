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

    [string]$EnvFileName = ".nebula.env",
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
        [string]$WorkspaceFolderScope
    )

    $args = @(
        "run",
        "--rm",
        "-i",
        "--pull=never",
        "--memory=2g",
        "--cpus=1.0"
    )

    if (-not [string]::IsNullOrWhiteSpace($WorkspaceFolderScope)) {
        $args += @(
            "--mount",
            "type=bind,source=${workspaceFolder:$WorkspaceFolderScope},target=/workspace",
            "--workdir",
            "/workspace"
        )
    }

    $args += @(
        "--env-file",
        $ConfiguredEnvFile,
        $ConfiguredImage,
        "--skip-self-test"
    )

    return [ordered]@{
        type = "stdio"
        command = "podman"
        args = $args
    }
}

function Build-McpConfigJson {
    param(
        [string]$ConfiguredServerName,
        [string]$ConfiguredImageName,
        [string]$ConfiguredEnvFile,
        [string]$WorkspaceFolderScope
    )

    $server = New-ServerDefinition -ConfiguredImage $ConfiguredImageName -ConfiguredEnvFile $ConfiguredEnvFile -WorkspaceFolderScope $WorkspaceFolderScope
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
        [string]$TemplatePath,
        [switch]$ForceWrite
    )

    if (-not (Test-Path -LiteralPath $TemplatePath)) {
        Write-Host "Skip env template: source .env.example not found in this repo."
        return
    }

    if ((Test-Path -LiteralPath $ConfiguredEnvPath) -and -not $ForceWrite) {
        Write-Host "Skip existing env file: $ConfiguredEnvPath"
        return
    }

    Ensure-Directory -Path (Split-Path -Parent $ConfiguredEnvPath)
    Copy-Item -LiteralPath $TemplatePath -Destination $ConfiguredEnvPath -Force
    Write-Host "Wrote env template: $ConfiguredEnvPath"
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
    $serverDefinition = New-ServerDefinition -ConfiguredImage $ConfiguredImageName -ConfiguredEnvFile $ConfiguredEnvFilePath -WorkspaceFolderScope ""

    foreach ($configPath in $configPaths) {
        $resolvedConfigPath = [System.IO.Path]::GetFullPath($configPath)
        $root = Get-OrCreateRootObject -Path $resolvedConfigPath
        Upsert-ServerConfig -Root $root -ConfiguredServerName $ConfiguredServerName -ServerDefinition $serverDefinition -ForceWrite:$ForceWrite
        Write-RootObject -Path $resolvedConfigPath -Root $root -SkipBackup:$SkipBackup
    }

    if ($WriteEnvTemplate) {
        Ensure-EnvTemplate -ConfiguredEnvPath $ConfiguredEnvFilePath -TemplatePath (Join-Path $TemplateRoot ".env.example") -ForceWrite:$ForceWrite
    }

    Write-Host ""
    Write-Host "User-level setup complete."
}

$templateRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($EnvFilePath)) {
    $EnvFilePath = Join-Path $HOME ".nebula-rag/.nebula.env"
}

if ($Mode -in @("Both", "User")) {
    Setup-User -SelectedChannel $Channel -ExplicitUserConfigPath $UserConfigPath -ConfiguredServerName $ServerName -ConfiguredImageName $ImageName -ConfiguredEnvFilePath $EnvFilePath -WriteEnvTemplate:$CreateEnvTemplate -ForceWrite:$Force -SkipBackup:$NoBackup -TemplateRoot $templateRoot
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
Write-Host "Server: $ServerName"
Write-Host "Image:  $ImageName"
