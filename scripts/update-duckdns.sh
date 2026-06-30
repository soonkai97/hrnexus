#!/bin/bash
# HRNexus — keep DuckDNS pointing at this server's public IP.
# Reads DUCKDNS_TOKEN from the .env file — never hardcodes the token.
# Cron: */5 * * * * /root/hrnexus/scripts/update-duckdns.sh >> /var/log/hrnexus-duckdns.log 2>&1

set -euo pipefail

DOMAIN="hrnexus"
ENV_FILE="/root/hrnexus/.env"

TOKEN=$(grep "^DUCKDNS_TOKEN=" "$ENV_FILE" | cut -d'=' -f2 | tr -d '[:space:]')

if [ -z "$TOKEN" ]; then
    echo "[$(date '+%F %T')] ERROR: DUCKDNS_TOKEN not set in $ENV_FILE"
    exit 1
fi

RESULT=$(curl -sf "https://www.duckdns.org/update?domains=${DOMAIN}&token=${TOKEN}&ip=")

if [ "$RESULT" = "OK" ]; then
    echo "[$(date '+%F %T')] DuckDNS updated OK"
else
    echo "[$(date '+%F %T')] DuckDNS update FAILED: $RESULT"
    exit 1
fi
