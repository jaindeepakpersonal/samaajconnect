#!/bin/bash
# Creates one logical database per service (CLAUDE.md §8: samaajconnect_{name}).
# Runs once, on first Postgres container start, against an empty data volume.
set -euo pipefail

databases=(
  samaajconnect_identity
  samaajconnect_member_family
  samaajconnect_timeline
  samaajconnect_volunteer_groups
  samaajconnect_events
  samaajconnect_social_issues
  samaajconnect_celebrity_voting
  samaajconnect_pathshala
  samaajconnect_boli
  samaajconnect_audit_notification
)

for db in "${databases[@]}"; do
  echo "  creating database '$db'"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "${POSTGRES_DB:-postgres}" <<-SQL
    SELECT 'CREATE DATABASE $db'
    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '$db')\gexec
SQL
done
