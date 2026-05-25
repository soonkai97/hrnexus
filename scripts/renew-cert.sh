#!/bin/bash
# HRNexus — Let's Encrypt certificate renewal
# Certbot --standalone needs port 80 free, so we stop/start the web container.
# Cron: 0 3 1 */2 * root /root/hrnexus/scripts/renew-cert.sh >> /var/log/hrnexus-renew.log 2>&1

set -euo pipefail

COMPOSE_DIR="/root/hrnexus"
COMPOSE_CMD="docker compose -f docker-compose.yml -f docker-compose.uat.yml"

echo "[$(date '+%F %T')] Starting certificate renewal..."

cd "$COMPOSE_DIR"

# Stop nginx so port 80 is free for the ACME challenge
$COMPOSE_CMD stop web

# Attempt renewal (only renews if cert expires within 30 days)
certbot renew --standalone

# Restart nginx with the fresh cert
$COMPOSE_CMD start web

echo "[$(date '+%F %T')] Certificate renewal complete."
