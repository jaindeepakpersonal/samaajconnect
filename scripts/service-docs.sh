#!/usr/bin/env bash
#
# Every command and query a service has is named in that service's own CLAUDE.md.
#
# Root `CLAUDE.md` §2 says each service doc runs Purpose -> Entities ->
# Commands -> Queries -> Events -> Endpoints -> Dependencies -> Testing, and §10
# sends a developer to that doc before touching the service. The Commands and
# Queries sections are therefore a claimed complete list of what the service can
# be asked to do - and §9 already wrote down what a hand-written list is:
#
#   "A hand-written list of the ten services is a list something will fall off,
#    and it will not fail when it does; it will simply be shorter."
#
# That was about the service list. It is just as true one level down. When this
# check was written, eleven commands and queries across four services were
# absent from their own documentation, and the worst case was self-inflicted:
# **member-family-service's entire photo feature** - seven commands and queries
# for uploading, serving and removing a member's or a child's photograph - was
# built, tested, smoke-checked through the gateway, given a long section of
# prose in the same file explaining why the bytes live in Postgres, and never
# added to the tables above that prose. A developer reading the Commands table
# to find out whether photos were possible would have concluded they were not.
#
# The direction that matters most is code -> doc: something built and not
# written down. The reverse - a doc naming a command that does not exist - is
# checked too, because it is the residue of a rename or a removal and it sends
# somebody looking for a file that is not there.
#
# **This fails rather than reports.** Unlike the unreachable-endpoint and
# unwritable-field sweeps, there is no legitimate second reading here: a command
# in the Application project either appears in the service's documentation or
# the documentation is wrong. There is nothing to weigh up.
#
# What this cannot check is the half that actually goes stale worst: prose.
# member-family-service's own doc said "a household whose head has erased can no
# longer decide a join request. Re-heading one is a known gap" for a full cycle
# after the erasure consumer started re-heading households, because the sentence
# and the code that contradicted it were changed by the same hand on the same
# day. A name can be matched; a claim cannot. Read the prose when you change the
# behaviour it describes.
#
# It needs no running stack: it reads the source.
set -euo pipefail

cd "$(dirname "$0")/.."

pass=0
fail=0

ok()  { echo "  ok    $1"; pass=$((pass + 1)); }
bad() { echo "  FAIL  $1"; fail=$((fail + 1)); }

echo "== every command and query is in its service's own CLAUDE.md =="
echo

# The services are read from the directories, never listed here - the same rule
# this check exists to enforce one level down.
services=$(ls -d services/*-service 2>/dev/null | sort)

if [ -z "$services" ]; then
  echo "  FAIL  found no services under services/ - this check cannot run"
  exit 1
fi

total=0
undocumented=0
absent=0

for service in $services; do
  name=$(basename "$service")
  doc="$service/CLAUDE.md"

  if [ ! -f "$doc" ]; then
    bad "$name has no CLAUDE.md"
    continue
  fi

  application=$(ls -d "$service"/src/*.Application 2>/dev/null | head -1)

  if [ -z "$application" ]; then
    bad "$name has no Application project - this check cannot read its requests"
    continue
  fi

  # Every request type the service declares. Requests are records by convention
  # (root CLAUDE.md §4.1) and the marker interface is on the declaration, so the
  # name is enough; the interface itself is not matched because a command can
  # declare `ICommand<Guid>` on a following line.
  #
  # Scoped to the Application project rather than to */Commands/ and */Queries/
  # folders on purpose. Four features sit outside that folder shape today - the
  # photo and logo commands, and the integration-event consumers - and a check
  # that skipped them would have missed the exact case it was written for.
  #
  # The `|| true` on each grep is load-bearing under `set -o pipefail`: a grep
  # that matches nothing exits 1, which fails the whole pipeline and kills the
  # script through errexit. Without it, a service whose CLAUDE.md names no
  # request at all - the worst case this check exists for - ended the run
  # silently three services earlier, reporting nothing rather than failing.
  code=$({ grep -rhoE 'record [A-Za-z]+(Command|Query)\b' "$application" \
    --include='*.cs' 2>/dev/null || true; } \
    | awk '{ print $2 }' | sort -u)

  # Anything named in backticks in the doc. Not restricted to the tables: a
  # command explained in the decisions prose and left out of the table is
  # documented, if untidily, and failing that would be this check having an
  # opinion about layout rather than about coverage.
  documented=$({ grep -oE '`[A-Za-z]+(Command|Query)`' "$doc" || true; } \
    | tr -d '`' | sort -u)

  count=$(printf '%s\n' "$code" | grep -c . || true)
  total=$((total + count))

  # The `sed '/^$/d'` is not tidiness. An empty `$code` or `$documented` becomes
  # one blank line through printf, comm reports that blank as a difference, and
  # the service would fail with an entry that has no name in it - a failure
  # nobody could act on, for a service that has simply not been written yet.
  missing=$(comm -13 <(printf '%s\n' "$documented") <(printf '%s\n' "$code") | sed '/^$/d')
  ghost=$(comm -23 <(printf '%s\n' "$documented") <(printf '%s\n' "$code") | sed '/^$/d')

  if [ -n "$missing" ]; then
    n=$(printf '%s\n' "$missing" | grep -c .)
    undocumented=$((undocumented + n))
    bad "$name: $n of its $count requests are in no line of its CLAUDE.md:"
    printf '%s\n' "$missing" | sed 's/^/          /'
  elif [ -n "$ghost" ]; then
    : # reported below, so a service with both does not print a passing line
  else
    ok "$name: all $count"
  fi

  if [ -n "$ghost" ]; then
    n=$(printf '%s\n' "$ghost" | grep -c .)
    absent=$((absent + n))
    bad "$name: its CLAUDE.md names requests the service does not have:"
    printf '%s\n' "$ghost" | sed 's/^/          /'
  fi
done

echo
echo "=================================================="
echo "  commands and queries:     $total"
echo "  documented nowhere:       $undocumented"
echo "  documented, not in code:  $absent"
echo "=================================================="

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
