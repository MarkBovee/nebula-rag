param(
    [string]$VariantsRoot = "designs/openpencil",
    [int]$PollSeconds = 2,
    [switch]$Watch,
    [switch]$StartMcp,
    [int]$McpPort = 3100,
    [string]$EditorUrl
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

if ($StartMcp) {
    $startMcpScript = Join-Path (Split-Path -Parent $PSCommandPath) "start-openpencil-mcp.ps1"
    if (-not (Test-Path $startMcpScript)) {
        throw "MCP start script not found at $startMcpScript"
    }

    $settings = Get-OpenPencilSettings -ScriptPath $PSCommandPath
    if (-not $PSBoundParameters.ContainsKey("EditorUrl")) {
        $EditorUrl = $settings.EditorUrl
    }

    Write-Step "Ensuring OpenPencil MCP is running on port $McpPort"
    if ([string]::IsNullOrWhiteSpace($EditorUrl)) {
        & $startMcpScript -Port $McpPort
    }
    else {
        & $startMcpScript -Port $McpPort -OpenUiUrl $EditorUrl
        $EditorUrl = $null
    }
}

if (-not $PSBoundParameters.ContainsKey("EditorUrl")) {
    $settings = Get-OpenPencilSettings -ScriptPath $PSCommandPath
    $EditorUrl = $settings.EditorUrl
}

$resolvedVariantsRoot = if (Test-Path $VariantsRoot) { (Resolve-Path $VariantsRoot).Path } else { $VariantsRoot }

Write-Step "Watching variants root: $resolvedVariantsRoot"

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
            $resolvedVariantPath = (Resolve-Path $latestVariant.FullName).Path
            Write-Step "Latest variant updated: $resolvedVariantPath"
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