Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Test-OpenPencilRepoMarker {
    param([string]$CandidatePath)

    return (Test-Path (Join-Path $CandidatePath "repository.json")) -or
        (Test-Path (Join-Path $CandidatePath "NebulaRAG.slnx")) -or
        (Test-Path (Join-Path $CandidatePath ".github\skills\openpencil-design\SKILL.md"))
}

function Get-OpenPencilRepoRoot {
    param([string]$ScriptPath)

    $candidatePath = Split-Path -Parent $ScriptPath

    while (-not [string]::IsNullOrWhiteSpace($candidatePath)) {
        if (Test-OpenPencilRepoMarker -CandidatePath $candidatePath) {
            return (Resolve-Path $candidatePath).Path
        }

        $parentPath = Split-Path -Parent $candidatePath
        if ([string]::IsNullOrWhiteSpace($parentPath) -or $parentPath -eq $candidatePath) {
            break
        }

        $candidatePath = $parentPath
    }

    throw "Could not resolve the NebulaRAG repository root from $ScriptPath"
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
    }
}

function Get-OpenPencilExpectedFigEntries {
    return @(
        "canvas.fig",
        "thumbnail.png",
        "meta.json"
    )
}

function Test-OpenPencilFigArchive {
    param([string]$VariantPath)

    $requiredEntries = Get-OpenPencilExpectedFigEntries
    if ([string]::IsNullOrWhiteSpace($VariantPath) -or -not (Test-Path $VariantPath)) {
        return [pscustomobject]@{
            IsValid = $false
            VariantPath = $VariantPath
            Entries = @()
            MissingEntries = $requiredEntries
            Error = "Variant path does not exist."
        }
    }

    $resolvedVariantPath = (Resolve-Path $VariantPath).Path
    $zipArchive = $null

    try {
        $zipArchive = [System.IO.Compression.ZipFile]::OpenRead($resolvedVariantPath)
        $entries = @($zipArchive.Entries | ForEach-Object { $_.FullName })
        $missingEntries = @($requiredEntries | Where-Object { $_ -notin $entries })

        return [pscustomobject]@{
            IsValid = $missingEntries.Count -eq 0
            VariantPath = $resolvedVariantPath
            Entries = $entries
            MissingEntries = $missingEntries
            Error = $null
        }
    }
    catch {
        return [pscustomobject]@{
            IsValid = $false
            VariantPath = $resolvedVariantPath
            Entries = @()
            MissingEntries = $requiredEntries
            Error = $_.Exception.Message
        }
    }
    finally {
        if ($null -ne $zipArchive) {
            $zipArchive.Dispose()
        }
    }
}

function Get-OpenPencilBrowserAutomationScriptPath {
    param([string]$ScriptPath)

    $repoRoot = Get-OpenPencilRepoRoot -ScriptPath $ScriptPath
    return Join-Path $repoRoot ".github\skills\openpencil-design\scripts\openpencil-browser-automation.js"
}