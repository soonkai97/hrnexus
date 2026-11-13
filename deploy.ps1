# deploy.ps1 — one-command deploy for Windows (PowerShell)
#
# Usage (run from repo root):
#   .\deploy.ps1          → pull + rebuild + restart web (Angular + nginx)  [default]
#   .\deploy.ps1 api      → pull + rebuild + restart .NET API only
#   .\deploy.ps1 all      → pull + rebuild + restart both web and api

param(
    [string]$Service = "web"
)

$ErrorActionPreference = "Stop"
$COMPOSE_FILES = @("-f", "docker-compose.yml")

# ── Verify .env exists ────────────────────────────────────────────────────────
if (-not (Test-Path ".env")) {
    Write-Host "✗ .env file not found. Copy .env.example and fill in the values." -ForegroundColor Red
    exit 1
}

# ── Pull latest code ──────────────────────────────────────────────────────────
Write-Host "▶  Pulling latest code from origin…" -ForegroundColor Cyan
git pull

# ── Build ─────────────────────────────────────────────────────────────────────
if ($Service -eq "all") {
    Write-Host "▶  Building web and api (--no-cache)…" -ForegroundColor Cyan
    docker compose @COMPOSE_FILES build --no-cache web api
} else {
    Write-Host "▶  Building $Service (--no-cache)…" -ForegroundColor Cyan
    docker compose @COMPOSE_FILES build --no-cache $Service
}

# ── Restart ───────────────────────────────────────────────────────────────────
if ($Service -eq "all") {
    Write-Host "▶  Restarting web and api…" -ForegroundColor Cyan
    docker compose @COMPOSE_FILES up -d --no-deps web api
} else {
    Write-Host "▶  Restarting $Service…" -ForegroundColor Cyan
    docker compose @COMPOSE_FILES up -d --no-deps $Service
}

# ── Status ────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "▶  Container status:" -ForegroundColor Cyan
docker compose @COMPOSE_FILES ps

Write-Host ""
Write-Host "✅  Deploy complete." -ForegroundColor Green
Write-Host "   Hard-refresh the browser (Ctrl+Shift+R) to bypass cached index.html." -ForegroundColor Yellow
