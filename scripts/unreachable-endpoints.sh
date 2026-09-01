#!/usr/bin/env bash
#
# Lists every endpoint the services map that neither Angular app ever calls.
#
# Three cycles running, the most valuable thing to build next turned out to be
# an endpoint with no caller:
#
#   - the member profile screen, which the welcome notification tells every new
#     member to go and use;
#   - timeline moderation, without which no member post could ever be approved;
#   - redeeming an activation code, which three screens in the admin panel told
#     people to do "in the member portal" while the member portal had nowhere to
#     do it - so no invited administrator could sign in and no converted adult
#     child could get an account.
#
# All three were complete, tested, and covered by the smoke script through curl.
# What they had in common is that nothing a person could click reached them, and
# nothing failed to say so. This script is that check, made repeatable.
#
# It needs no running stack: it reads the source.
#
# **A listed endpoint is not automatically a bug.** Some are deliberately
# reachable only by another service - `/v1/identity/tenants/by-id/{id}` is the
# gateway's, not an app's. Read the list, do not clear it.
set -euo pipefail

cd "$(dirname "$0")/.."

routes=$(mktemp)
called=$(mktemp)
trap 'rm -f "$routes" "$called"' EXIT

# Every mapped route, resolved through its MapGroup prefix and with route
# parameters flattened to {id} so they can be matched against a client's
# interpolated path.
for file in services/*/src/*/Endpoints/*.cs; do
  # `|| true` on both greps in this loop. A file with no MapGroup at all -
  # audit-notification maps absolute paths - makes grep exit 1, and with
  # `set -euo pipefail` that takes the whole sweep down before it prints
  # anything. A sweep that dies silently on the first file is worse than none.
  prefix=$({ grep -oE 'MapGroup\("[^"]*"\)' "$file" || true; } \
    | head -1 | sed 's/MapGroup("//; s/")//')

  { grep -oE 'Map(Get|Post|Put|Patch|Delete)\("[^"]*"' "$file" || true; } \
    | sed 's/Map\([A-Za-z]*\)("/\1 /; s/"$//' \
    | while read -r verb path; do
        case "$path" in
          /v1/*) echo "$verb $path" ;;
          *) echo "$verb ${prefix}${path}" ;;
        esac
      done
done | sed 's/{[a-zA-Z]*:guid}/{id}/g; s/{[a-zA-Z]*}/{id}/g; s|/$||' | sort -u > "$routes"

# Every /v1 path literal either app contains, in a quoted or templated string,
# with the query string and any interpolation flattened the same way.
grep -rhoE "'/v1/[^']*'|\`/v1/[^\`]*\`" apps/*/src libs --include=*.ts \
  | tr -d "'\`" \
  | sed 's/?.*//; s|\${[^}]*}|{id}|g; s|/$||' \
  | sort -u > "$called"

echo "== endpoints no app calls =="

unreached=0

while read -r verb path; do
  # The route with {id} turned into a path-segment wildcard, and every regex
  # metacharacter in the literal part escaped.
  pattern=$(printf '%s' "$path" | sed 's/[.[\*^$]/\\&/g; s/{id}/[^\/]*/g')

  if ! grep -qE "^${pattern}$" "$called"; then
    printf '  %s %s\n' "$verb" "$path"
    unreached=$((unreached + 1))
  fi
done < "$routes"

total=$(wc -l < "$routes" | tr -d ' ')

echo
echo "=================================================="
echo "  mapped endpoints:   $total"
echo "  reached by an app:  $((total - unreached))"
echo "  reached by neither: $unreached"
echo "=================================================="
echo
echo "Each of these is either a screen nobody has built, or an endpoint meant"
echo "for another service. DEVELOPMENT_PLAN.md tracks which is which."
