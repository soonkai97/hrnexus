#!/usr/bin/env bash
# gen-build-version.sh — emit a build number of the form YYYYMMDD.NN where NN is
# the Nth build produced today (01 = first build of the day). The per-day counter
# lives in scripts/.build-seq (git-ignored, host-local — prod and local machines
# count independently). Echoes only the version string so callers can capture it:
#
#   BUILD_VERSION="$(bash scripts/gen-build-version.sh)"
#
set -euo pipefail

SEQ_FILE="$(dirname "$0")/.build-seq"
TODAY="$(date +%Y%m%d)"

last_date=""
last_seq=0
if [[ -f "$SEQ_FILE" ]]; then
  read -r last_date last_seq < "$SEQ_FILE" || true
fi

if [[ "$last_date" == "$TODAY" ]]; then
  seq=$((last_seq + 1))
else
  seq=1
fi

printf '%s %s\n' "$TODAY" "$seq" > "$SEQ_FILE"
printf '%s.%02d\n' "$TODAY" "$seq"
