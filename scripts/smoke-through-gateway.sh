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
echo "Consent notice, shown before anyone registers"
NOTICE_VERSION=$(curl -s "$GATEWAY/v1/identity/consent-notice" | json_field version)

if [ -n "$NOTICE_VERSION" ]; then
  echo "  ok    consent notice is public (version $NOTICE_VERSION)"
  pass=$((pass + 1))
else
  echo "  FAIL  could not read the consent notice"
  fail=$((fail + 1))
fi

check "registering without consent is refused" 400 \
  "$(status -X POST "$GATEWAY/v1/identity/register" -H 'Content-Type: application/json' \
     -d "{\"tenantSlug\":\"$SLUG\",\"fullName\":\"No Consent\",\"mobileOrEmail\":\"no-consent@example.com\",\"password\":\"$MEMBER_PASSWORD\",\"consentedPurposes\":[],\"noticeVersion\":\"$NOTICE_VERSION\"}")"

echo
echo "Member registers, choosing their Samaaj from the directory"
REGISTER_STATUS=$(status -X POST "$GATEWAY/v1/identity/register" \
  -H 'Content-Type: application/json' \
  -d "{\"tenantSlug\":\"$SLUG\",\"fullName\":\"Smoke Member\",\"mobileOrEmail\":\"$MEMBER\",\"password\":\"$MEMBER_PASSWORD\",\"consentedPurposes\":[\"Membership\"],\"noticeVersion\":\"$NOTICE_VERSION\"}")

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

# Either answer is correct: 404 on a clean database, 200 on a re-run, because
# the conversion section below gives this member a family.
FAMILY_LOOKUP=$(status -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/families/mine")

if [ "$FAMILY_LOOKUP" = "404" ] || [ "$FAMILY_LOOKUP" = "200" ]; then
  echo "  ok    family lookup routes ($FAMILY_LOOKUP)"
  pass=$((pass + 1))
else
  echo "  FAIL  family lookup routes (got $FAMILY_LOOKUP)"
  fail=$((fail + 1))
fi

check "children of a member with no family" 200 "$(status -H "Authorization: Bearer $MEMBER_TOKEN" \
  "$GATEWAY/v1/children")"

check "conversion queue is refused to a member" 403 "$(status -H "Authorization: Bearer $MEMBER_TOKEN" \
  "$GATEWAY/v1/children/conversion-requests")"


echo
echo "Adult-child conversion, end to end across three services"

# A fresh identifier per run: the flow creates a real account, and re-running
# must not collide with the one the last run made.
CHILD_EMAIL="converted-$(date +%s)@example.com"
CHILD_PASSWORD="a-long-enough-password"
ADMIN_TENANT_HEADER="X-Tenant-Override-Id: $TENANT_ID"

# The member may already head a family from an earlier run.
FAMILY_STATUS=$(status -X POST "$GATEWAY/v1/families" -H "Authorization: Bearer $MEMBER_TOKEN")

if [ "$FAMILY_STATUS" = "201" ] || [ "$FAMILY_STATUS" = "409" ]; then
  echo "  ok    member heads a family ($FAMILY_STATUS)"
  pass=$((pass + 1))
else
  echo "  FAIL  member heads a family (got $FAMILY_STATUS)"
  fail=$((fail + 1))
fi

DOB=$(date -d '20 years ago' +%Y-%m-%d 2>/dev/null || date -v-20y +%Y-%m-%d)

CHILD_ID=$(curl -s -X POST "$GATEWAY/v1/children" \
  -H 'Content-Type: application/json' -H "Authorization: Bearer $MEMBER_TOKEN" \
  -d "{\"fullName\":\"Aarav Jain\",\"dateOfBirth\":\"$DOB\",\"gender\":\"Male\"}" | json_field id)

if [ -n "$CHILD_ID" ]; then
  echo "  ok    added a child who has turned 18"
  pass=$((pass + 1))
else
  echo "  FAIL  could not add a child"
  fail=$((fail + 1))
fi

REQUEST_ID=$(curl -s -X POST "$GATEWAY/v1/children/$CHILD_ID/conversion" \
  -H 'Content-Type: application/json' -H "Authorization: Bearer $MEMBER_TOKEN" \
  -d "{\"mobileOrEmail\":\"$CHILD_EMAIL\"}" | json_field id)

if [ -n "$REQUEST_ID" ]; then
  echo "  ok    family head requested conversion"
  pass=$((pass + 1))
else
  echo "  FAIL  could not request conversion"
  fail=$((fail + 1))
fi

check "the head cannot approve their own request" 403 \
  "$(status -X POST "$GATEWAY/v1/children/conversion-requests/$REQUEST_ID/decide" \
     -H 'Content-Type: application/json' -H "Authorization: Bearer $MEMBER_TOKEN" \
     -d '{"approve":true}')"

check "a Samaaj admin approves it" 200 \
  "$(status -X POST "$GATEWAY/v1/children/conversion-requests/$REQUEST_ID/decide" \
     -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN_TOKEN" \
     -H "$ADMIN_TENANT_HEADER" -d '{"approve":true,"note":"Verified in person"}')"

# identity-tenant-service consumes the approval and creates the account.
NEW_USER_ID=""
for attempt in $(seq 1 30); do
  NEW_USER_ID=$(curl -s -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
    "$GATEWAY/v1/identity/activations/pending" \
    | tr '}' '\n' | { grep "$CHILD_EMAIL" || true; } | json_field userId)

  [ -n "$NEW_USER_ID" ] && break
  sleep 2
done

if [ -n "$NEW_USER_ID" ]; then
  echo "  ok    identity created the account over Kafka, awaiting activation"
  pass=$((pass + 1))
else
  echo "  FAIL  the account never appeared on the pending list"
  fail=$((fail + 1))
fi

ACTIVATION_CODE=$(curl -s -X POST "$GATEWAY/v1/identity/activations/$NEW_USER_ID/code" \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" | json_field code)

if [ -n "$ACTIVATION_CODE" ]; then
  echo "  ok    admin issued a one-time activation code"
  pass=$((pass + 1))
else
  echo "  FAIL  could not issue an activation code"
  fail=$((fail + 1))
fi

check "the new member redeems it and sets a password" 200 \
  "$(status -X POST "$GATEWAY/v1/identity/activations/redeem" -H 'Content-Type: application/json' \
     -d "{\"mobileOrEmail\":\"$CHILD_EMAIL\",\"code\":\"$ACTIVATION_CODE\",\"password\":\"$CHILD_PASSWORD\"}")"

check "the code cannot be redeemed twice" 403 \
  "$(status -X POST "$GATEWAY/v1/identity/activations/redeem" -H 'Content-Type: application/json' \
     -d "{\"mobileOrEmail\":\"$CHILD_EMAIL\",\"code\":\"$ACTIVATION_CODE\",\"password\":\"$CHILD_PASSWORD\"}")"

check "the converted child can now sign in" 200 \
  "$(status -X POST "$GATEWAY/v1/identity/login" -H 'Content-Type: application/json' \
     -d "{\"mobileOrEmail\":\"$CHILD_EMAIL\",\"password\":\"$CHILD_PASSWORD\"}")"

# member-family consumes the activation and closes the loop.
converted=0
for attempt in $(seq 1 30); do
  if curl -s -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/children" \
     | tr '}' '\n' | grep -q '"status":"Converted"'; then
    converted=1
    break
  fi
  sleep 2
done
check "the child record is marked Converted" 1 "$converted"
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
