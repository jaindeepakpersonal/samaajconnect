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
sign_in_super_admin() {
  curl -s -X POST "$GATEWAY/v1/identity/login" \
    -H 'Content-Type: application/json' \
    -d "{\"mobileOrEmail\":\"$SUPERADMIN\",\"password\":\"$SUPERADMIN_PASSWORD\"}" \
    | json_field accessToken
}

ADMIN_TOKEN=$(sign_in_super_admin)

# The rate-limit section at the end of this script deliberately exhausts the
# credential window, so a re-run inside the following minute starts against a
# 429. Waiting it out beats failing on a condition the previous run created.
if [ -z "$ADMIN_TOKEN" ]; then
  probe=$(status -X POST "$GATEWAY/v1/identity/login" -H 'Content-Type: application/json' \
    -d "{\"mobileOrEmail\":\"$SUPERADMIN\",\"password\":\"$SUPERADMIN_PASSWORD\"}")

  if [ "$probe" = "429" ]; then
    echo "  ..    rate-limited by a previous run; waiting for the window to reset"
    sleep 62
    ADMIN_TOKEN=$(sign_in_super_admin)
  fi
fi

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

CHILD_NOTICE=$(curl -s -H "Authorization: Bearer $MEMBER_TOKEN" \
  "$GATEWAY/v1/children/data-notice" | json_field version)

check "the child data notice is available before asking" 200 \
  "$(status -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/children/data-notice")"

check "a child cannot be added without parental consent" 400 \
  "$(status -X POST "$GATEWAY/v1/children" -H 'Content-Type: application/json' \
     -H "Authorization: Bearer $MEMBER_TOKEN" \
     -d "{\"fullName\":\"No Consent\",\"dateOfBirth\":\"$DOB\",\"gender\":\"Male\",\"parentalConsentGiven\":false,\"noticeVersion\":\"$CHILD_NOTICE\"}")"

CHILD_ID=$(curl -s -X POST "$GATEWAY/v1/children" \
  -H 'Content-Type: application/json' -H "Authorization: Bearer $MEMBER_TOKEN" \
  -d "{\"fullName\":\"Aarav Jain\",\"dateOfBirth\":\"$DOB\",\"gender\":\"Male\",\"parentalConsentGiven\":true,\"noticeVersion\":\"$CHILD_NOTICE\"}" | json_field id)

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
echo "DPDP: the three data exports, and the grievance contact"
check "identity export" 200 "$(status -H "Authorization: Bearer $MEMBER_TOKEN" \
  "$GATEWAY/v1/identity/me/data-export")"

check "member-family export" 200 "$(status -H "Authorization: Bearer $MEMBER_TOKEN" \
  "$GATEWAY/v1/members/me/data-export")"

check "audit export" 200 "$(status -H "Authorization: Bearer $MEMBER_TOKEN" \
  "$GATEWAY/v1/audit/me/data-export")"

check "withdrawing an optional consent" 200 \
  "$(status -X POST "$GATEWAY/v1/identity/me/consents/Communications/withdraw" \
     -H "Authorization: Bearer $MEMBER_TOKEN")"

check "the membership consent cannot be withdrawn piecemeal" 409 \
  "$(status -X POST "$GATEWAY/v1/identity/me/consents/Membership/withdraw" \
     -H "Authorization: Bearer $MEMBER_TOKEN")"

check "naming the grievance contact" 200 \
  "$(status -X PUT "$GATEWAY/v1/identity/tenants/$TENANT_ID/grievance-contact" \
     -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN_TOKEN" \
     -H "$ADMIN_TENANT_HEADER" \
     -d '{"name":"Ravi Shah","email":"grievances@example.com","phone":null}')"

GRIEVANCE=$(curl -s "$GATEWAY/v1/identity/tenants/$SLUG" | json_field email)
check "it is published to anyone, as section 13 requires" "grievances@example.com" "$GRIEVANCE"

echo

echo

echo
echo "Admin surface: tenants, modules, the role matrix, and administrators"

check "the tenant list is refused to a member" 403 \
  "$(status -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/identity/tenants")"

check "the tenant list is served to a Super Admin" 200 \
  "$(status -H "Authorization: Bearer $ADMIN_TOKEN" "$GATEWAY/v1/identity/tenants")"

# The admin panel signs an admin out if /me fails, so this being right is the
# difference between an unusable panel and a working one.
check "a Super Admin overriding into a Samaaj can still read their own account" 200 \
  "$(status -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
     "$GATEWAY/v1/identity/me")"

OVERRIDDEN_ME=$(curl -s -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
  "$GATEWAY/v1/identity/me" | json_field tenantId)

if [ "$OVERRIDDEN_ME" = "00000000-0000-0000-0000-000000000000" ]; then
  echo "  ok    and is still themselves, not somebody in the Samaaj they administer"
  pass=$((pass + 1))
else
  echo "  FAIL  the override changed who /me says the caller is (got $OVERRIDDEN_ME)"
  fail=$((fail + 1))
fi

check "a nonsense status is refused, not answered with an empty list" 400 \
  "$(status -H "Authorization: Bearer $ADMIN_TOKEN" "$GATEWAY/v1/identity/tenants?status=Dormant")"

check "the module catalogue is public because it fills a form" 200 \
  "$(status "$GATEWAY/v1/identity/tenants/modules")"

check "a mistyped module key is refused" 400 \
  "$(status -X PUT "$GATEWAY/v1/identity/tenants/$TENANT_ID/modules" \
     -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN_TOKEN" \
     -d '{"enabledModules":["pathshaala"]}')"

check "modules are replaced as a whole set" 200 \
  "$(status -X PUT "$GATEWAY/v1/identity/tenants/$TENANT_ID/modules" \
     -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN_TOKEN" \
     -d '{"enabledModules":["pathshala","boli"]}')"

check "the role matrix is readable by any signed-in member" 200 \
  "$(status -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/identity/roles")"

MATRIX=$(curl -s -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/identity/roles")

if printf '%s' "$MATRIX" | grep -q '"editable":false'; then
  echo "  ok    the matrix says it is not editable rather than letting a screen assume"
  pass=$((pass + 1))
else
  echo "  FAIL  the matrix says it is not editable"
  fail=$((fail + 1))
fi

INVITE_EMAIL="admin-$(date +%s)@example.com"

check "a member cannot invite an administrator" 403 \
  "$(status -X POST "$GATEWAY/v1/identity/admins" -H 'Content-Type: application/json' \
     -H "Authorization: Bearer $MEMBER_TOKEN" \
     -d "{\"fullName\":\"Rajesh Jain\",\"mobileOrEmail\":\"$INVITE_EMAIL\",\"roles\":[\"SamaajAdmin\"]}")"

check "SuperAdmin cannot be invited into" 400 \
  "$(status -X POST "$GATEWAY/v1/identity/admins" -H 'Content-Type: application/json' \
     -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
     -d "{\"fullName\":\"Rajesh Jain\",\"mobileOrEmail\":\"$INVITE_EMAIL\",\"roles\":[\"SuperAdmin\"]}")"

INVITE=$(curl -s -X POST "$GATEWAY/v1/identity/admins" -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
  -d "{\"fullName\":\"Rajesh Jain\",\"mobileOrEmail\":\"$INVITE_EMAIL\",\"roles\":[\"SamaajAdmin\"]}")

INVITED_ID=$(printf '%s' "$INVITE" | json_field userId)
INVITE_CODE=$(printf '%s' "$INVITE" | json_field activationCode)

if [ -n "$INVITED_ID" ] && [ -n "$INVITE_CODE" ]; then
  echo "  ok    an admin is invited and handed a one-time code"
  pass=$((pass + 1))
else
  echo "  FAIL  an admin is invited and handed a one-time code ($INVITE)"
  fail=$((fail + 1))
fi

check "the invited account cannot be signed into until the code is redeemed" 401 \
  "$(status -X POST "$GATEWAY/v1/identity/login" -H 'Content-Type: application/json' \
     -d "{\"mobileOrEmail\":\"$INVITE_EMAIL\",\"password\":\"$MEMBER_PASSWORD\"}")"

check "the same identifier cannot be invited twice" 409 \
  "$(status -X POST "$GATEWAY/v1/identity/admins" -H 'Content-Type: application/json' \
     -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
     -d "{\"fullName\":\"Rajesh Jain\",\"mobileOrEmail\":\"$INVITE_EMAIL\",\"roles\":[\"SamaajAdmin\"]}")"

check "the invited admin redeems the code and sets a password" 200 \
  "$(status -X POST "$GATEWAY/v1/identity/activations/redeem" -H 'Content-Type: application/json' \
     -d "{\"mobileOrEmail\":\"$INVITE_EMAIL\",\"code\":\"$INVITE_CODE\",\"password\":\"$MEMBER_PASSWORD\"}")"

SAMAAJ_ADMIN_TOKEN=$(curl -s -X POST "$GATEWAY/v1/identity/login" \
  -H 'Content-Type: application/json' \
  -d "{\"mobileOrEmail\":\"$INVITE_EMAIL\",\"password\":\"$MEMBER_PASSWORD\"}" | json_field accessToken)

if [ -n "$SAMAAJ_ADMIN_TOKEN" ]; then
  echo "  ok    the invited admin signs in with the role they were invited into"
  pass=$((pass + 1))
else
  echo "  FAIL  the invited admin signs in"
  fail=$((fail + 1))
fi

# The role travelled with the invitation, so this admin can do admin work
# without anyone granting them anything after activation.
check "and can immediately see their Samaaj's administrators" 200 \
  "$(status -H "Authorization: Bearer $SAMAAJ_ADMIN_TOKEN" "$GATEWAY/v1/identity/admins")"

check "but cannot decide which modules their Samaaj runs" 403 \
  "$(status -X PUT "$GATEWAY/v1/identity/tenants/$TENANT_ID/modules" \
     -H 'Content-Type: application/json' -H "Authorization: Bearer $SAMAAJ_ADMIN_TOKEN" \
     -d '{"enabledModules":["boli"]}')"

check "nor list every Samaaj on the platform" 403 \
  "$(status -H "Authorization: Bearer $SAMAAJ_ADMIN_TOKEN" "$GATEWAY/v1/identity/tenants")"

check "granting a role" 200 \
  "$(status -X PUT "$GATEWAY/v1/identity/admins/$INVITED_ID/roles/BoliManager" \
     -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN_TOKEN" \
     -H "$ADMIN_TENANT_HEADER" -d '{"granted":true}')"

check "SuperAdmin cannot be granted through this endpoint" 400 \
  "$(status -X PUT "$GATEWAY/v1/identity/admins/$INVITED_ID/roles/SuperAdmin" \
     -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN_TOKEN" \
     -H "$ADMIN_TENANT_HEADER" -d '{"granted":true}')"

check "an admin cannot remove their own Samaaj Admin role" 409 \
  "$(status -X PUT "$GATEWAY/v1/identity/admins/$INVITED_ID/roles/SamaajAdmin" \
     -H 'Content-Type: application/json' -H "Authorization: Bearer $SAMAAJ_ADMIN_TOKEN" \
     -d '{"granted":false}')"

ADMINS=$(curl -s -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
  "$GATEWAY/v1/identity/admins")

if printf '%s' "$ADMINS" | grep -q "BoliManager"; then
  echo "  ok    the admin list shows the granted role"
  pass=$((pass + 1))
else
  echo "  FAIL  the admin list shows the granted role"
  fail=$((fail + 1))
fi

if printf '%s' "$ADMINS" | grep -q "Smoke Member"; then
  echo "  FAIL  the admin list leaks ordinary members"
  fail=$((fail + 1))
else
  echo "  ok    the admin list omits ordinary members"
  pass=$((pass + 1))
fi

check "revoking a role" 200 \
  "$(status -X PUT "$GATEWAY/v1/identity/admins/$INVITED_ID/roles/BoliManager" \
     -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN_TOKEN" \
     -H "$ADMIN_TENANT_HEADER" -d '{"granted":false}')"

check "the admin list is refused to a member" 403 \
  "$(status -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/identity/admins")"

echo
echo "DPDP section 12: erasure, across all three services"

# A throwaway account, because everything below this point still needs the
# member registered at the top to be able to sign in.
ERASE_EMAIL="erasable-$(date +%s)@example.com"

check "a throwaway member registers" 201 \
  "$(status -X POST "$GATEWAY/v1/identity/register" -H 'Content-Type: application/json' \
     -d "{\"tenantSlug\":\"$SLUG\",\"fullName\":\"Erasable Member\",\"mobileOrEmail\":\"$ERASE_EMAIL\",\"password\":\"$MEMBER_PASSWORD\",\"consentedPurposes\":[\"Membership\"],\"noticeVersion\":\"$NOTICE_VERSION\"}")"

ERASE_TOKEN=$(curl -s -X POST "$GATEWAY/v1/identity/login" -H 'Content-Type: application/json' \
  -d "{\"mobileOrEmail\":\"$ERASE_EMAIL\",\"password\":\"$MEMBER_PASSWORD\"}" | json_field accessToken)

ERASE_USER_ID=$(curl -s -H "Authorization: Bearer $ERASE_TOKEN" "$GATEWAY/v1/identity/me" \
  | json_field userId)

# There has to be something to erase before erasing proves anything.
erasable_profile=0
for attempt in $(seq 1 30); do
  if curl -s -H "Authorization: Bearer $ERASE_TOKEN" "$GATEWAY/v1/members/me" \
     | grep -q "Erasable Member"; then
    erasable_profile=1
    break
  fi
  sleep 2
done
check "the throwaway profile arrives over Kafka" 1 "$erasable_profile"

# Both waits below are for something to *disappear*, so each needs proof it was
# there first - otherwise a check that runs before the event is consumed passes
# for the wrong reason.
listed_before=0
for attempt in $(seq 1 30); do
  if curl -s -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/members?search=Erasable" \
     | grep -q "Erasable Member"; then
    listed_before=1
    break
  fi
  sleep 2
done
check "the throwaway member is in the directory first" 1 "$listed_before"

actor_recorded=0
for attempt in $(seq 1 30); do
  if curl -s -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
     "$GATEWAY/v1/audit/logs?limit=200" | grep -q "\"actorUserId\":\"$ERASE_USER_ID\""; then
    actor_recorded=1
    break
  fi
  sleep 2
done
check "their actions are on the audit record first" 1 "$actor_recorded"

check "a wrong password erases nothing" 401 \
  "$(status -X POST "$GATEWAY/v1/identity/me/erase" -H 'Content-Type: application/json' \
     -H "Authorization: Bearer $ERASE_TOKEN" -d '{"password":"not-the-password"}')"

check "still signed in after the refused attempt" 200 \
  "$(status -H "Authorization: Bearer $ERASE_TOKEN" "$GATEWAY/v1/identity/me")"

check "the member erases their own account" 200 \
  "$(status -X POST "$GATEWAY/v1/identity/me/erase" -H 'Content-Type: application/json' \
     -H "Authorization: Bearer $ERASE_TOKEN" -d "{\"password\":\"$MEMBER_PASSWORD\"}")"

check "an erased member cannot sign in" 401 \
  "$(status -X POST "$GATEWAY/v1/identity/login" -H 'Content-Type: application/json' \
     -d "{\"mobileOrEmail\":\"$ERASE_EMAIL\",\"password\":\"$MEMBER_PASSWORD\"}")"

# member-family-service consumes identity.user.erased.v1.
profile_erased=0
for attempt in $(seq 1 30); do
  if ! curl -s -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/members?search=Erasable" \
     | grep -q "Erasable Member"; then
    profile_erased=1
    break
  fi
  sleep 2
done
check "the profile is erased in member-family-service" 1 "$profile_erased"

# audit-notification-service consumes it too, and de-identifies rather than
# deletes: the actions survive, the actor does not.
actor_gone=0
for attempt in $(seq 1 30); do
  if ! curl -s -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
     "$GATEWAY/v1/audit/logs?limit=200" | grep -q "\"actorUserId\":\"$ERASE_USER_ID\"" ; then
    actor_gone=1
    break
  fi
  sleep 2
done
check "the audit rows no longer name the actor" 1 "$actor_gone"

ERASURE_RECORDED=$(curl -s -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
  "$GATEWAY/v1/audit/logs?limit=200" | grep -c '"action":"Erased"' || true)

if [ "$ERASURE_RECORDED" -ge 1 ]; then
  echo "  ok    the erasure itself is on the record"
  pass=$((pass + 1))
else
  echo "  FAIL  the erasure itself is on the record (found none)"
  fail=$((fail + 1))
fi

echo

echo

echo

echo
echo "Timeline: posting, moderation, and the module gate"

# The first module-gated route on the platform, so this is also the first time
# ModuleGateMiddleware decides anything for real.
check "the community module is switched on for this Samaaj" 200 \
  "$(status -X PUT "$GATEWAY/v1/identity/tenants/$TENANT_ID/modules" \
     -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN_TOKEN" \
     -d '{"enabledModules":["community","pathshala"]}')"

# The gateway caches what a Samaaj runs for 60 seconds, so a module change is
# not instant. Waiting is the honest way to test it.
sleep 62

check "a member reads the timeline" 200 \
  "$(status -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/timeline/posts")"

POST=$(curl -s -X POST "$GATEWAY/v1/timeline/posts" -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $MEMBER_TOKEN" \
  -d '{"title":"Community blood donation drive","body":"Volunteers are welcome to participate.","asAnnouncement":false}')

POST_ID=$(printf '%s' "$POST" | json_field id)
POST_STATUS=$(printf '%s' "$POST" | json_field status)

if [ -n "$POST_ID" ] && [ "$POST_STATUS" = "PendingReview" ]; then
  echo "  ok    a member's post is created awaiting review"
  pass=$((pass + 1))
else
  echo "  FAIL  a member's post is created awaiting review (got '$POST_STATUS')"
  fail=$((fail + 1))
fi

check "a member cannot publish a Samaaj announcement" 403 \
  "$(status -X POST "$GATEWAY/v1/timeline/posts" -H 'Content-Type: application/json' \
     -H "Authorization: Bearer $MEMBER_TOKEN" \
     -d '{"title":"Announcement","body":"Straight to the timeline.","asAnnouncement":true}')"

check "nor moderate" 403 \
  "$(status -X POST "$GATEWAY/v1/timeline/posts/$POST_ID/moderate" -H 'Content-Type: application/json' \
     -H "Authorization: Bearer $MEMBER_TOKEN" -d '{"decision":"Approve"}')"

check "nor read the moderation queue" 403 \
  "$(status -H "Authorization: Bearer $MEMBER_TOKEN" \
     "$GATEWAY/v1/timeline/posts/moderation-queue")"

# The post is invisible to everyone but its author until it is approved.
if curl -s -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
   "$GATEWAY/v1/timeline/posts" | grep -q "blood donation"; then
  echo "  FAIL  an unapproved post is on the timeline"
  fail=$((fail + 1))
else
  echo "  ok    an unapproved post is not on the timeline"
  pass=$((pass + 1))
fi

if curl -s -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
   "$GATEWAY/v1/timeline/posts/moderation-queue" | grep -q "blood donation"; then
  echo "  ok    but it is in the moderation queue"
  pass=$((pass + 1))
else
  echo "  FAIL  the post did not reach the moderation queue"
  fail=$((fail + 1))
fi

check "rejecting without saying why is refused" 400 \
  "$(status -X POST "$GATEWAY/v1/timeline/posts/$POST_ID/moderate" -H 'Content-Type: application/json' \
     -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
     -d '{"decision":"Reject"}')"

check "a moderator approves it" 200 \
  "$(status -X POST "$GATEWAY/v1/timeline/posts/$POST_ID/moderate" -H 'Content-Type: application/json' \
     -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
     -d '{"decision":"Approve"}')"

if curl -s -H "Authorization: Bearer $ADMIN_TOKEN" -H "$ADMIN_TENANT_HEADER" \
   "$GATEWAY/v1/timeline/posts" | grep -q "blood donation"; then
  echo "  ok    and it reaches the Samaaj's timeline"
  pass=$((pass + 1))
else
  echo "  FAIL  the approved post is not on the timeline"
  fail=$((fail + 1))
fi

check "commenting on an approved post" 201 \
  "$(status -X POST "$GATEWAY/v1/timeline/posts/$POST_ID/comments" -H 'Content-Type: application/json' \
     -H "Authorization: Bearer $MEMBER_TOKEN" -d '{"body":"Happy to help."}')"

check "reacting to it" 200 \
  "$(status -X PUT "$GATEWAY/v1/timeline/posts/$POST_ID/reaction" -H 'Content-Type: application/json' \
     -H "Authorization: Bearer $MEMBER_TOKEN" -d '{"reaction":"Appreciate"}')"

check "reporting it" 200 \
  "$(status -X POST "$GATEWAY/v1/timeline/posts/$POST_ID/report" \
     -H "Authorization: Bearer $MEMBER_TOKEN")"

# 404, not the handler's 403: a Super Admin with no Samaaj selected has no
# resolved tenant, and the module gate refuses a module route it cannot check
# before the request ever reaches the service. The handler check behind it is
# defence in depth for a caller who bypasses the gateway.
check "a post with no Samaaj selected never reaches the service" 404 \
  "$(status -X POST "$GATEWAY/v1/timeline/posts" -H 'Content-Type: application/json' \
     -H "Authorization: Bearer $ADMIN_TOKEN" \
     -d '{"title":"Nowhere","body":"No Samaaj selected.","asAnnouncement":false}')"

# Switching the module off must take the whole area away, not merely refuse it:
# a Samaaj that does not run a module should be indistinguishable from a
# platform that has no such feature (ARCHITECTURE.md section 6).
check "switching the community module off" 200 \
  "$(status -X PUT "$GATEWAY/v1/identity/tenants/$TENANT_ID/modules" \
     -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN_TOKEN" \
     -d '{"enabledModules":["pathshala"]}')"

sleep 62

check "the timeline then answers 404, not 403" 404 \
  "$(status -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/timeline/posts")"

check "while an ungated route is unaffected" 200 \
  "$(status -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/identity/me")"

check "switching it back on" 200 \
  "$(status -X PUT "$GATEWAY/v1/identity/tenants/$TENANT_ID/modules" \
     -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN_TOKEN" \
     -d '{"enabledModules":["community","pathshala"]}')"

echo
echo "Sessions: rotation, reuse detection and sign-out"

SESSION_LOGIN=$(curl -s -X POST "$GATEWAY/v1/identity/login" -H 'Content-Type: application/json' \
  -d "{\"mobileOrEmail\":\"$MEMBER\",\"password\":\"$MEMBER_PASSWORD\"}")

REFRESH_1=$(printf '%s' "$SESSION_LOGIN" | json_field refreshToken)

if [ -n "$REFRESH_1" ]; then
  echo "  ok    signing in returns a refresh token"
  pass=$((pass + 1))
else
  echo "  FAIL  signing in returns a refresh token"
  fail=$((fail + 1))
fi

REFRESHED=$(curl -s -X POST "$GATEWAY/v1/identity/token/refresh" -H 'Content-Type: application/json' \
  -d "{\"refreshToken\":\"$REFRESH_1\"}")

REFRESH_2=$(printf '%s' "$REFRESHED" | json_field refreshToken)
ACCESS_2=$(printf '%s' "$REFRESHED" | json_field accessToken)

if [ -n "$REFRESH_2" ] && [ "$REFRESH_2" != "$REFRESH_1" ]; then
  echo "  ok    refreshing rotates the token"
  pass=$((pass + 1))
else
  echo "  FAIL  refreshing rotates the token"
  fail=$((fail + 1))
fi

check "the new access token works" 200 \
  "$(status -H "Authorization: Bearer $ACCESS_2" "$GATEWAY/v1/identity/me")"

check "spending a refresh token twice is refused" 401 \
  "$(status -X POST "$GATEWAY/v1/identity/token/refresh" -H 'Content-Type: application/json' \
     -d "{\"refreshToken\":\"$REFRESH_1\"}")"

# The theft response: the live token in that chain dies with the replayed one.
check "and kills the live token in the same session" 401 \
  "$(status -X POST "$GATEWAY/v1/identity/token/refresh" -H 'Content-Type: application/json' \
     -d "{\"refreshToken\":\"$REFRESH_2\"}")"

SESSION_LOGIN=$(curl -s -X POST "$GATEWAY/v1/identity/login" -H 'Content-Type: application/json' \
  -d "{\"mobileOrEmail\":\"$MEMBER\",\"password\":\"$MEMBER_PASSWORD\"}")
REFRESH_3=$(printf '%s' "$SESSION_LOGIN" | json_field refreshToken)

check "signing out is accepted" 200 \
  "$(status -X POST "$GATEWAY/v1/identity/logout" -H 'Content-Type: application/json' \
     -d "{\"refreshToken\":\"$REFRESH_3\"}")"

check "and the session cannot be continued afterwards" 401 \
  "$(status -X POST "$GATEWAY/v1/identity/token/refresh" -H 'Content-Type: application/json' \
     -d "{\"refreshToken\":\"$REFRESH_3\"}")"

check "signing out with an unknown token is not an error" 200 \
  "$(status -X POST "$GATEWAY/v1/identity/logout" -H 'Content-Type: application/json' \
     -d '{"refreshToken":"not-a-real-token"}')"

check "a refresh token nobody issued is refused" 401 \
  "$(status -X POST "$GATEWAY/v1/identity/token/refresh" -H 'Content-Type: application/json' \
     -d '{"refreshToken":"not-a-real-token"}')"

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
echo "Rate limiting"

# The limit is deliberately high - Indian carriers put many subscribers behind
# one address - so this proves the policy is attached and enforcing rather than
# trying to exhaust it. A route with no policy would never answer 429.
RL_STATUS=""
for attempt in $(seq 1 400); do
  code=$(status -X POST "$GATEWAY/v1/identity/login" -H 'Content-Type: application/json' \
    -d '{"mobileOrEmail":"nobody@example.com","password":"wrong"}')
  if [ "$code" = "429" ]; then
    RL_STATUS="429"
    break
  fi
done

if [ "$RL_STATUS" = "429" ]; then
  echo "  ok    sign-in is rate limited per source (429 after $attempt attempts)"
  pass=$((pass + 1))
else
  echo "  FAIL  sign-in was never rate limited in 400 attempts"
  fail=$((fail + 1))
fi

# An authenticated route carries no policy, so the burst above must not have
# taken the rest of the platform down with it.
check "an unrelated route is unaffected by the burst" 200 \
  "$(status -H "Authorization: Bearer $MEMBER_TOKEN" "$GATEWAY/v1/identity/me")"

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
