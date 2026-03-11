param(
    [string]$VariantsRoot = "designs",
    [int]$PollSeconds = 2,
    [switch]$Watch,
    [string]$EditorUrl,
    [string]$McpUrl,
    [switch]$SkipArchiveValidation,
    [switch]$NoReopenOnChange
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "openpencil-common.ps1")

function Write-Step {
    param([string]$Message)
    Write-Host "[OpenPencil Live] $Message" -ForegroundColor Cyan
}

function Get-LatestVariant {
    param([string]$RootPath)

    if (-not (Test-Path $RootPath)) {
        return $null
    }

    return Get-ChildItem -Path $RootPath -File -Filter *.fig |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Open-EditorUrl {
    param([string]$Url)

    if ([string]::IsNullOrWhiteSpace($Url)) {
        return
    }

    Write-Step "Opening browser editor: $Url"
    Start-Process $Url
}

function Get-VariantDocumentUrl {
    param([System.IO.FileInfo]$Variant)

    if ($null -eq $Variant) {
        return $null
    }

    $repoRoot = Get-OpenPencilRepoRoot -ScriptPath $PSCommandPath
    $mcpFileUrl = Get-OpenPencilFileUrl -McpUrl $McpUrl -RepoRoot $repoRoot -VariantPath $Variant.FullName
    if (-not [string]::IsNullOrWhiteSpace($mcpFileUrl)) {
        return $mcpFileUrl
    }

    $publicVariantPath = Sync-VariantToEditorPublic -Variant $Variant
    if (-not [string]::IsNullOrWhiteSpace($publicVariantPath)) {
        return '/' + $Variant.Name
    }

    return $null
}

function Get-OpenPencilPublicRoot {
    $publicRoot = Join-Path (Get-OpenPencilRepoRoot -ScriptPath $PSCommandPath) "..\open-pencil\public"
    if (-not (Test-Path $publicRoot)) {
        return $null
    }

    return (Resolve-Path $publicRoot).Path
}

function Sync-VariantToEditorPublic {
    param([System.IO.FileInfo]$Variant)

    if ($null -eq $Variant) {
        return $null
    }

    $publicRoot = Get-OpenPencilPublicRoot
    if ([string]::IsNullOrWhiteSpace($publicRoot)) {
        return $null
    }

    $publicPath = Join-Path $publicRoot $Variant.Name
    Copy-Item -LiteralPath $Variant.FullName -Destination $publicPath -Force
    return $publicPath
}

function Test-VariantReadiness {
    param([System.IO.FileInfo]$Variant)

    if ($null -eq $Variant) {
        return $false
    }

    if ($SkipArchiveValidation) {
        return $true
    }

    $validation = Test-OpenPencilFigArchive -VariantPath $Variant.FullName
    if ($validation.IsValid) {
        Write-Step "Validated .fig archive: $($validation.VariantPath)"
        return $true
    }

    $missingEntriesText = if ($validation.MissingEntries.Count -gt 0) {
        $validation.MissingEntries -join ', '
    }
    else {
        'none'
    }

    Write-Warning "Skipping variant because the .fig archive is invalid: $($validation.VariantPath). Missing entries: $missingEntriesText. Error: $($validation.Error)"
    return $false
}

function Get-EditorUrlForVariant {
    param(
        [string]$BaseUrl,
        [string]$DocumentUrl
    )

    if ([string]::IsNullOrWhiteSpace($BaseUrl) -or [string]::IsNullOrWhiteSpace($DocumentUrl)) {
        return $BaseUrl
    }

    $builder = [System.UriBuilder]::new($BaseUrl)
    $queryPairs = @()
    if (-not [string]::IsNullOrWhiteSpace($builder.Query)) {
        $queryPairs += $builder.Query.TrimStart('?').Split('&') | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and $_ -notmatch '^(open|fit)='
        }
    }

    $queryPairs += "open=$([System.Uri]::EscapeDataString($DocumentUrl))"
    $queryPairs += 'fit=1'
    $builder.Query = [string]::Join('&', $queryPairs)
    return $builder.Uri.AbsoluteUri
}

if (-not $PSBoundParameters.ContainsKey("EditorUrl")) {
    $settings = Get-OpenPencilSettings -ScriptPath $PSCommandPath
    $EditorUrl = $settings.EditorUrl
    if (-not $PSBoundParameters.ContainsKey("McpUrl")) {
        $McpUrl = $settings.McpUrl
    }
}

$resolvedVariantsRoot = if (Test-Path $VariantsRoot) { (Resolve-Path $VariantsRoot).Path } else { $VariantsRoot }

Write-Step "Watching variants root: $resolvedVariantsRoot"

$initialVariant = Get-LatestVariant -RootPath $resolvedVariantsRoot
if ((Test-VariantReadiness -Variant $initialVariant)) {
    $initialDocumentUrl = Get-VariantDocumentUrl -Variant $initialVariant
    if (-not [string]::IsNullOrWhiteSpace($initialDocumentUrl)) {
        $EditorUrl = Get-EditorUrlForVariant -BaseUrl $EditorUrl -DocumentUrl $initialDocumentUrl
    }
}

if (-not [string]::IsNullOrWhiteSpace($EditorUrl)) {
    Open-EditorUrl -Url $EditorUrl
}

$lastFingerprint = $null

while ($true) {
    $latestVariant = Get-LatestVariant -RootPath $resolvedVariantsRoot

    if ($null -eq $latestVariant) {
        Write-Step "No .fig files found yet under $resolvedVariantsRoot"
    }
    else {
        $fingerprint = "{0}|{1}|{2}" -f $latestVariant.FullName, $latestVariant.LastWriteTimeUtc.Ticks, $latestVariant.Length
        if ($fingerprint -ne $lastFingerprint) {
            if (-not (Test-VariantReadiness -Variant $latestVariant)) {
                Start-Sleep -Seconds $PollSeconds
                continue
            }

            $resolvedVariantPath = (Resolve-Path $latestVariant.FullName).Path
            $documentUrl = Get-VariantDocumentUrl -Variant $latestVariant
            Write-Step "Latest variant updated: $resolvedVariantPath"
            if (-not [string]::IsNullOrWhiteSpace($documentUrl)) {
                Write-Step "Prepared latest variant for editor reload: $($latestVariant.Name)"
            }

            if (-not [string]::IsNullOrWhiteSpace($EditorUrl) -and -not $NoReopenOnChange) {
                $latestEditorUrl = Get-EditorUrlForVariant -BaseUrl $EditorUrl -DocumentUrl $documentUrl
                Write-Step "Reopening editor on the latest validated variant"
                Open-EditorUrl -Url $latestEditorUrl
            }

            $lastFingerprint = $fingerprint
        }
    }

    if (-not $Watch) {
        break
    }

    Start-Sleep -Seconds $PollSeconds
}

if ($Watch) {
    Write-Step "Watch mode continues until this terminal is stopped (Ctrl+C)."
}