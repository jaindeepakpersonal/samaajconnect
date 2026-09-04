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

# Every /v1 path literal either app contains, with the HTTP verb it is called
# with, the query string dropped and any interpolation flattened the same way.
#
# **The verb matters, and leaving it out made this sweep quietly lie.** The first
# version matched on the path alone, so `DELETE /v1/pathshala/pathshalas/{id}`
# counted as reached because a screen loads `GET` on that same path - the
# endpoint that stops a Pathshala operating looked built for as long as any
# screen could read one. Every REST path that carries more than one verb had the
# same hole, which is most of the interesting ones.
#
# The verb is the nearest `.get(`/`.post(`/... within three lines either side of
# the literal, searched per file so one file's last call cannot lend its verb to
# the next file's first path. Both directions are needed, because the call is
# written both ways round:
#
#     return this.http.get<RegisterEntry[]>(          <- verb first
#       `/v1/pathshala/classes/${classId}/register`,
#     );
#
#     const path = `/v1/identity/roles/${roleId}/...`; <- path first
#     return this.http.put<RoleMatrix>(path, { granted });
#
# A `/v1` literal with no call within three lines is a path handed to a helper -
# `this.one('/v1/audit/me/data-export', …)` in the member portal's DPDP export -
# and is recorded with a wildcard verb, counting as reached for every method.
# That is deliberately the *lenient* direction, and it is only safe because
# comments and specs are excluded first: what is left is real code passing a real
# path somewhere. Guessing a verb for it would be worse, and refusing to count it
# would report a screen that plainly exists as missing.
#
# Specs are excluded. `http.expectOne('/v1/…')` in a test is not a person
# clicking anything, and counting one as a caller is how an endpoint with a test
# and no screen - exactly what this sweep is for - would report as reached.
for file in $(find apps/*/src libs -name '*.ts' -not -name '*.spec.ts' -not -path '*/node_modules/*'); do
  awk '
      { line[NR] = $0 }
      END {
        for (n = 1; n <= NR; n++) {
          if (line[n] !~ /\/v1\//) continue

          # Comments are not callers, and this repo documents endpoints in prose
          # constantly. A doc comment in the Boli client mentioning
          # `/v1/pathshala/pathshalas` in passing was enough to make the endpoint
          # that stops a Pathshala operating look reached - the second time the
          # same endpoint slipped through this sweep for a different reason.
          if (line[n] ~ /^[[:space:]]*(\/\/|\/\*|\*)/) continue

          verb = ""

          for (d = 0; d <= 3 && verb == ""; d++) {
            for (s = -1; s <= 1 && verb == ""; s += 2) {
              m = n + (d * s)

              if (m < 1 || m > NR) continue

              if (match(line[m], /\.(get|post|put|patch|delete)[<(]/)) {
                verb = substr(line[m], RSTART + 1, RLENGTH - 2)
              }
            }
          }

          # No call within three lines: the path is reached by something this
          # script cannot see the verb of - a helper taking the path as an
          # argument, most often. Emitted with a wildcard verb so it counts as
          # reached for every method rather than as a gap for all of them.
          if (verb == "") verb = "*"

          rest = line[n]

          while (match(rest, /\/v1\/[^"'"'"'`?,)]*/)) {
            print toupper(verb) " " substr(rest, RSTART, RLENGTH)
            rest = substr(rest, RSTART + RLENGTH)
          }
        }
      }
    ' "$file"
done \
  | sed 's|\${[^}]*}|{id}|g; s|/$||' \
  | sort -u > "$called"

echo "== endpoints no app calls =="

unreached=0

while read -r verb path; do
  # The route with {id} turned into a path-segment wildcard, and every regex
  # metacharacter in the literal part escaped.
  pattern=$(printf '%s' "$path" | sed 's/[.[\*^$]/\\&/g; s/{id}/[^\/]*/g')
  upper=$(printf '%s' "$verb" | tr '[:lower:]' '[:upper:]')

  if ! grep -qE "^(${upper}|\*) ${pattern}$" "$called"; then
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
echo "Each of these is one of three things, and DEVELOPMENT_PLAN.md tracks which:"
echo
echo "  - a screen nobody has built. This is the case the sweep exists for."
echo "  - an endpoint meant for another service rather than an app."
echo "    GET /v1/identity/tenants/by-id/{id} is the gateway's."
echo "  - reached by a path the app is handed rather than one it writes down."
echo "    This sweep finds callers by looking for /v1/ literals in app code, so"
echo "    an endpoint whose path the server derives is invisible to it. The two"
echo "    photo reads are that: the client renders whatever 'photoUrl' it was"
echo "    given, through the scAuthedSrc directive, and never spells the path."
echo
echo "The third case is the one to be careful with. It is indistinguishable"
echo "from the first here, so a listed endpoint still has to be checked by"
echo "hand - which is the point of printing the list rather than failing on it."
