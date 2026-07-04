# docker-redeploy.ps1 — local versioned rebuild + restart (Windows / Docker Desktop).
# Computes the YYYYMMDD.NN build number, passes it as build args, then rebuilds
# and restarts. Mirrors what deploy.sh does on the prod server.
#
#   ./scripts/docker-redeploy.ps1            # web only (most common)
#   ./scripts/docker-redeploy.ps1 api        # api only
#   ./scripts/docker-redeploy.ps1 all        # web + api
#
param([string]$Service = 'web')

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# Compute the build stamp (fail loud if the generator errors).
$ErrorActionPreference = 'Stop'
$env:BUILD_VERSION = & (Join-Path $PSScriptRoot 'gen-build-version.ps1')
$env:BUILD_SHA     = (git rev-parse --short HEAD)
$env:BUILT_AT      = (Get-Date -Format 'yyyy-MM-ddTHH:mmZ')
Write-Host "Build version: $($env:BUILD_VERSION) ($($env:BUILD_SHA))" -ForegroundColor Cyan

# Docker writes progress to stderr — don't let that abort the script; check exit codes instead.
$ErrorActionPreference = 'Continue'
if ($Service -eq 'all') {
  docker compose build --no-cache web api
} else {
  docker compose build --no-cache $Service
}
if ($LASTEXITCODE -ne 0) { Write-Error "docker build failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }

docker compose up -d
if ($LASTEXITCODE -ne 0) { Write-Error "docker compose up failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }

Write-Host "Deployed $($env:BUILD_VERSION)." -ForegroundColor Green
