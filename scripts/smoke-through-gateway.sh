#!/usr/bin/env bash
#
# Exercises the platform end to end *through the gateway*, which is what
# CLAUDE.md section 9 asks for: a route that works when curled directly at a
# service but was never wired into YARP is a common and easy-to-miss failure.
#
# Assumes `docker compose up -d --build` has finished and the stack is healthy.
#
# Subdomains are supplied with an explicit Host header rather than through DNS,
# so this works on a laptop with no /etc/hosts entries and no wildcard record.
set -euo pipefail

GATEWAY="${GATEWAY:-http://localhost:8080}"
APEX_HOST="${APEX_HOST:-samaajconnect.com}"
ADMIN_HOST="${ADMIN_HOST:-admin.samaajconnect.com}"
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
  grep -o "\"$1\":\"[^\"]*\"" | head -1 | cut -d'"' -f4
}

echo "Gateway smoke test against $GATEWAY"

echo
echo "Gateway itself"
check "health" 200 "$(status "$GATEWAY/health")"

echo
echo "Tenant resolution"
check "unknown Samaaj subdomain is 404" 404 \
  "$(status -H "Host: no-such-samaj.$APEX_HOST" "$GATEWAY/v1/identity/me")"

echo
echo "Super Admin signs in through the gateway (apex host, no Samaaj)"
ADMIN_TOKEN=$(curl -s -X POST "$GATEWAY/v1/identity/login" \
  -H "Host: $APEX_HOST" -H 'Content-Type: application/json' \
  -d "{\"mobileOrEmail\":\"$SUPERADMIN\",\"password\":\"$SUPERADMIN_PASSWORD\"}" | json_field accessToken)

if [ -z "$ADMIN_TOKEN" ]; then
  echo "  FAIL  could not sign in as $SUPERADMIN through the gateway"
  exit 1
fi
echo "  ok    got a Super Admin token"
pass=$((pass + 1))

echo
echo "Super Admin creates and activates a Samaaj through the gateway"
CREATE_BODY=$(curl -s -X POST "$GATEWAY/v1/identity/tenants" \
  -H "Host: $ADMIN_HOST" -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -d "{\"name\":\"Smoke Samaaj\",\"slug\":\"$SLUG\",\"enabledModules\":[\"Pathshala\"]}")

TENANT_ID=$(printf '%s' "$CREATE_BODY" | json_field id)

if [ -z "$TENANT_ID" ]; then
  echo "  note  Samaaj '$SLUG' already exists; resolving it instead"
  TENANT_ID=$(curl -s -H "Host: $APEX_HOST" "$GATEWAY/v1/identity/tenants/$SLUG" | json_field id)
else
  echo "  ok    created Samaaj $TENANT_ID"
  pass=$((pass + 1))
fi

check "activate" 200 "$(status -X PATCH "$GATEWAY/v1/identity/tenants/$TENANT_ID/status" \
  -H "Host: $ADMIN_HOST" -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $ADMIN_TOKEN" -d '{"status":"Active"}')"

echo
echo "Member registers and signs in through the gateway"
REGISTER_STATUS=$(status -X POST "$GATEWAY/v1/identity/register" \
  -H "Host: $APEX_HOST" -H 'Content-Type: application/json' \
  -d "{\"tenantSlug\":\"$SLUG\",\"fullName\":\"Smoke Member\",\"mobileOrEmail\":\"$MEMBER\",\"password\":\"$MEMBER_PASSWORD\"}")

if [ "$REGISTER_STATUS" = "409" ]; then
  echo "  note  member already registered from an earlier run"
else
  check "register" 201 "$REGISTER_STATUS"
fi

MEMBER_TOKEN=$(curl -s -X POST "$GATEWAY/v1/identity/login" \
  -H "Host: $APEX_HOST" -H 'Content-Type: application/json' \
  -d "{\"mobileOrEmail\":\"$MEMBER\",\"password\":\"$MEMBER_PASSWORD\"}" | json_field accessToken)

if [ -z "$MEMBER_TOKEN" ]; then
  echo "  FAIL  member could not sign in through the gateway"
  fail=$((fail + 1))
else
  echo "  ok    got a member token"
  pass=$((pass + 1))
fi

echo
echo "Routing to each service, on the Samaaj subdomain"
check "identity /me" 200 "$(status -H "Host: $SLUG.$APEX_HOST" \
  -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/identity/me")"

check "audit log is refused to a member" 403 "$(status -H "Host: $SLUG.$APEX_HOST" \
  -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/audit/logs")"

check "audit log is served to a Super Admin" 200 "$(status -H "Host: $ADMIN_HOST" \
  -H "Authorization: Bearer $ADMIN_TOKEN" "$GATEWAY/v1/audit/logs")"

check "notifications" 200 "$(status -H "Host: $SLUG.$APEX_HOST" \
  -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/notifications")"

echo
echo "Header forgery"
check "a forged tenant header does not change the answer" 200 \
  "$(status -H "Host: $SLUG.$APEX_HOST" -H "X-Tenant-Id: 11111111-1111-1111-1111-111111111111" \
     -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/identity/me")"

check "an override from a Samaaj subdomain is refused" 403 \
  "$(status -H "Host: $SLUG.$APEX_HOST" -H "X-Tenant-Override-Id: $TENANT_ID" \
     -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/identity/me")"

check "an override from a member on the admin host is refused" 403 \
  "$(status -H "Host: $ADMIN_HOST" -H "X-Tenant-Override-Id: $TENANT_ID" \
     -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/identity/me")"

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
