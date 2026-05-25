#!/bin/bash
# HRNexus — restore PostgreSQL from a backup file
# Usage: ./restore-db.sh /opt/hrnexus/backups/hrnexus_20260101_020000.sql.gz
#
# WARNING: This overwrites ALL current data. Use only for disaster recovery.

set -euo pipefail

if [ -z "${1:-}" ]; then
    echo "Usage: $0 <backup-file.sql.gz>"
    echo "Available backups:"
    ls -lh /opt/hrnexus/backups/hrnexus_*.sql.gz 2>/dev/null || echo "  (none found)"
    exit 1
fi

BACKUP_FILE="$1"
DB_CONTAINER="nightclub-workforce-postgres-1"
DB_USER="${POSTGRES_USER:-ncuser}"
DB_NAME="${POSTGRES_DB:-nightclub_workforce}"

if [ ! -f "$BACKUP_FILE" ]; then
    echo "ERROR: File not found: $BACKUP_FILE"
    exit 1
fi

echo ""
echo "=========================================="
echo "  RESTORE: $BACKUP_FILE"
echo "  Target:  $DB_NAME"
echo "  WARNING: ALL existing data will be REPLACED"
echo "=========================================="
echo ""
read -p "Type YES to confirm: " CONFIRM
if [ "$CONFIRM" != "YES" ]; then
    echo "Aborted."
    exit 0
fi

echo "[$(date '+%F %T')] Dropping and recreating database..."
docker exec "$DB_CONTAINER" psql -U "$DB_USER" -c "DROP DATABASE IF EXISTS ${DB_NAME};"
docker exec "$DB_CONTAINER" psql -U "$DB_USER" -c "CREATE DATABASE ${DB_NAME};"

echo "[$(date '+%F %T')] Restoring from backup..."
gunzip -c "$BACKUP_FILE" | docker exec -i "$DB_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME"

echo "[$(date '+%F %T')] Restore complete."
