#!/usr/bin/env bash
#
# Answers one question: is any service quietly left out?
#
# There are ten of them and every list of them is written by hand somewhere -
# the CI matrix, the gateway's route table, the smoke script's sections. A
# service missing from one of those lists does not fail; it is simply absent,
# and absence is what nobody notices. boli-service has now been the missing one
# twice: it had no gateway coverage at all for eleven cycles while its module
# key was toggled on and off around it, and its solution was never added to the
# CI matrix, so its 49 tests - including the ones holding "a Boli has exactly
# one highest bid" - had never run in CI.
#
# Three lists, checked against the ten directories under services/:
#
#   1. The gateway's route table. Every service is behind a cluster, and every
#      cluster points at something that exists.
#   2. scripts/smoke-through-gateway.sh. Root CLAUDE.md §9's last bullet asks
#      for at least one test per service **through the gateway**, because a
#      route that works in isolation but is not wired into YARP is a failure
#      the service's own integration suite cannot see: the endpoint is real,
#      the suite passes, and every caller gets a 404.
#   3. The CI matrix. A service whose solution is not named there is a service
#      whose tests run only on somebody's laptop.
#
# Each mapping is read from the file that owns it rather than from a list here,
# so a new service fails all three until it is added to all three - which is
# the point.
#
# This is a different question from scripts/unreachable-endpoints.sh, which asks
# which endpoints no *app* calls. A service can be reachable by both portals and
# still have no test proving the gateway routes to it.
#
# It needs no running stack: it reads the source.
set -euo pipefail

cd "$(dirname "$0")/.."

GATEWAY_CONFIG=gateway/src/Sangam.Gateway/appsettings.json
SMOKE=scripts/smoke-through-gateway.sh
CI=.github/workflows/ci.yml

for file in "$GATEWAY_CONFIG" "$SMOKE" "$CI"; do
  if [ ! -f "$file" ]; then
    echo "  FAIL  $file is missing - this check cannot read the route table"
    exit 1
  fi
done

pass=0
fail=0

# ---- cluster -> service, from the destination address ----------------------
#
# A cluster's destination names the container, which is the directory under
# services/. Reading it beats a table here: a cluster repointed at a different
# service changes this map on its own.
CLUSTER_SERVICE=$(awk '
  /"Clusters"/ { in_clusters = 1 }
  in_clusters && /^      "[a-z-]+": \{/ {
    match($0, /"[a-z-]+"/)
    cluster = substr($0, RSTART + 1, RLENGTH - 2)
  }
  in_clusters && /"Address"/ {
    if (match($0, /http:\/\/[a-z-]+/)) {
      host = substr($0, RSTART + 7, RLENGTH - 7)
      print cluster "\t" host
    }
  }
' "$GATEWAY_CONFIG" | sort -u)

# ---- /v1/{prefix} -> cluster, from the routes ------------------------------
#
# ClusterId precedes Match within a route block. The member-portal catch-all
# has no /v1 prefix and so never matches here, which is right: it is the front
# door for the app, not a service route.
PREFIX_CLUSTER=$(awk '
  /"Routes"/ { in_routes = 1 }
  /"Clusters"/ { in_routes = 0 }
  in_routes && /"ClusterId"/ {
    match($0, /: *"[a-z-]+"/)
    cluster = substr($0, RSTART + 3, RLENGTH - 4)
  }
  in_routes && /"Path"/ {
    if (match($0, /\/v1\/[a-z-]+/)) {
      prefix = substr($0, RSTART + 4, RLENGTH - 4)
      print prefix "\t" cluster
    }
  }
' "$GATEWAY_CONFIG" | sort -u)

if [ -z "$CLUSTER_SERVICE" ] || [ -z "$PREFIX_CLUSTER" ]; then
  echo "  FAIL  could not read the route table out of $GATEWAY_CONFIG"
  echo "        (its shape changed - Routes, Clusters, ClusterId, Path, Address)"
  exit 1
fi

echo "== gateway coverage =="
echo
echo "  $(printf '%s\n' "$PREFIX_CLUSTER" | grep -c .) route prefixes across" \
     "$(printf '%s\n' "$CLUSTER_SERVICE" | cut -f1 | sort -u | grep -c .) clusters"
echo

# ---- the map, and the two ways it can be wrong -----------------------------

echo "-- every service has a gateway route, and every route has a service --"

unrouted=""
for service in services/*-service; do
  name=$(basename "$service")
  cluster=$(printf '%s\n' "$CLUSTER_SERVICE" | { grep -P "\t$name\$" || true; } | cut -f1 | head -1)
  [ -z "$cluster" ] && unrouted="$unrouted $name"
done

if [ -z "$unrouted" ]; then
  echo "  ok    all $(ls -d services/*-service | wc -l) services are behind a cluster"
  pass=$((pass + 1))
else
  echo "  FAIL  no gateway cluster points at:$unrouted"
  fail=$((fail + 1))
fi

# `apps/` counts as well as `services/`. One cluster is the member portal: the
# gateway serves it at the root as the platform's public front door, on a
# catch-all with Order 1000 so every /v1 route and the gateway's own /health win
# over it (root CLAUDE.md §8). It is a real destination, just not a service.
dangling=""
while IFS=$'\t' read -r cluster host; do
  [ -z "$host" ] && continue
  [ -d "services/$host" ] || [ -d "apps/$host" ] || dangling="$dangling $cluster->$host"
done <<< "$CLUSTER_SERVICE"

if [ -z "$dangling" ]; then
  echo "  ok    and every cluster points at a service or app that exists"
  pass=$((pass + 1))
else
  echo "  FAIL  a cluster points at no such service:$dangling"
  fail=$((fail + 1))
fi

# ---- §9: at least one smoke check per service, through the gateway ---------

echo
echo "-- every service is called through the gateway by the smoke run --"

uncovered=""

for service in services/*-service; do
  name=$(basename "$service")

  clusters=$(printf '%s\n' "$CLUSTER_SERVICE" | { grep -P "\t$name\$" || true; } | cut -f1)
  [ -z "$clusters" ] && continue

  hits=0
  used=""

  while read -r cluster; do
    [ -z "$cluster" ] && continue
    prefixes=$(printf '%s\n' "$PREFIX_CLUSTER" | { grep -P "\t$cluster\$" || true; } | cut -f1)

    while read -r prefix; do
      [ -z "$prefix" ] && continue
      n=$({ grep -c "GATEWAY/v1/$prefix" "$SMOKE" || true; })
      if [ "$n" -gt 0 ]; then
        hits=$((hits + n))
        used="$used $prefix"
      fi
    done <<< "$prefixes"
  done <<< "$clusters"

  if [ "$hits" -gt 0 ]; then
    printf '  ok    %-26s %3d calls (%s )\n' "$name" "$hits" "$used"
    pass=$((pass + 1))
  else
    printf '  FAIL  %-26s no call through the gateway\n' "$name"
    fail=$((fail + 1))
    uncovered="$uncovered $name"
  fi
done

if [ -n "$uncovered" ]; then
  echo
  echo "  Root CLAUDE.md §9: \"At least one test per service curls its endpoint"
  echo "  through the gateway, not only directly against the service - a route"
  echo "  that works in isolation but isn't actually wired into the gateway is a"
  echo "  common and easy-to-miss failure mode.\""
fi

# ---- every service is actually in CI ---------------------------------------
#
# Two lists in one file, both written by hand: the build/test matrix names a
# solution per service so a failure says which one broke, and the migrations job
# names an Infrastructure project per service. Being absent from either does not
# fail anything - it is simply absent, which is why boli-service sat outside
# both for eleven cycles with 49 tests that ran nowhere but a laptop.

echo
echo "-- every service is in the CI matrix, and in the migrations job --"

missing_from_matrix=""
missing_from_migrations=""

for service in services/*-service; do
  name=$(basename "$service")

  solution=$(ls "$service"/*.sln 2>/dev/null | head -1)
  if [ -n "$solution" ] && ! grep -qF "$solution" "$CI"; then
    missing_from_matrix="$missing_from_matrix $name"
  fi

  # A service with no migrations has no database to check.
  if find "$service/src" -type d -name Migrations | grep -q . \
     && ! grep -qE "services/$name/src/[A-Za-z.]+\.Infrastructure" "$CI"; then
    missing_from_migrations="$missing_from_migrations $name"
  fi
done

if [ -z "$missing_from_matrix" ]; then
  echo "  ok    all $(ls services/*/*.sln | wc -l) solutions are built and tested by CI"
  pass=$((pass + 1))
else
  echo "  FAIL  not in the CI build/test matrix:$missing_from_matrix"
  echo "        Those tests run nowhere but a developer's machine."
  fail=$((fail + 1))
fi

if [ -z "$missing_from_migrations" ]; then
  echo "  ok    and every service with migrations is checked for pending changes"
  pass=$((pass + 1))
else
  echo "  FAIL  not in the CI migrations job:$missing_from_migrations"
  echo "        A model changed without a migration stays invisible until a"
  echo "        deployment fails against a real database."
  fail=$((fail + 1))
fi

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
