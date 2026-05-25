#!/bin/bash
# HRNexus — daily PostgreSQL backup
# Saves a gzipped dump to /opt/hrnexus/backups and keeps the last 7 days.
# Usage: run directly, or via cron (see README / CLAUDE.md for setup).

set -euo pipefail

BACKUP_DIR="/opt/hrnexus/backups"
DB_CONTAINER="hrnexus-postgres-1"
DB_USER="${POSTGRES_USER:-ncuser}"
DB_NAME="${POSTGRES_DB:-nightclub_workforce}"
KEEP_DAYS=7
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="$BACKUP_DIR/hrnexus_${TIMESTAMP}.sql.gz"

mkdir -p "$BACKUP_DIR"

echo "[$(date '+%F %T')] Starting backup..."

docker exec "$DB_CONTAINER" pg_dump -U "$DB_USER" "$DB_NAME" | gzip > "$BACKUP_FILE"

# Verify the file has content
if [ ! -s "$BACKUP_FILE" ]; then
    echo "[$(date '+%F %T')] ERROR: Backup file is empty — aborting."
    rm -f "$BACKUP_FILE"
    exit 1
fi

SIZE=$(du -h "$BACKUP_FILE" | cut -f1)
echo "[$(date '+%F %T')] Backup saved: $BACKUP_FILE ($SIZE)"

# Remove backups older than KEEP_DAYS
DELETED=$(find "$BACKUP_DIR" -name "hrnexus_*.sql.gz" -mtime +$KEEP_DAYS -print -delete | wc -l)
echo "[$(date '+%F %T')] Cleaned $DELETED old backup(s) (keeping last ${KEEP_DAYS} days)"
