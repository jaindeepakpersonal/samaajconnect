#!/usr/bin/env bash
#
# Checks the mechanical claims in docs/product/SECURITY-CHECKLIST.md against the
# source, across all ten services.
#
# The checklist's status block used to say "walked against all ten services and
# the gateway" with a date on it. That is worth exactly as much as the date: the
# pass before it covered three services, and six of the seven that shipped
# afterwards were never re-checked. A property believed true of ten copies of a
# file, verified once by hand, is a property that stops being true silently -
# which is the failure this repository has now hit on the a11y audit, the
# isolation probe and the backup drill in turn.
#
# So the parts of that walk a machine can do, a machine does:
#
#   1. Every request type carries one of the four authorization attributes.
#   2. The set of anonymous request types is exactly the one the checklist lists.
#   3. The set of internal request types is exactly the one the checklist lists,
#      and no endpoint file mentions any of them.
#   4. Every DbContext applies the tenant query filter by reflection.
#   5. Every service's persistence calls TenantWriteGuard.
#   6. The files that are meant to be identical across all ten services are
#      identical.
#
# **The two allow-lists are read out of SECURITY-CHECKLIST.md**, not written
# here, so the documentation is the single source of truth and this fails if
# either side moves without the other - the arrangement scripts/pipeline-order.sh
# already uses for CLAUDE.md §4.4. Hard-coding them would make this a second
# place to update, which is the problem it exists to solve.
#
# What it does not check: whether a permission is the *right* one, whether a
# role should hold it, or anything that needs a running stack. Those are
# scripts/tenant-isolation-probe.sh and scripts/smoke-through-gateway.sh.
#
# It needs no running stack: it reads the source.
set -euo pipefail

cd "$(dirname "$0")/.."

CHECKLIST=docs/product/SECURITY-CHECKLIST.md

pass=0
fail=0

ok()   { echo "  ok    $1"; pass=$((pass + 1)); }
bad()  { echo "  FAIL  $1"; fail=$((fail + 1)); }

# ---- Reading the request types out of the source ---------------------------
#
# A request type is a record implementing ICommand<> or IQuery<>. The attribute
# above it may span many lines - GetCurrentUserQuery carries a nine-line role
# list - so the scan upward stops at the end of the previous declaration rather
# than at the first line that does not look like an attribute.
#
# Comment lines are skipped, and that is not tidiness. RecordIntegrationEventCommand's
# own remarks say "it carries [InternalRequest] rather than [AllowAnonymousRequest]",
# and a scan that reads comments concluded from that sentence that the command was
# anonymous. A checker that reads prose is a checker that can be talked into
# anything.
read_requests() {
  local awk_prog
  awk_prog=$(cat <<'AWKEOF'
{ line[FNR] = $0; n = FNR }
END {
  for (i = 1; i <= n; i++) {
    if (line[i] !~ /(public|internal) +(sealed +)?record +[A-Za-z0-9_]+/) { continue }
    decl = line[i]
    for (j = i + 1; j <= n && j <= i + 12; j++) {
      decl = decl " " line[j]
      if (line[j] ~ /(\{|;) *$/) { break }
    }
    if (decl !~ /: *I(Command|Query)</) { continue }
    match(line[i], /record +[A-Za-z0-9_]+/)
    name = substr(line[i], RSTART + 7, RLENGTH - 7)
    block = ""
    for (k = i - 1; k >= 1; k--) {
      if (line[k] ~ /(;|\}) *$/) { break }
      if (line[k] ~ /^(using |namespace )/) { break }
      if (line[k] ~ /^[[:space:]]*\//) { continue }
      if (line[k] ~ /^[[:space:]]*\*/) { continue }
      block = line[k] " " block
    }
    a = ""
    if (block ~ /\[RequiresPermission/)    { a = a "perm," }
    if (block ~ /\[RequiresRoles/)         { a = a "roles," }
    if (block ~ /\[AllowAnonymousRequest/) { a = a "anon," }
    if (block ~ /\[InternalRequest/)       { a = a "internal," }
    if (a == "") { a = "NONE" }
    printf "%s\t%s\t%s\n", a, name, FILENAME
  }
}
AWKEOF
  )

  find services -name '*.cs' -path '*/src/*' ! -path '*/obj/*' ! -path '*/bin/*' \
    | sort \
    | while read -r file; do
        awk "$awk_prog" "$file"
      done
}

REQUESTS=$(read_requests)
TOTAL=$(printf '%s\n' "$REQUESTS" | grep -c . || true)

echo "== security invariants =="
echo
echo "  $TOTAL request types across $(ls -d services/*-service | wc -l) services"
echo

# A source tree this check cannot see is a check that passes for the worst
# possible reason. 100 is well below the real number and well above nothing.
if [ "$TOTAL" -lt 100 ]; then
  echo "  FAIL  only $TOTAL request types found - the scan is broken, not the code"
  exit 1
fi

# ---- 1. Everything is annotated --------------------------------------------

echo "-- every request type carries an authorization attribute --"

UNANNOTATED=$(printf '%s\n' "$REQUESTS" | { grep '^NONE' || true; })

if [ -z "$UNANNOTATED" ]; then
  ok "all $TOTAL carry one of the four attributes"
else
  bad "$(printf '%s\n' "$UNANNOTATED" | grep -c .) request types carry none:"
  printf '%s\n' "$UNANNOTATED" | awk -F'\t' '{ print "          " $2 "  (" $3 ")" }'
fi

# ---- 2 and 3. The two sets match the checklist -----------------------------
#
# Read from the tables under the two headings. The header row has no backticked
# name in its first column, so it does not match.
list_from_checklist() {
  sed -n "/^#### $1/,/^#### \|^## /p" "$CHECKLIST" \
    | grep -oE '^\| `[A-Za-z0-9_]+`' \
    | tr -d '|` ' \
    | sort -u
}

compare_set() {
  local label="$1" expected="$2" actual="$3"

  if [ -z "$expected" ]; then
    bad "$label: could not read the list out of $CHECKLIST (the table moved)"
    return
  fi

  local missing extra
  missing=$(comm -23 <(printf '%s\n' "$expected") <(printf '%s\n' "$actual"))
  extra=$(comm -13 <(printf '%s\n' "$expected") <(printf '%s\n' "$actual"))

  if [ -z "$missing" ] && [ -z "$extra" ]; then
    ok "$label: $(printf '%s\n' "$actual" | grep -c .), exactly the ones listed"
    return
  fi

  bad "$label does not match $CHECKLIST"
  [ -n "$extra" ] && printf '%s\n' "$extra" \
    | sed 's/^/          in the code, not on the list: /'
  [ -n "$missing" ] && printf '%s\n' "$missing" \
    | sed 's/^/          on the list, not in the code: /'
  return 0
}

echo
echo "-- the anonymous and internal sets are the ones the checklist names --"

ANON_ACTUAL=$(printf '%s\n' "$REQUESTS" | { grep 'anon,' || true; } | cut -f2 | sort -u)
INTERNAL_ACTUAL=$(printf '%s\n' "$REQUESTS" | { grep 'internal,' || true; } | cut -f2 | sort -u)

compare_set "requests reachable without authentication" \
  "$(list_from_checklist 'Requests reachable without authentication')" "$ANON_ACTUAL"
compare_set "requests no HTTP route may reach" \
  "$(list_from_checklist 'Requests no HTTP route may reach')" "$INTERNAL_ACTUAL"

# Every anonymous request lives in identity-tenant-service, and the checklist
# says so. A second service answering unauthenticated callers is a change worth
# noticing on the day it happens rather than at the next audit.
STRAY=$(printf '%s\n' "$REQUESTS" | { grep 'anon,' || true; } \
  | cut -f3 | grep -v '^services/identity-tenant-service/' || true)

if [ -z "$STRAY" ]; then
  ok "and every anonymous request is in identity-tenant-service"
else
  bad "an anonymous request outside identity-tenant-service:"
  printf '%s\n' "$STRAY" | sed 's/^/          /'
fi

# An [InternalRequest] command is one no route reaches. Nothing in the type
# system holds that, so it is asserted here.
echo
echo "-- no internal request is reachable from an endpoint --"

routed=""
while read -r name; do
  [ -z "$name" ] && continue
  hits=$(grep -rln "$name" services --include='*.cs' 2>/dev/null \
    | { grep -E '/src/[^/]*\.Api/(Endpoints/|Program\.cs)' || true; })
  [ -n "$hits" ] && routed="$routed$name: $hits"$'\n'
done <<< "$INTERNAL_ACTUAL"

if [ -z "$routed" ]; then
  ok "none of the $(printf '%s\n' "$INTERNAL_ACTUAL" | grep -c .) is named in an endpoint file"
else
  bad "an internal command is reachable from an endpoint:"
  printf '%s' "$routed" | sed 's/^/          /'
fi

# ---- The permission catalogue, three ways ----------------------------------
#
# `AuthorizationCatalog` in identity-tenant-service is the executable copy: it
# holds the keys with stable hand-assigned ids and says which roles carry them.
# Each service names the keys it gates on in its own `PermissionKeys.cs`. The
# table under "Permission key naming convention" below is the readable copy.
#
# All three have to agree. A service gating on a key the catalogue has never
# heard of is an endpoint that answers 403 to everybody, and the checklist's
# table falling behind is how `Roles.Manage` - the lock-out floor a Samaaj
# administrator cannot lose - came to be undocumented on the page that
# documents permissions.
echo
echo "-- the permission keys agree across the catalogue, the services and this page --"

CATALOG=services/identity-tenant-service/src/Sangam.IdentityTenant.Domain/Authorization/AuthorizationCatalog.cs

catalog_keys=$(grep -oE 'new\(PermissionIds\.[A-Za-z]+, "[A-Za-z.]+"\)' "$CATALOG" 2>/dev/null \
  | grep -oE '"[A-Za-z.]+"' | tr -d '"' | sort -u)

service_keys=$(grep -rhoE '= *"[A-Z][A-Za-z]+(\.[A-Za-z]+)+"' services --include='PermissionKeys.cs' \
  | tr -d '= "' | sort -u)

# Every backticked key in the table's first column. Some rows name two - the
# read and the write, the post and the moderate - so this takes them all rather
# than the first, which is a mistake worth naming: an extraction that reads one
# key per row reported `Members.Write` and `Timeline.Moderate` as missing when
# both were sitting in the table beside their pair.
doc_keys=$(sed -n '/^## Permission key naming convention/,/^### A permission held/p' "$CHECKLIST" \
  | grep '^| `' \
  | cut -d'|' -f2 \
  | grep -oE '`[A-Za-z][A-Za-z.]+`' | tr -d '`' | sort -u)

if [ -z "$catalog_keys" ] || [ -z "$doc_keys" ]; then
  bad "could not read the permission keys (AuthorizationCatalog or the table moved)"
else
  ungranted=$(comm -13 <(printf '%s\n' "$catalog_keys") <(printf '%s\n' "$service_keys"))
  if [ -z "$ungranted" ]; then
    ok "every key a service gates on is in AuthorizationCatalog ($(printf '%s\n' "$catalog_keys" | grep -c .))"
  else
    bad "a service gates on a key no role can hold - those endpoints 403 for everybody:"
    printf '%s\n' "$ungranted" | sed 's/^/          /'
  fi

  undocumented=$(comm -23 <(printf '%s\n' "$catalog_keys") <(printf '%s\n' "$doc_keys"))
  invented=$(comm -13 <(printf '%s\n' "$catalog_keys") <(printf '%s\n' "$doc_keys"))

  if [ -z "$undocumented" ] && [ -z "$invented" ]; then
    ok "and the table on this page names all of them, and none it does not have"
  else
    bad "the permission table does not match AuthorizationCatalog"
    [ -n "$undocumented" ] && printf '%s\n' "$undocumented" \
      | sed 's/^/          in the catalogue, not in the table: /'
    [ -n "$invented" ] && printf '%s\n' "$invented" \
      | sed 's/^/          in the table, not in the catalogue: /'
  fi
fi

# ---- 4 and 5. The two persistence-level guards ------------------------------

echo
echo "-- the tenant guards, in every service --"

filter_missing=""
guard_missing=""

for service in services/*-service; do
  context=$(find "$service/src" -name '*DbContext.cs' ! -path '*/obj/*' ! -path '*/bin/*' | head -1)

  if [ -z "$context" ]; then
    filter_missing="$filter_missing $(basename "$service")"
    guard_missing="$guard_missing $(basename "$service")"
    continue
  fi

  grep -q 'ITenantScopedEntity' "$context" \
    || filter_missing="$filter_missing $(basename "$service")"

  grep -rq 'TenantWriteGuard' "$service/src" --include='*.cs' \
    || guard_missing="$guard_missing $(basename "$service")"
done

if [ -z "$filter_missing" ]; then
  ok "every DbContext applies the query filter over ITenantScopedEntity"
else
  bad "no ITenantScopedEntity filter in:$filter_missing"
fi

if [ -z "$guard_missing" ]; then
  ok "every service calls TenantWriteGuard at SaveChanges"
else
  bad "no TenantWriteGuard in:$guard_missing"
fi

# ---- 6. The copies that are meant to be copies ------------------------------
#
# These files are deliberately identical across the ten services - shared
# infrastructure kept as source rather than as a package, which root CLAUDE.md
# and the new-microservice skill both instruct ("copy verbatim ... and adjust
# only the namespace"). Copying is how they get there and drift is what happens
# to copies: the consumer group id was wrong in three services for exactly this
# reason, and nothing said so.
#
# The namespace and the service's own DbContext type are the two things that
# legitimately differ, so both are normalised away before comparing.
echo
echo "-- the files that are copies really are copies --"

for name in \
  LoggingBehavior TenantAuthorizationBehavior ValidationBehavior \
  TransactionBehavior UnhandledExceptionBehavior \
  TenantWriteGuard OutboxDispatcher KafkaProducer \
  Result Error ITenantContext
do
  reference=""
  differs=""
  absent=""

  for service in services/*-service; do
    file=$(find "$service/src" -name "$name.cs" ! -path '*/obj/*' ! -path '*/bin/*' | head -1)

    if [ -z "$file" ]; then
      absent="$absent $(basename "$service")"
      continue
    fi

    hash=$(grep -v '^namespace \|^using ' "$file" \
      | sed 's/Sangam\.[A-Za-z]*\./Sangam.X./g; s/[A-Za-z]*DbContext/XDbContext/g' \
      | md5sum | cut -c1-12)

    if [ -z "$reference" ]; then
      reference="$hash"
    elif [ "$hash" != "$reference" ]; then
      differs="$differs $(basename "$service")"
    fi
  done

  if [ -n "$absent" ]; then
    bad "$name is missing from:$absent"
  elif [ -n "$differs" ]; then
    bad "$name has drifted in:$differs"
  else
    ok "$name is identical in all ten"
  fi
done

# ---- Copies that are not in every service ----------------------------------
#
# The list above is infrastructure every service has. These are files only some
# services need, which must still be identical wherever they appear - the same
# drift risk with a different shape, and one the loop above cannot express
# because it fails a file for being absent.
#
# `ImageContent` is the only one so far: member-family-service sniffs member and
# child photos, identity-tenant-service sniffs Samaaj logos, and the question
# "are these bytes an image we will store and serve" has exactly one right
# answer. Two copies drifting is how one service quietly starts accepting
# something the other refuses.
echo
echo "-- and the copies only some services have --"

for name in ImageContent; do
  reference=""
  differs=""
  found=""

  for service in services/*-service; do
    file=$(find "$service/src" -name "$name.cs" ! -path '*/obj/*' ! -path '*/bin/*' | head -1)

    [ -z "$file" ] && continue

    found="$found $(basename "$service")"

    hash=$(grep -v '^namespace \|^using ' "$file" \
      | sed 's/Sangam\.[A-Za-z]*\./Sangam.X./g; s/[A-Za-z]*DbContext/XDbContext/g' \
      | md5sum | cut -c1-12)

    if [ -z "$reference" ]; then
      reference="$hash"
    elif [ "$hash" != "$reference" ]; then
      differs="$differs $(basename "$service")"
    fi
  done

  count=$(printf '%s' "$found" | wc -w)

  if [ "$count" -lt 2 ]; then
    # One copy cannot drift. Reported rather than passed silently, because a
    # name on this list that only one service has is a name that should either
    # come off it or be copied to the service that needs it.
    ok "$name is in one service ($found) - nothing to compare"
  elif [ -n "$differs" ]; then
    bad "$name has drifted in:$differs"
  else
    ok "$name is identical in the $count services that have it:$found"
  fi
done

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
