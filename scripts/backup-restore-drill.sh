#!/usr/bin/env bash
#
# Backs up every logical database, restores each into a scratch copy, and
# proves the copy is the same as the original.
#
# This is the drill `DEVELOPMENT_PLAN.md` Phase 5 asks for, and the reason it is
# a drill rather than a runbook entry: a backup nobody has restored is not a
# backup, it is a file. The only way to know `pg_dump` output is usable is to
# use it.
#
# **It never touches the live databases.** Each dump is restored into a
# `_drill`-suffixed database, compared against the original, and dropped. That
# is what makes it safe to run against a running system - which matters, because
# a drill that can only be run somewhere safe is a drill nobody runs.
#
# What it proves:
#   - every database dumps without error
#   - every dump restores without error
#   - every table comes back with the same number of rows
#   - the unique indexes that are correctness guarantees survive the round trip
#
# What it does not prove, and is written down so nobody assumes otherwise:
#   - point-in-time recovery. These are full dumps, so the recovery point is
#     whenever the dump ran, and everything since is lost. A real deployment
#     needs WAL archiving and this drill says nothing about it.
#   - anything about Kafka. Undispatched outbox rows are in the dump and will be
#     re-sent on restore, which is fine because consumers are idempotent; already
#     -dispatched events that consumers acted on are not replayed, so a restored
#     system can be behind its consumers.
#   - that the dump is stored anywhere durable. Writing it next to the database
#     it came from protects against exactly nothing.
set -euo pipefail

COMPOSE_SERVICE="${COMPOSE_SERVICE:-postgres}"
PGUSER="${PGUSER:-samaajconnect}"
OUT_DIR="${OUT_DIR:-.backup-drill}"

pass=0
fail=0

note() { printf '  %s\n' "$*"; }

check() {
  local label="$1" expected="$2" actual="$3"

  if [ "$expected" = "$actual" ]; then
    printf '  ok    %s\n' "$label"
    pass=$((pass + 1))
  else
    printf '  FAIL  %s (expected %s, got %s)\n' "$label" "$expected" "$actual"
    fail=$((fail + 1))
  fi
}

psql_q() {
  # -t tuples only, -A unaligned: output a script can compare.
  docker compose exec -T "$COMPOSE_SERVICE" \
    psql -U "$PGUSER" -d "$1" -t -A -c "$2"
}

# Row counts for every table, as one block compared verbatim between original
# and restored - so a restore that silently drops rows from one table fails
# rather than passing on a total that happens to still add up.
#
# An actual count per table, deliberately, not pg_class.reltuples. That is a
# planner estimate and is wrong immediately after a restore, which would make
# this drill pass on a database with nothing in it.
row_counts() {
  local db="$1"
  local tables
  tables=$(psql_q "$db" "
    SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename;")

  local out="" table n
  local -a table_list
  mapfile -t table_list <<< "$tables"

  # A `for`, for the same reason as the loops below: psql runs through
  # `docker compose exec -T`, which reads standard input and would eat the rest
  # of the table list on the first iteration.
  for table in "${table_list[@]}"; do
    [ -z "$table" ] && continue
    n=$(psql_q "$db" "SELECT count(*) FROM public.\"$table\";")
    out="${out}${table}=${n}"$'\n'
  done

  printf '%s' "$out"
}

# The unique indexes this platform documents as correctness guarantees rather
# than as performance. A restore that lost one would not fail loudly - it would
# quietly re-enable double voting, or two bids at one amount.
guarantee_indexes() {
  psql_q "$1" "
    SELECT string_agg(indexname, ',' ORDER BY indexname)
    FROM pg_indexes
    WHERE schemaname = 'public' AND indexdef LIKE 'CREATE UNIQUE INDEX%';"
}

mkdir -p "$OUT_DIR"

databases=$(docker compose exec -T "$COMPOSE_SERVICE" \
  psql -U "$PGUSER" -d postgres -t -A -c \
  "SELECT datname FROM pg_database WHERE datname LIKE 'samaajconnect%' AND datname NOT LIKE '%_drill' ORDER BY datname;")

[ -n "$databases" ] || { echo "No samaajconnect databases found. Is the stack up?"; exit 1; }

# Into an array, and every loop below is a `for` rather than a `while read`.
#
# This mattered: the first version piped the list into `while IFS= read -r db`,
# and `docker compose exec -T` inside the body reads standard input - so the
# first iteration swallowed the remaining nine database names. The drill
# reported "every database dumped, restored, and came back identical" having
# checked one of ten. A drill that passes for the wrong reason is worse than no
# drill, so there is no stdin for a command to eat any more.
mapfile -t db_list <<< "$databases"

echo "== backing up =="

for db in "${db_list[@]}"; do
  [ -z "$db" ] && continue

  docker compose exec -T "$COMPOSE_SERVICE" \
    pg_dump -U "$PGUSER" --format=custom --no-owner --no-acl "$db" > "$OUT_DIR/$db.dump"

  size=$(wc -c < "$OUT_DIR/$db.dump" | tr -d ' ')

  if [ "$size" -lt 1000 ]; then
    echo "  FAIL  $db dumped only $size bytes"
    fail=$((fail + 1))
  else
    note "$db -> $(printf '%s' "$size") bytes"
  fi
done

echo
echo "== restoring each into a scratch copy and comparing =="

for db in "${db_list[@]}"; do
  [ -z "$db" ] && continue

  drill="${db}_drill"

  # Dropped first in case a previous run was interrupted. FORCE disconnects
  # anything still attached, which a failed run can leave behind.
  docker compose exec -T "$COMPOSE_SERVICE" \
    psql -U "$PGUSER" -d postgres -q -c "DROP DATABASE IF EXISTS $drill WITH (FORCE);" >/dev/null
  docker compose exec -T "$COMPOSE_SERVICE" \
    psql -U "$PGUSER" -d postgres -q -c "CREATE DATABASE $drill;" >/dev/null

  # pg_restore's exit code is not the whole story: it warns rather than fails on
  # a good many things. The comparison below is what actually decides.
  docker compose exec -T "$COMPOSE_SERVICE" \
    pg_restore -U "$PGUSER" -d "$drill" --no-owner --no-acl < "$OUT_DIR/$db.dump" >/dev/null 2>&1 || true

  check "$db rows" "$(row_counts "$db")" "$(row_counts "$drill")"
  check "$db unique indexes" "$(guarantee_indexes "$db")" "$(guarantee_indexes "$drill")"

  docker compose exec -T "$COMPOSE_SERVICE" \
    psql -U "$PGUSER" -d postgres -q -c "DROP DATABASE $drill WITH (FORCE);" >/dev/null
done

echo
echo "=================================================="
echo "  checks passed: $pass"
echo "  checks failed: $fail"
echo "=================================================="

if [ "$fail" -gt 0 ]; then
  echo "A database did not come back the same as it went in."
  exit 1
fi

echo "Every database dumped, restored, and came back identical."
echo
echo "The dumps are in $OUT_DIR and are NOT a backup: they sit on the same"
echo "machine as the database they came from. Somewhere else is the whole point."
