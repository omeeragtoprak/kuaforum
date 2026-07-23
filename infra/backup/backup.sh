#!/bin/sh
# Kuaforum Postgres nightly backup sidecar.
#
# Deliberately simple: plain pg_dump + gzip on a sleep loop, writing to a local
# named volume, with a find -mtime cleanup for retention. No cron daemon, no
# third-party backup image — this is a stopgap until WAL-G → R2 continuous
# backup (see docs/12-devops) is wired up, which needs a founder-owned R2
# bucket that doesn't exist yet. Local-volume backups are NOT disaster-proof
# (same host as the DB) — do not treat this as the final backup story.
set -eu

BACKUP_DIR=/backups
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-7}"
INTERVAL_SECONDS="${BACKUP_INTERVAL_SECONDS:-86400}"

mkdir -p "$BACKUP_DIR"

echo "[backup] starting: interval=${INTERVAL_SECONDS}s retention=${RETENTION_DAYS}d dir=${BACKUP_DIR}"

while true; do
  ts="$(date -u +%Y%m%dT%H%M%SZ)"
  file="${BACKUP_DIR}/kuaforum_${ts}.sql.gz"
  echo "[backup] $(date -u +%FT%TZ) dumping ${POSTGRES_DB} -> ${file}"

  if PGPASSWORD="$POSTGRES_PASSWORD" pg_dump -h postgres -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
      | gzip > "$file"; then
    echo "[backup] OK: ${file} ($(du -h "$file" | cut -f1))"
  else
    echo "[backup] FAILED for ${ts}" >&2
    rm -f "$file"
  fi

  find "$BACKUP_DIR" -name 'kuaforum_*.sql.gz' -mtime "+${RETENTION_DAYS}" -print -delete

  sleep "$INTERVAL_SECONDS"
done
