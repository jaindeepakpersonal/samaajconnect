#!/usr/bin/env bash
#
# Every <table> in either app has a <caption class="sr-only"> naming it.
#
# The 2026-09-02 accessibility audit found 23 tables with no accessible name
# at all - a screen reader listing the tables on the class screen got
# "table, table, table" - and gave every one of them a caption. Nothing
# checks that a 24th table gets one too, or that none of the 23 loses theirs
# in a later edit. `role-matrix.component.ts` - the widest table in either
# app, per this app's own CLAUDE.md - was missing one on 2026-09-05, found by
# counting `<table` against `caption class="sr-only"` per file rather than by
# rereading all 24 by hand.
#
# It needs no running stack: it reads the source.
set -uo pipefail

cd "$(dirname "$0")/.."

pass=0
fail=0

check_app() {
  local app="$1" file tables captions

  for file in $(find "apps/$app/src/app" -name "*.ts" ! -name "*.spec.ts" | sort); do
    grep -q "<table" "$file" || continue

    tables=$(grep -c "<table" "$file")
    captions=$(grep -c 'caption class="sr-only"' "$file")

    if [ "$tables" != "$captions" ]; then
      echo "  FAIL  $app :: $file :: $tables table(s), $captions captioned"
      fail=$((fail + 1))
    fi
  done
}

echo "== every table has a captioned accessible name =="
echo

check_app member-portal
check_app admin-portal

if [ "$fail" -eq 0 ]; then
  echo "  ok    every table in either app is captioned"
  pass=1
fi

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
