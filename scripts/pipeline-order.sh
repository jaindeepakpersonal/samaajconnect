#!/usr/bin/env bash
#
# Checks that every service registers the MediatR pipeline behaviors in the
# order root CLAUDE.md §4.4 documents.
#
# §4.4 calls that order "fixed, load-bearing" and ends with "Do not reorder
# these without updating this file and every service's Program.cs together".
# That is a rule stated in prose, duplicated across ten copies of one file, and
# until now enforced by nothing at all - which is the shape of thing that drifts
# quietly and is noticed late.
#
# What breaks if it does:
#
#   - Tenant authorization after validation means an unauthorized caller learns
#     the validation rules for data they cannot access. A 400 listing the fields
#     of a record is an answer about a record that should have been a 403.
#   - Validation after the transaction means an invalid request opens a database
#     transaction and holds it while FluentValidation decides it was never going
#     to be written.
#   - The unhandled-exception behavior anywhere but last stops being the
#     boundary that converts a thrown exception into a failure result.
#
# **The expected order is read out of CLAUDE.md rather than written here**, so
# the documentation is the single source of truth and this fails if either side
# moves without the other. Hard-coding it would make this a second place to
# update, which is the problem it exists to solve.
#
# It needs no running stack: it reads the source.
set -euo pipefail

cd "$(dirname "$0")/.."

expected=$(
  sed -n '/^### 4.4 Pipeline behavior order/,/^### 4.5/p' CLAUDE.md \
    | grep -oE '^[0-9]+\. `[A-Za-z]+`' \
    | sed 's/^[0-9]*\. `//; s/`$//' \
    | paste -sd, -
)

if [ -z "$expected" ]; then
  echo "  FAIL  could not read the pipeline order from CLAUDE.md §4.4"
  echo "        (the numbered list moved, or its formatting changed)"
  exit 1
fi

echo "== pipeline behavior order =="
echo
echo "  CLAUDE.md §4.4 says:"
echo "    $expected"
echo

fail=0
checked=0

# Only the Application project registers the pipeline. Every service has a
# second DependencyInjection.cs in Infrastructure that registers repositories
# and has nothing to do with this.
for file in services/*/src/*.Application/DependencyInjection.cs; do
  [ -e "$file" ] || continue

  service=$(printf '%s' "$file" | cut -d/ -f2)

  actual=$({ grep -oE 'AddOpenBehavior\(typeof\([A-Za-z]+' "$file" || true; } \
    | sed 's/.*typeof(//' \
    | paste -sd, -)

  checked=$((checked + 1))

  if [ "$actual" = "$expected" ]; then
    echo "  ok    $service"
  else
    echo "  FAIL  $service"
    echo "          expected: $expected"
    echo "          actual:   ${actual:-<none registered>}"
    fail=$((fail + 1))
  fi
done

if [ "$checked" -eq 0 ]; then
  echo "  FAIL  no service Application projects found - has the layout changed?"
  exit 1
fi

echo
echo "  $checked services checked, $fail wrong"

[ "$fail" -eq 0 ]
