#!/usr/bin/env bash
# deploy.sh — one-command deploy for UAT/production
#
# Usage:
#   ./deploy.sh          → pull + rebuild + restart the web service (Angular + nginx)
#   ./deploy.sh api      → pull + rebuild + restart only the .NET API
#   ./deploy.sh all      → pull + rebuild + restart both web and api
#
# Run from the repo root on the server. Requires .env to be present.

set -euo pipefail

SERVICE="${1:-web}"
COMPOSE="docker compose -f docker-compose.yml -f docker-compose.uat.yml"

# ── Verify .env exists ────────────────────────────────────────────────────────
if [[ ! -f .env ]]; then
  echo "✗ .env file not found. Copy .env.example and fill in the values."
  exit 1
fi

# ── Pull latest code ──────────────────────────────────────────────────────────
echo "▶  Pulling latest code from origin…"
git pull

# ── Maintenance mode ON ───────────────────────────────────────────────────────
# Only flip to maintenance for deployments that rebuild the web container.
# The flag lives in ./maintenance/on — mounted into the nginx container.
# Nginx checks the file on every request; no reload needed.
MAINTENANCE_FLAG="./maintenance/on"
if [[ "$SERVICE" == "web" || "$SERVICE" == "all" ]]; then
  mkdir -p ./maintenance
  touch "$MAINTENANCE_FLAG"
  echo "▶  Maintenance mode ON — users will see the maintenance page."
fi

# ── Build ─────────────────────────────────────────────────────────────────────
if [[ "$SERVICE" == "all" ]]; then
  echo "▶  Building web and api (--no-cache)…"
  $COMPOSE build --no-cache web api
else
  echo "▶  Building $SERVICE (--no-cache)…"
  $COMPOSE build --no-cache "$SERVICE"
fi

# ── Restart (rolling — other services stay up) ────────────────────────────────
if [[ "$SERVICE" == "all" ]]; then
  echo "▶  Restarting web and api…"
  $COMPOSE up -d --no-deps web api
else
  echo "▶  Restarting $SERVICE…"
  $COMPOSE up -d --no-deps "$SERVICE"
fi

# ── Maintenance mode OFF ──────────────────────────────────────────────────────
if [[ -f "$MAINTENANCE_FLAG" ]]; then
  rm -f "$MAINTENANCE_FLAG"
  echo "▶  Maintenance mode OFF — site is live."
fi

# ── Confirm healthy ───────────────────────────────────────────────────────────
echo ""
echo "▶  Container status:"
$COMPOSE ps

echo ""
echo "▶  Last 20 log lines (10 s):"
timeout 10 $COMPOSE logs --tail=20 -f "$SERVICE" 2>/dev/null || true

echo ""
echo "✅  Deploy complete."
echo "   Remember to hard-refresh the browser (Ctrl+Shift+R) to bypass cached index.html."
