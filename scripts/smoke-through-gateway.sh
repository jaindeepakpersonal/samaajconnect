#!/usr/bin/env bash
#
# Exercises the platform end to end *through the gateway*, which is what
# CLAUDE.md section 9 asks for: a route that works when curled directly at a
# service but was never wired into YARP is a common and easy-to-miss failure.
#
# Assumes `docker compose up -d --build` has finished.
#
# One domain, no Host headers: a member signs in and the token decides which
# Samaaj they belong to (root CLAUDE.md section 6).
set -euo pipefail

GATEWAY="${GATEWAY:-http://localhost:8080}"
SLUG="${SLUG:-smoke-samaj}"
SUPERADMIN="${SUPERADMIN:-superadmin@samaajconnect.local}"
SUPERADMIN_PASSWORD="${SUPERADMIN_PASSWORD:-change-me-immediately}"
MEMBER="${MEMBER:-smoke-member@example.com}"
MEMBER_PASSWORD="${MEMBER_PASSWORD:-a-long-enough-password}"

pass=0
fail=0

check() {
  local label="$1" expected="$2" actual="$3"
  if [ "$expected" = "$actual" ]; then
    echo "  ok    $label ($actual)"
    pass=$((pass + 1))
  else
    echo "  FAIL  $label (expected $expected, got $actual)"
    fail=$((fail + 1))
  fi
}

status() {
  curl -s -o /dev/null -w '%{http_code}' "$@"
}

json_field() {
  # Deliberately grep-based: this script must run anywhere curl runs, without
  # requiring jq to be installed.
  #
  # The `|| true` matters: with `set -o pipefail`, a grep that matches nothing
  # fails the whole pipeline and errexit kills the script. That only bites on a
  # re-run, where creating an existing Samaaj returns a problem document with
  # no id - so the script died silently exactly when someone ran it twice.
  { grep -o "\"$1\":\"[^\"]*\"" || true; } | head -1 | cut -d'"' -f4
}

wait_for_stack() {
  # The services have no healthcheck of their own (the aspnet image ships no
  # curl), so readiness is established here rather than by compose --wait.
  #
  # Waiting on the gateway alone is not enough: it comes up before the services
  # behind it have finished migrating, and the first requests then fail with a
  # 502 that looks like a routing bug. So this waits on a real route.
  local attempt=0
  until [ "$(status "$GATEWAY/health")" = "200" ] \
     && [ "$(status "$GATEWAY/v1/identity/tenants/directory")" = "200" ]; do
    attempt=$((attempt + 1))
    if [ "$attempt" -ge 90 ]; then
      echo "  FAIL  the stack did not become ready"
      exit 1
    fi
    sleep 2
  done
}

echo "Gateway smoke test against $GATEWAY"

wait_for_stack

echo
echo "Gateway itself"
check "health" 200 "$(status "$GATEWAY/health")"

echo
echo "Anonymous surface"
check "unauthenticated /me is refused" 401 "$(status "$GATEWAY/v1/identity/me")"
check "the Samaaj directory is public" 200 "$(status "$GATEWAY/v1/identity/tenants/directory")"

echo
echo "Super Admin signs in"
ADMIN_TOKEN=$(curl -s -X POST "$GATEWAY/v1/identity/login" \
  -H 'Content-Type: application/json' \
  -d "{\"mobileOrEmail\":\"$SUPERADMIN\",\"password\":\"$SUPERADMIN_PASSWORD\"}" | json_field accessToken)

if [ -z "$ADMIN_TOKEN" ]; then
  echo "  FAIL  could not sign in as $SUPERADMIN through the gateway"
  exit 1
fi
echo "  ok    got a Super Admin token"
pass=$((pass + 1))

echo
echo "Super Admin creates and activates a Samaaj"
CREATE_BODY=$(curl -s -X POST "$GATEWAY/v1/identity/tenants" \
  -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN_TOKEN" \
  -d "{\"name\":\"Smoke Samaaj\",\"slug\":\"$SLUG\",\"enabledModules\":[\"Pathshala\"]}")

TENANT_ID=$(printf '%s' "$CREATE_BODY" | json_field id)

if [ -z "$TENANT_ID" ]; then
  echo "  note  Samaaj '$SLUG' already exists; resolving it instead"
  TENANT_ID=$(curl -s "$GATEWAY/v1/identity/tenants/$SLUG" | json_field id)
else
  echo "  ok    created Samaaj $TENANT_ID"
  pass=$((pass + 1))
fi

check "activate" 200 "$(status -X PATCH "$GATEWAY/v1/identity/tenants/$TENANT_ID/status" \
  -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN_TOKEN" \
  -d '{"status":"Active"}')"

echo
echo "Member registers, choosing their Samaaj from the directory"
REGISTER_STATUS=$(status -X POST "$GATEWAY/v1/identity/register" \
  -H 'Content-Type: application/json' \
  -d "{\"tenantSlug\":\"$SLUG\",\"fullName\":\"Smoke Member\",\"mobileOrEmail\":\"$MEMBER\",\"password\":\"$MEMBER_PASSWORD\"}")

if [ "$REGISTER_STATUS" = "409" ]; then
  echo "  note  member already registered from an earlier run"
else
  check "register" 201 "$REGISTER_STATUS"
fi

echo
echo "Member signs in and the token decides their Samaaj"
MEMBER_LOGIN=$(curl -s -X POST "$GATEWAY/v1/identity/login" \
  -H 'Content-Type: application/json' \
  -d "{\"mobileOrEmail\":\"$MEMBER\",\"password\":\"$MEMBER_PASSWORD\"}")

MEMBER_TOKEN=$(printf '%s' "$MEMBER_LOGIN" | json_field accessToken)
RESOLVED_SLUG=$(printf '%s' "$MEMBER_LOGIN" | json_field tenantSlug)

if [ -z "$MEMBER_TOKEN" ]; then
  echo "  FAIL  member could not sign in through the gateway"
  fail=$((fail + 1))
else
  echo "  ok    got a member token"
  pass=$((pass + 1))
fi

check "login resolved the Samaaj without being told" "$SLUG" "$RESOLVED_SLUG"

echo
echo "Routing to each service"
check "identity /me" 200 "$(status -H "Authorization: Bearer $MEMBER_TOKEN" \
  "$GATEWAY/v1/identity/me")"

check "audit log is refused to a member" 403 "$(status -H "Authorization: Bearer $MEMBER_TOKEN" \
  "$GATEWAY/v1/audit/logs")"

check "audit log is served to a Super Admin overriding into the Samaaj" 200 \
  "$(status -H "Authorization: Bearer $ADMIN_TOKEN" \
     -H "X-Tenant-Override-Id: $TENANT_ID" "$GATEWAY/v1/audit/logs")"

check "notifications" 200 "$(status -H "Authorization: Bearer $MEMBER_TOKEN" \
  "$GATEWAY/v1/notifications")"

echo
echo "Member profile, created by the registration event rather than by a call"
profile_ready=0
for attempt in $(seq 1 30); do
  if [ "$(status -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/members/me")" = "200" ]; then
    profile_ready=1
    break
  fi
  sleep 2
done
check "profile arrives over Kafka" 1 "$profile_ready"

check "member directory" 200 "$(status -H "Authorization: Bearer $MEMBER_TOKEN" \
  "$GATEWAY/v1/members")"

check "family before joining one" 404 "$(status -H "Authorization: Bearer $MEMBER_TOKEN" \
  "$GATEWAY/v1/families/mine")"

check "children of a member with no family" 200 "$(status -H "Authorization: Bearer $MEMBER_TOKEN" \
  "$GATEWAY/v1/children")"

check "conversion queue is refused to a member" 403 "$(status -H "Authorization: Bearer $MEMBER_TOKEN" \
  "$GATEWAY/v1/children/conversion-requests")"

echo
echo "Header forgery"
check "a forged tenant header does not change the answer" 200 \
  "$(status -H "X-Tenant-Id: 11111111-1111-1111-1111-111111111111" \
     -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/identity/me")"

check "an override from a member is refused" 403 \
  "$(status -H "X-Tenant-Override-Id: $TENANT_ID" \
     -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/identity/me")"

check "an override from an anonymous caller is refused" 403 \
  "$(status -H "X-Tenant-Override-Id: $TENANT_ID" "$GATEWAY/v1/members")"

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
