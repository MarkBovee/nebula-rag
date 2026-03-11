Set-StrictMode -Version Latest

function Test-OpenPencilMcpCommandLine {
    param([string]$CommandLine)

    if ([string]::IsNullOrWhiteSpace($CommandLine)) {
        return $false
    }

    return $CommandLine -match "openpencil-mcp-http" -or
        $CommandLine -match "@open-pencil[\\/]mcp[\\/]dist[\\/]http\.js"
}

function ConvertTo-OpenPencilBoolean {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    switch ($Value.Trim().ToLowerInvariant()) {
        "1" { return $true }
        "true" { return $true }
        "yes" { return $true }
        "on" { return $true }
        default { return $false }
    }
}

function Get-OpenPencilRepoRoot {
    param([string]$ScriptPath)

    return (Resolve-Path (Join-Path (Split-Path -Parent $ScriptPath) "..\..")).Path
}

function Get-OpenPencilContainerName {
    return "openpencil-mcp-http"
}

function Get-OpenPencilSettings {
    param([string]$ScriptPath)

    $repoRoot = Get-OpenPencilRepoRoot -ScriptPath $ScriptPath
    $envFilePath = Join-Path $repoRoot ".env"
    $values = @{}

    if (Test-Path $envFilePath) {
        foreach ($line in Get-Content -Path $envFilePath) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            $trimmedLine = $line.Trim()
            if ($trimmedLine.StartsWith("#")) {
                continue
            }

            $separatorIndex = $trimmedLine.IndexOf("=")
            if ($separatorIndex -lt 1) {
                continue
            }

            $key = $trimmedLine.Substring(0, $separatorIndex).Trim()
            $rawValue = $trimmedLine.Substring($separatorIndex + 1).Trim()
            $normalizedValue = $rawValue.Trim('"').Trim("'")
            $values[$key] = $normalizedValue
        }
    }

    return [pscustomobject]@{
        RepoRoot = $repoRoot
        EnvFilePath = $envFilePath
        EditorUrl = $values["OPENPENCIL_EDITOR_URL"]
        UsePodman = ConvertTo-OpenPencilBoolean -Value $values["OPENPENCIL_USE_PODMAN"]
        PodmanImage = if ([string]::IsNullOrWhiteSpace($values["OPENPENCIL_MCP_PODMAN_IMAGE"])) { "nebula-openpencil-mcp:latest" } else { $values["OPENPENCIL_MCP_PODMAN_IMAGE"] }
    }
}

function Get-OpenPencilMcpLocalProcesses {
    if ($IsWindows) {
        return @(Get-CimInstance Win32_Process | Where-Object {
            $_.Name -match "node|bun|openpencil" -and (Test-OpenPencilMcpCommandLine -CommandLine $_.CommandLine)
        } | ForEach-Object {
            [pscustomobject]@{
                ProcessId = $_.ProcessId
                CommandLine = $_.CommandLine
            }
        })
    }

    $processList = & ps -eo pid=,command= 2>$null
    if ($LASTEXITCODE -ne 0 -or $null -eq $processList) {
        return @()
    }

    $matchingProcesses = @()
    foreach ($processLine in $processList) {
        if ($processLine -notmatch "^\s*(\d+)\s+(.*)$") {
            continue
        }

        $processId = [int]$Matches[1]
        $commandLine = $Matches[2]
        if (-not (Test-OpenPencilMcpCommandLine -CommandLine $commandLine)) {
            continue
        }

        $matchingProcesses += [pscustomobject]@{
            ProcessId = $processId
            CommandLine = $commandLine
        }
    }

    return $matchingProcesses
}

function Get-OpenPencilMcpContainerId {
    if ($null -eq (Get-Command podman -ErrorAction SilentlyContinue)) {
        return $null
    }

    $containerName = Get-OpenPencilContainerName
    $containerId = (& podman ps --filter "name=$containerName" --format "{{.ID}}" 2>$null | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
        return $null
    }

    return $containerId.Trim()
}