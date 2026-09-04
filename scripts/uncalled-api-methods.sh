#!/usr/bin/env bash
#
# API-client methods no screen calls.
#
# **This is the half `scripts/unreachable-endpoints.sh` structurally cannot
# see.** That sweep finds callers by looking for `/v1/` literals in app code -
# and the literal for every endpoint lives in the app's API client, so an
# endpoint whose client method nothing calls still counts as reached. The dead
# end has simply moved one layer up.
#
# It found two the day it was written, and both had been sitting there for
# cycles:
#
#   - **`setGrievanceContact`**. DPDP section 13 requires a Data Fiduciary to
#     publish who answers a data principal's grievances.
#     `DPDP-COMPLIANCE.md` marked the obligation **built**, the endpoint was
#     built, the client method was written - and no screen called it, so the
#     only way for a Samaaj to name its grievance officer was curl.
#   - **`issueActivationCode`**. The dashboard shows a count of accounts
#     awaiting activation and there was no screen behind it, so an
#     administrator was told three people were waiting and could do nothing
#     for any of them.
#
# member-portal's own CLAUDE.md already named this shape - "that is the shape of
# gap this app should look for: a client method with no caller" - after
# `updateMe` sat uncalled through three cycles. Naming it was not enough; this
# is the same sentence made repeatable.
#
# **Reported, never failed**, like the endpoint and field sweeps. A method could
# legitimately be called through an alias this grep cannot see, and a list that
# fails on a false positive is a list somebody adds an exception to. Read it.
#
# It needs no running stack: it reads the source.
set -euo pipefail

cd "$(dirname "$0")/.."

echo "== API-client methods no screen calls =="
echo

total=0
uncalled=0

# The API clients, found rather than listed - the rule §9 states about the ten
# services applies to any hand-written list, including one in a check.
clients=$(find apps -name '*.api.ts' -o -name 'admin-api.ts' 2>/dev/null \
  | grep -v node_modules | sort)

if [ -z "$clients" ]; then
  echo "  FAIL  found no API clients under apps/ - this check cannot run"
  exit 1
fi

for client in $clients; do
  app=$(printf '%s' "$client" | cut -d/ -f2)

  # A public method on the client class: two spaces of indentation, a name, an
  # open paren. Deliberately not matching `private`, and not matching a
  # property - both are implementation rather than surface.
  methods=$({ grep -oE '^  [a-z][a-zA-Z0-9]*\(' "$client" || true; } \
    | tr -d ' (' | sort -u)

  for method in $methods; do
    [ -z "$method" ] && continue

    total=$((total + 1))

    # `.method(` anywhere in that app's source, which covers a component's
    # inline template as well as its class - both live in the .ts file.
    callers=$({ grep -rho "\.${method}(" "apps/$app/src" --include='*.ts' 2>/dev/null || true; } \
      | wc -l)

    if [ "$callers" -eq 0 ]; then
      printf '  %-16s %-30s %s\n' "$app" "$method" "$(basename "$client")"
      uncalled=$((uncalled + 1))
    fi
  done
done

if [ "$uncalled" -eq 0 ]; then
  echo "  (none - every API-client method has a caller)"
fi

echo
echo "=================================================="
echo "  API-client methods:  $total"
echo "  nothing calls:       $uncalled"
echo "=================================================="
echo
echo "A listed method is one of two things:"
echo
echo "  - an endpoint with no screen, wearing a client method as a disguise."
echo "    This is the case the sweep exists for, and it is invisible to the"
echo "    endpoint sweep because the path literal is right there in the client."
echo "  - a method reached by a name this grep cannot follow. Legitimate, and"
echo "    why this reports rather than fails."
