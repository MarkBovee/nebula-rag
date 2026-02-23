$ContainerArgs = @($args)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$imageName = "localhost/nebula-rag-mcp:latest"
$forceRebuild = $env:NEBULARAG_REBUILD_IMAGE -eq "1"
$envFilePath = Join-Path $repoRoot ".env"

$engine = if (Get-Command podman -ErrorAction SilentlyContinue) {
    "podman"
} elseif (Get-Command docker -ErrorAction SilentlyContinue) {
    "docker"
} else {
    throw "Neither podman nor docker was found in PATH."
}

$imageId = (& $engine images -q $imageName 2>$null | Out-String).Trim()
if ($forceRebuild -or [string]::IsNullOrWhiteSpace($imageId)) {
    & $engine build -f "$repoRoot/Dockerfile" -t $imageName $repoRoot
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$runArgs = @(
    "run",
    "--rm",
    "--pull=never",
    "-i",
    "-a", "stdin",
    "-a", "stdout",
    "--env-file", $envFilePath
)

Get-ChildItem Env: | Where-Object { $_.Name -like "NEBULARAG_*" } | ForEach-Object {
    $runArgs += @("-e", "{0}={1}" -f $_.Name, $_.Value)
}

$runArgs += $imageName

if ($ContainerArgs.Count -gt 0) {
    $runArgs += $ContainerArgs
}

& $engine @runArgs
exit $LASTEXITCODE
