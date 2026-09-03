#!/usr/bin/env bash
#
# Attempts cross-tenant IDOR against every id-taking write endpoint on the
# platform, through the gateway.
#
# This is the check `DEVELOPMENT_PLAN.md` Phase 5 asks for. Multi-tenancy is the
# platform's core safety property and the one whose failure is silent: a handler
# that forgets to re-validate `TenantId` looks exactly like one that does not
# need to, and the query filter alone does not protect a write path
# (root CLAUDE.md section 6).
#
# The method: build two real Samaaj with real data in each, then have Samaaj B's
# member and Samaaj B's administrator attempt every write against Samaaj A's
# ids.
#
# **The expected answer is 404, and 403 is a failure.** A 403 on an entity from
# another Samaaj confirms that the id is real, which is the thing the platform
# is trying not to say - it is the same reasoning as an unapproved timeline post
# reporting "not found" to somebody who guessed its id. Both are recorded
# separately below so a 403 is never mistaken for a pass.
#
# Assumes `docker compose up -d --build` has finished.
set -euo pipefail

GATEWAY="${GATEWAY:-http://localhost:8080}"

SUPERADMIN="${SUPERADMIN:-superadmin@samaajconnect.local}"
SUPERADMIN_PASSWORD="${SUPERADMIN_PASSWORD:-change-me-immediately}"

# Samaaj A holds the data. Samaaj B does the probing.
A_SLUG="${A_SLUG:-smoke-samaj}"
B_SLUG="${B_SLUG:-probe-samaj}"
PASSWORD="a-long-enough-password"
A_MEMBER="probe-a@example.com"
B_MEMBER="probe-b@example.com"

pass=0
fail=0
leaked=0

# Every (method, path) this run actually probed, for the coverage audit at the
# end. See "what this run did not probe" below for why that audit exists.
covered=$(mktemp)
trap 'rm -f "$covered"' EXIT

json_field() {
  { grep -o "\"$1\":\"[^\"]*\"" || true; } | head -1 | cut -d'"' -f4
}

# The whole point of the script. `label` names the endpoint being probed.
refuses() {
  local label="$1" actual="$2"

  case "$actual" in
    404)
      echo "  ok      $label -> 404"
      pass=$((pass + 1))
      ;;
    403)
      # Refused, so nothing was written - but the refusal itself confirms the
      # id exists. Counted apart from a clean pass.
      echo "  LEAK    $label -> 403 (refused, but confirms the id is real)"
      leaked=$((leaked + 1))
      ;;
    400)
      # Not an isolation failure, and worth saying so in the output rather than
      # leaving the next person to work it out.
      #
      # ValidationBehavior runs *before* the handler that checks the tenant
      # (root CLAUDE.md §4.4), so a body the validator refuses never reaches the
      # check this script exists to make. The endpoint is then not probed at
      # all - which is why this counts as a failure rather than a pass, even
      # though nothing leaked.
      #
      # It happens when a command gains a required field and the body below is
      # not updated with it. `UpdateProfileCommand` gaining
      # `IsListedInDirectory` did exactly that, and this line is what said so.
      echo "  STALE   $label -> 400 (body no longer satisfies the validator; NOT probed)"
      fail=$((fail + 1))
      ;;
    *)
      echo "  FAIL    $label -> $actual (expected 404)"
      fail=$((fail + 1))
      ;;
  esac
}

call() {
  # call <method> <path> <token> [tenant-override] [body] -> status code
  local method="$1" path="$2" token="$3" override="${4:-}" body="${5:-}"
  local args=(-s -o /dev/null -w '%{http_code}' -X "$method" "$GATEWAY$path"
              -H "Authorization: Bearer $token" -H 'Content-Type: application/json')

  [ -n "$override" ] && args+=(-H "X-Tenant-Override-Id: $override")
  [ -n "$body" ] && args+=(-d "$body")

  curl "${args[@]}"
}

probe() {
  # probe <label> <method> <path> <token> [tenant-override] [body]

  # Recorded so this script can audit its own coverage at the end. The ids are
  # flattened back to {id} and the query string dropped, matching the shape the
  # endpoint enumeration below produces.
  printf '%s %s\n' "$2" "$3" \
    | sed 's/?.*//; s|/[0-9a-fA-F]\{8\}-[0-9a-fA-F-]\{27\}|/{id}|g' >> "$covered"

  refuses "$1" "$(call "$2" "$3" "$4" "${5:-}" "${6:-}")"
}

# The probe's own safety catch, and the reason it is not optional.
#
# A cross-tenant probe against a path that does not exist answers 404 and looks
# exactly like a pass. This script had three of those on its first run - the
# volunteer-groups routes are under /v1/volunteer-groups/groups, not
# /v1/volunteer-groups - and they were indistinguishable from the real refusals
# beside them. A security check that passes because it missed the endpoint is
# worse than no check, because somebody reads the summary line and believes it.
#
# So every read probed below is first proven reachable *by the Samaaj that owns
# the entity*. If A's own member cannot read it, the path is wrong and the
# script says so instead of scoring a pass.
control() {
  local label="$1" path="$2" token="$3" override="${4:-}"
  local actual
  actual="$(call GET "$path" "$token" "$override")"

  case "$actual" in
    200)
      : # Reachable. A 404 from Samaaj B now means something.
      ;;
    *)
      echo "  BROKEN  control: $label is $actual for its own Samaaj - the probe below is meaningless"
      fail=$((fail + 1))
      ;;
  esac
}

login() {
  curl -s -X POST "$GATEWAY/v1/identity/login" -H 'Content-Type: application/json' \
    -d "{\"mobileOrEmail\":\"$1\",\"password\":\"$2\"}" | json_field accessToken
}

register_member() {
  # Idempotent: a second run finds the account already there and just signs in.
  local slug="$1" email="$2" name="$3" notice="$4"

  curl -s -o /dev/null -X POST "$GATEWAY/v1/identity/register" \
    -H 'Content-Type: application/json' \
    -d "{\"tenantSlug\":\"$slug\",\"fullName\":\"$name\",\"mobileOrEmail\":\"$email\",\"password\":\"$PASSWORD\",\"consentedPurposes\":[\"Membership\"],\"noticeVersion\":\"$notice\"}" || true

  login "$email" "$PASSWORD"
}

# Waiting on the gateway's own /health is not enough, and this script learned
# that the same way `smoke-through-gateway.sh` did: the gateway answers before
# the services behind it have finished migrating, so the first request fails and
# the script reports "could not sign in as Super Admin" - which reads like a
# wrong password rather than a stack that was not ready yet.
wait_for_stack() {
  local attempt=0

  until [ "$(curl -s -o /dev/null -w '%{http_code}' "$GATEWAY/health")" = "200" ] \
     && [ "$(curl -s -o /dev/null -w '%{http_code}' "$GATEWAY/v1/identity/tenants/directory")" = "200" ]; do
    attempt=$((attempt + 1))

    if [ "$attempt" -ge 90 ]; then
      echo "  BROKEN  the stack did not become ready"
      exit 1
    fi

    sleep 2
  done
}

echo "== waiting for the stack =="
wait_for_stack

echo "== signing in as Super Admin =="
SUPER=$(login "$SUPERADMIN" "$SUPERADMIN_PASSWORD")
[ -n "$SUPER" ] || { echo "could not sign in as Super Admin"; exit 1; }

NOTICE=$(curl -s "$GATEWAY/v1/identity/consent-notice" | json_field version)

echo "== making sure both Samaaj exist, with every module on =="

# Both are created, not just B.
#
# A used to be looked up and never created, on the assumption that
# `smoke-through-gateway.sh` had already made `smoke-samaj`. Against empty
# volumes that lookup returns nothing, `A_ID` is empty, and the script gets as
# far as "could not sign in both members" before stopping - which is at least
# loud, but it made a security check quietly dependent on another script having
# been run first, in the right order, in the same session. Creating both means
# this one stands on its own.
ensure_tenant() {
  local slug="$1" name="$2"

  curl -s -o /dev/null -X POST "$GATEWAY/v1/identity/tenants" \
    -H "Authorization: Bearer $SUPER" -H 'Content-Type: application/json' \
    -d "{\"name\":\"$name\",\"slug\":\"$slug\"}" || true

  curl -s "$GATEWAY/v1/identity/tenants/$slug" | json_field id
}

A_ID=$(ensure_tenant "$A_SLUG" "Probe Samaaj A")
B_ID=$(ensure_tenant "$B_SLUG" "Probe Samaaj B")

[ -n "$A_ID" ] && [ -n "$B_ID" ] || {
  echo "  BROKEN  could not create or find both Samaaj (A='$A_ID' B='$B_ID')"
  exit 1
}

for t in "$A_ID" "$B_ID"; do
  curl -s -o /dev/null -X PUT "$GATEWAY/v1/identity/tenants/$t/modules" \
    -H "Authorization: Bearer $SUPER" -H 'Content-Type: application/json' \
    -d '{"enabledModules":["community","social-issues","celebrity-voting","pathshala","boli"]}'
  curl -s -o /dev/null -X PATCH "$GATEWAY/v1/identity/tenants/$t/status" \
    -H "Authorization: Bearer $SUPER" -H 'Content-Type: application/json' \
    -d '{"status":"Active"}' || true
done

echo "   A=$A_ID  B=$B_ID"
echo "   waiting out the gateway's 60-second module cache"
sleep 62

echo "== members =="
A_TOKEN=$(register_member "$A_SLUG" "$A_MEMBER" "Probe A" "$NOTICE")
B_TOKEN=$(register_member "$B_SLUG" "$B_MEMBER" "Probe B" "$NOTICE")
[ -n "$A_TOKEN" ] && [ -n "$B_TOKEN" ] || { echo "could not sign in both members"; exit 1; }

AA=(-H "Authorization: Bearer $A_TOKEN" -H 'Content-Type: application/json')
SA=(-H "Authorization: Bearer $SUPER" -H "X-Tenant-Override-Id: $A_ID" -H 'Content-Type: application/json')

echo "== building one of everything in Samaaj A =="

POST_ID=$(curl -s "${AA[@]}" -X POST "$GATEWAY/v1/timeline/posts" \
  -d '{"type":"MemberPost","title":"Probe post","body":"Belongs to Samaaj A."}' | json_field id)

ISSUE_ID=$(curl -s "${AA[@]}" -X POST "$GATEWAY/v1/social-issues" \
  -d '{"title":"Probe issue","description":"Belongs to Samaaj A.","category":"Safety","locality":"A","submitNow":true}' | json_field id)

A_MEMBER_ID=$(curl -s "${AA[@]}" "$GATEWAY/v1/identity/me" | json_field userId)

# A group name is unique within a Samaaj, so a second run gets 409 rather than
# an id. Falling back to the list keeps the script re-runnable, which matters:
# the alternative is an empty id, and an empty id probes a list endpoint.
GROUP_ID=$(curl -s "${SA[@]}" -X POST "$GATEWAY/v1/volunteer-groups/groups" \
  -d "{\"name\":\"Probe group\",\"description\":\"Belongs to Samaaj A.\",\"presidentMemberId\":\"$A_MEMBER_ID\"}" \
  | json_field id)

if [ -z "$GROUP_ID" ]; then
  GROUP_ID=$(curl -s "${AA[@]}" "$GATEWAY/v1/volunteer-groups/groups" \
    | tr '{' '\n' | grep '"name":"Probe group"' | json_field id)
fi

EVENT_ID=$(curl -s "${SA[@]}" -X POST "$GATEWAY/v1/events" \
  -d "{\"title\":\"Probe event\",\"description\":\"A\",\"venue\":\"Hall\",\"organizerType\":\"Samaaj\",\"startAt\":\"2099-01-01T10:00:00Z\",\"endAt\":\"2099-01-01T12:00:00Z\",\"capacity\":50}" | json_field id)

CAMPAIGN_ID=$(curl -s "${SA[@]}" -X POST "$GATEWAY/v1/celebrity-voting/campaigns" \
  -d "{\"title\":\"Probe campaign\",\"description\":\"A\",\"nominationStartAt\":\"2099-01-01T00:00:00Z\",\"nominationEndAt\":\"2099-01-02T00:00:00Z\",\"votingStartAt\":\"2099-01-02T00:00:00Z\",\"votingEndAt\":\"2099-01-03T00:00:00Z\",\"topN\":3,\"resultsVisibility\":\"Live\"}" | json_field id)

PATHSHALA_ID=$(curl -s "${SA[@]}" -X POST "$GATEWAY/v1/pathshala/pathshalas" \
  -d '{"name":"Probe Pathshala","address":"A","contactPerson":"A"}' | json_field id)

# A Pathshala with a class, a placed child and an exam, so the teaching half is
# probeable at all. Those endpoints - the register, the roll, the timetable,
# exam results - were added long after this script was written, and until now
# nothing here touched them.
SESSION_ID=$(curl -s "${SA[@]}" -X POST "$GATEWAY/v1/pathshala/pathshalas/$PATHSHALA_ID/sessions" \
  -d '{"label":"probe-2026","startDate":"2020-01-01","endDate":"2099-01-01"}' \
  | grep -oE '"sessions":\[\{"id":"[^"]*"' | cut -d'"' -f6)

CLASS_ID=$(curl -s "${SA[@]}" -X POST "$GATEWAY/v1/pathshala/pathshalas/$PATHSHALA_ID/classes" \
  -d "{\"sessionId\":\"$SESSION_ID\",\"name\":\"Probe class\",\"roomLabel\":null}" | json_field id)

ENROLMENT_ID=$(curl -s "${SA[@]}" -X POST "$GATEWAY/v1/pathshala/pathshalas/$PATHSHALA_ID/enrollments" \
  -d '{"childProfileId":"00000000-0000-0000-0000-0000000000aa"}' | json_field id)

curl -s -o /dev/null "${SA[@]}" -X POST "$GATEWAY/v1/pathshala/enrollments/$ENROLMENT_ID/placement" \
  -d "{\"classId\":\"$CLASS_ID\",\"place\":true}"

EXAM_ID=$(curl -s "${SA[@]}" -X POST "$GATEWAY/v1/pathshala/classes/$CLASS_ID/exams" \
  -d '{"title":"Probe exam","examDate":"2026-01-01","maxScore":50}' | json_field id)

OCCASION_ID=$(curl -s "${SA[@]}" -X POST "$GATEWAY/v1/boli/occasions" \
  -d '{"title":"Probe occasion","description":"A","occasionDate":"2099-01-01"}' | json_field id)

BOLI_TYPE_ID=$(curl -s "${SA[@]}" -X POST "$GATEWAY/v1/boli/occasions/$OCCASION_ID/boli-types" \
  -d '{"name":"Probe type","description":null}' | json_field id)

BOLI_ID=$(curl -s "${SA[@]}" -X POST "$GATEWAY/v1/boli/occasions/$OCCASION_ID/boli" \
  -d "{\"boliTypeId\":\"$BOLI_TYPE_ID\",\"title\":\"Probe Boli\",\"startAt\":\"2020-01-01T00:00:00Z\",\"endAt\":\"2099-01-01T00:00:00Z\",\"startingAmount\":100000,\"minIncrement\":10000,\"eligibilityRule\":null}" | json_field id)

# Creation is not idempotent everywhere - a volunteer group refuses a repeated
# name with 409 - so on a re-run some of the ids above come back empty. That
# matters more than it looks: an empty id turns "/groups/$GROUP_ID" into
# "/groups/", which is the *list* endpoint, and it answers 200 for anybody. The
# script's second run reported that as a cross-tenant read failure.
#
# So a missing id is a broken probe and says so, loudly, rather than being
# probed anyway. Re-runnable creation is handled where it can be; this catches
# whatever is left.
missing=0

for pair in "post:$POST_ID" "issue:$ISSUE_ID" "group:$GROUP_ID" "event:$EVENT_ID" \
            "campaign:$CAMPAIGN_ID" "pathshala:$PATHSHALA_ID" "occasion:$OCCASION_ID" "boli:$BOLI_ID" \
            "session:$SESSION_ID" "class:$CLASS_ID" "enrolment:$ENROLMENT_ID" "exam:$EXAM_ID"; do
  name=${pair%%:*}; id=${pair##*:}

  if [ -z "$id" ]; then
    echo "  BROKEN  nothing created for '$name' in Samaaj A - cannot probe it"
    missing=$((missing + 1))
  fi
done

if [ "$missing" -gt 0 ]; then
  echo
  echo "Could not build $missing of the fixtures in Samaaj A, so the run would"
  echo "score passes against paths with an empty id. Drop the volumes and start"
  echo "clean:  docker compose down -v && docker compose up -d --build"
  exit 1
fi

echo
echo "== controls: Samaaj A can reach its own, so a 404 for B means something =="

control "timeline  post"      "/v1/timeline/posts/$POST_ID"                  "$A_TOKEN"
control "issues    issue"     "/v1/social-issues/$ISSUE_ID"                  "$A_TOKEN"
control "groups    group"     "/v1/volunteer-groups/groups/$GROUP_ID"        "$A_TOKEN"
control "events    event"     "/v1/events/$EVENT_ID"                         "$SUPER" "$A_ID"
control "voting    campaign"  "/v1/celebrity-voting/campaigns/$CAMPAIGN_ID"  "$A_TOKEN"
control "boli      Boli"      "/v1/boli/boli/$BOLI_ID"                       "$A_TOKEN"
control "boli      occasion"  "/v1/boli/occasions/$OCCASION_ID"              "$A_TOKEN"
control "pathshala Pathshala" "/v1/pathshala/pathshalas/$PATHSHALA_ID"       "$A_TOKEN"
control "pathshala class roll" "/v1/pathshala/classes/$CLASS_ID/roll"        "$SUPER" "$A_ID"
control "pathshala register"   "/v1/pathshala/classes/$CLASS_ID/register?date=2026-01-01" "$SUPER" "$A_ID"
control "pathshala class exams" "/v1/pathshala/classes/$CLASS_ID/exams"      "$SUPER" "$A_ID"
control "members   member"    "/v1/members/$A_MEMBER_ID"                     "$A_TOKEN"

echo
echo "== Samaaj B's MEMBER attempts to act on Samaaj A's entities =="

probe "timeline  comment on A's post"    POST "/v1/timeline/posts/$POST_ID/comments" "$B_TOKEN" "" '{"body":"probe"}'
probe "timeline  react to A's post"      PUT  "/v1/timeline/posts/$POST_ID/reaction" "$B_TOKEN" "" '{"reaction":"Appreciate"}'
probe "timeline  report A's post"        POST "/v1/timeline/posts/$POST_ID/report"   "$B_TOKEN" "" '{}'
probe "timeline  read A's post"          GET  "/v1/timeline/posts/$POST_ID"          "$B_TOKEN"
probe "issues    read A's issue"         GET  "/v1/social-issues/$ISSUE_ID"          "$B_TOKEN"
probe "issues    revise A's issue"       PUT  "/v1/social-issues/$ISSUE_ID"          "$B_TOKEN" "" '{"title":"probe","description":"probe","category":"Safety","locality":null}'
# The body has to be *valid*, or ValidationBehavior answers 400 before the
# handler ever reaches the tenant check and the probe proves nothing. That 400
# is not a leak - it says the same thing whether or not the issue exists - but
# it is not a pass either. The first version of this line sent "Withdrawn",
# which is not an IssueStatus; the terminal state is Closed.
probe "issues    move A's issue"         POST "/v1/social-issues/$ISSUE_ID/status"   "$B_TOKEN" "" '{"status":"Closed","reason":"probe"}'
probe "groups    apply to A's group"     POST "/v1/volunteer-groups/groups/$GROUP_ID/applications" "$B_TOKEN" "" '{"message":"probe"}'
probe "groups    read A's group"         GET  "/v1/volunteer-groups/groups/$GROUP_ID"       "$B_TOKEN"
probe "events    register for A's event" POST "/v1/events/$EVENT_ID/registration"    "$B_TOKEN" "" '{}'
probe "events    read A's event"         GET  "/v1/events/$EVENT_ID"                 "$B_TOKEN"
probe "voting    nominate in A's campaign" POST "/v1/celebrity-voting/campaigns/$CAMPAIGN_ID/candidates" "$B_TOKEN" "" "{\"memberId\":\"$A_MEMBER_ID\",\"category\":null}"
probe "voting    vote in A's campaign"   POST "/v1/celebrity-voting/campaigns/$CAMPAIGN_ID/votes" "$B_TOKEN" "" '{"candidateId":"00000000-0000-0000-0000-000000000001"}'
probe "voting    read A's campaign"      GET  "/v1/celebrity-voting/campaigns/$CAMPAIGN_ID" "$B_TOKEN"
probe "boli      bid on A's Boli"        POST "/v1/boli/boli/$BOLI_ID/bids"          "$B_TOKEN" "" '{"amount":9999900}'
probe "boli      read A's Boli"          GET  "/v1/boli/boli/$BOLI_ID"               "$B_TOKEN"
probe "boli      read A's occasion"      GET  "/v1/boli/occasions/$OCCASION_ID"      "$B_TOKEN"
probe "pathshala enrol in A's Pathshala" POST "/v1/pathshala/pathshalas/$PATHSHALA_ID/enrollments" "$B_TOKEN" "" '{"childProfileId":"00000000-0000-0000-0000-000000000001"}'
probe "pathshala read A's Pathshala"     GET  "/v1/pathshala/pathshalas/$PATHSHALA_ID" "$B_TOKEN"
probe "members   read A's member"        GET  "/v1/members/$A_MEMBER_ID"             "$B_TOKEN"
# A full, valid body - otherwise ValidationBehavior answers 400 before the
# handler reaches the tenant check, the same trap as the issues move above.
#
# `isListedInDirectory` was added to this body on 2026-09-02, and its absence is
# the second time this one endpoint has caught the probe out. `UpdateProfile`
# replaces the whole profile, so every required field has to be here; when the
# command gained that one, this body stopped reaching the handler at all and the
# endpoint quietly stopped being probed. `refuses` now says STALE rather than
# FAIL for a 400, so the next time it is obvious what happened.
probe "members   correct A's member"     PATCH "/v1/members/$A_MEMBER_ID"            "$B_TOKEN" "" '{"fullName":"probe","privacy":{"mobile":"Private","email":"Private","address":"Private","profession":"Private","dateOfBirth":"Private"},"isListedInDirectory":true}'

echo
echo "== Samaaj B's ADMINISTRATOR attempts to act on Samaaj A's entities =="
echo "   (a Super Admin whose override scopes them to B - the IDOR guard's real test)"

probe "timeline  moderate A's post"       POST "/v1/timeline/posts/$POST_ID/moderate" "$SUPER" "$B_ID" '{"decision":"Hide","reason":"probe"}'
probe "issues    approve A's issue"       POST "/v1/social-issues/$ISSUE_ID/status"   "$SUPER" "$B_ID" '{"status":"UnderReview","reason":null}'
probe "groups    change A's group status" PATCH "/v1/volunteer-groups/groups/$GROUP_ID/status" "$SUPER" "$B_ID" '{"status":"Inactive"}'
probe "events    publish A's event"       POST "/v1/events/$EVENT_ID/publish"         "$SUPER" "$B_ID" '{}'
probe "events    cancel A's event"        POST "/v1/events/$EVENT_ID/cancel"          "$SUPER" "$B_ID" '{"reason":"probe"}'
probe "voting    move A's campaign"       POST "/v1/celebrity-voting/campaigns/$CAMPAIGN_ID/status" "$SUPER" "$B_ID" '{"status":"NominationsOpen"}'
probe "voting    publish A's results"     POST "/v1/celebrity-voting/campaigns/$CAMPAIGN_ID/results" "$SUPER" "$B_ID" '{}'
probe "pathshala open a session in A's"   POST "/v1/pathshala/pathshalas/$PATHSHALA_ID/sessions" "$SUPER" "$B_ID" '{"label":"probe","startDate":"2099-01-01","endDate":"2099-06-01"}'
probe "pathshala add a class to A's"      POST "/v1/pathshala/pathshalas/$PATHSHALA_ID/classes" "$SUPER" "$B_ID" '{"sessionId":"00000000-0000-0000-0000-000000000001","name":"probe","roomLabel":null}'
probe "pathshala deactivate A's"          DELETE "/v1/pathshala/pathshalas/$PATHSHALA_ID" "$SUPER" "$B_ID"

# The teaching half. These take a class, an enrolment or an exam id rather than
# the Pathshala's, so they are a separate reach across the boundary: the caller
# never names the Samaaj at all, and the handler has to find it from the id it
# was given and check that against the request's tenant.
probe "pathshala read A's roll"           GET  "/v1/pathshala/classes/$CLASS_ID/roll"     "$SUPER" "$B_ID"
probe "pathshala read A's register"       GET  "/v1/pathshala/classes/$CLASS_ID/register?date=2026-01-01" "$SUPER" "$B_ID"
probe "pathshala read A's class exams"    GET  "/v1/pathshala/classes/$CLASS_ID/exams"    "$SUPER" "$B_ID"
probe "pathshala teach A's class"         POST "/v1/pathshala/classes/$CLASS_ID/teachers" "$SUPER" "$B_ID" "{\"teacherMemberId\":\"$A_MEMBER_ID\",\"assign\":true}"
probe "pathshala timetable A's class"     POST "/v1/pathshala/classes/$CLASS_ID/schedule" "$SUPER" "$B_ID" '{"dayOfWeek":"Sunday","startTime":"09:00:00","endTime":"10:00:00"}'
probe "pathshala mark A's register"       POST "/v1/pathshala/classes/$CLASS_ID/attendance" "$SUPER" "$B_ID" "{\"classDate\":\"2026-01-01\",\"marks\":[{\"enrolmentId\":\"$ENROLMENT_ID\",\"status\":\"Present\"}]}"
probe "pathshala set an exam in A's class" POST "/v1/pathshala/classes/$CLASS_ID/exams"   "$SUPER" "$B_ID" '{"title":"probe","examDate":"2026-01-01","maxScore":10}'
probe "pathshala mark A's exam"           POST "/v1/pathshala/exams/$EXAM_ID/results"     "$SUPER" "$B_ID" "{\"enrolmentId\":\"$ENROLMENT_ID\",\"score\":1,\"grade\":null}"
probe "pathshala place A's child"         POST "/v1/pathshala/enrollments/$ENROLMENT_ID/placement" "$SUPER" "$B_ID" "{\"classId\":\"$CLASS_ID\",\"place\":true}"
probe "pathshala withdraw A's child"      DELETE "/v1/pathshala/enrollments/$ENROLMENT_ID" "$SUPER" "$B_ID"
probe "pathshala read A's placement queue" GET "/v1/pathshala/pathshalas/$PATHSHALA_ID/enrollments/requests" "$SUPER" "$B_ID"

# A child's own records, which are the most sensitive thing the platform holds.
probe "pathshala read A's child's class"  GET  "/v1/pathshala/enrollments/$ENROLMENT_ID/my-class"   "$SUPER" "$B_ID"
probe "pathshala read A's child's marks"  GET  "/v1/pathshala/enrollments/$ENROLMENT_ID/attendance" "$SUPER" "$B_ID"
probe "pathshala read A's child's exams"  GET  "/v1/pathshala/enrollments/$ENROLMENT_ID/exams"      "$SUPER" "$B_ID"
probe "pathshala read A's child's progress" GET "/v1/pathshala/enrollments/$ENROLMENT_ID/progress"  "$SUPER" "$B_ID"
probe "boli      add a type to A's"       POST "/v1/boli/occasions/$OCCASION_ID/boli-types" "$SUPER" "$B_ID" '{"name":"probe","description":null}'
probe "boli      move A's occasion"       POST "/v1/boli/occasions/$OCCASION_ID/status" "$SUPER" "$B_ID" '{"status":"Closed"}'
probe "boli      close A's Boli"          POST "/v1/boli/boli/$BOLI_ID/close"         "$SUPER" "$B_ID" '{}'
probe "boli      record A's result"       POST "/v1/boli/boli/$BOLI_ID/result"        "$SUPER" "$B_ID" '{}'
probe "boli      publish A's result"      POST "/v1/boli/boli/$BOLI_ID/result/publish" "$SUPER" "$B_ID" '{}'
probe "boli      open a Boli in A's"      POST "/v1/boli/occasions/$OCCASION_ID/boli" "$SUPER" "$B_ID" "{\"boliTypeId\":\"$BOLI_TYPE_ID\",\"title\":\"probe\",\"startAt\":\"2099-01-01T00:00:00Z\",\"endAt\":\"2099-01-02T00:00:00Z\",\"startingAmount\":100,\"minIncrement\":10,\"eligibilityRule\":null}"

# Reads an administrator of another Samaaj should not get either. Who bid what,
# who is coming to an event and who has applied to a group are all facts about
# somebody else's members.
probe "boli      read A's bid history"    GET  "/v1/boli/boli/$BOLI_ID/bids"           "$SUPER" "$B_ID"
probe "boli      read A's Boli result"    GET  "/v1/boli/boli/$BOLI_ID/result"         "$SUPER" "$B_ID"
probe "voting    read A's campaign result" GET "/v1/celebrity-voting/campaigns/$CAMPAIGN_ID/results" "$SUPER" "$B_ID"
probe "events    read A's attendees"      GET  "/v1/events/$EVENT_ID/attendees"        "$SUPER" "$B_ID"
probe "groups    read A's applications"   GET  "/v1/volunteer-groups/groups/$GROUP_ID/applications" "$SUPER" "$B_ID"
probe "events    cancel A's registration" DELETE "/v1/events/$EVENT_ID/registration"   "$SUPER" "$B_ID"

echo
echo "== what this run did not probe =="

# **This section exists because the script silently went stale and said nothing.**
#
# On 2026-09-02 it was found to be covering 36 of the platform's 73 id-taking
# endpoints. The Pathshala teaching cluster - the register, the roll, exam
# results, placing and withdrawing a child - had been added months earlier and
# no probe here had ever touched it. Nothing said so: the run printed "every
# cross-tenant attempt was refused", which was true and gave entirely the wrong
# impression.
#
# So the script now works out its own coverage rather than being trusted to be
# complete. It reads every id-taking route the services map, the same way
# `unreachable-endpoints.sh` does, and lists the ones no `probe` call above
# reached.
#
# **Listed is not the same as wrong**, which is why the ones that genuinely have
# no cross-tenant meaning are named below rather than left in the list. A list
# with ten permanent entries in it is a list people stop reading, and then it
# stops working the way this one just did.
#
# Each exclusion needs a reason that survives being read six months later. "Not
# got to it yet" is not one - that belongs in the list.
excluded() {
  cat <<'REASONS'
GET /v1/identity/tenants/by-id/{id}|the gateway's own lookup; no app calls it
GET /v1/identity/tenants/{id}|the public Samaaj directory, anonymous by design
PATCH /v1/identity/tenants/{id}/status|platform administration: a Super Admin acting across Samaaj is the point
PUT /v1/identity/tenants/{id}/modules|platform administration, as above
PUT /v1/identity/tenants/{id}/grievance-contact|platform administration, as above
PUT /v1/identity/admins/{id}/roles/{id}|platform administration, as above
PUT /v1/identity/roles/{id}/permissions/{id}|platform administration, as above
POST /v1/identity/activations/{id}/code|platform administration, as above
POST /v1/identity/me/consents/{id}/withdraw|acts on the caller's own consent; the id names a purpose, not a Samaaj
REASONS
}

routes=$(mktemp)
trap 'rm -f "$covered" "$routes"' EXIT

for file in "$(dirname "$0")/.."/services/*/src/*/Endpoints/*.cs; do
  [ -e "$file" ] || continue

  prefix=$({ grep -oE 'MapGroup\("[^"]*"\)' "$file" || true; } | head -1 | sed 's/MapGroup("//; s/")//')

  { grep -oE 'Map(Get|Post|Put|Patch|Delete)\("[^"]*"' "$file" || true; } \
    | sed 's/Map\([A-Za-z]*\)("/\1 /; s/"$//' \
    | while read -r verb path; do
        case "$path" in
          /v1/*) echo "$(printf '%s' "$verb" | tr '[:lower:]' '[:upper:]') $path" ;;
          *) echo "$(printf '%s' "$verb" | tr '[:lower:]' '[:upper:]') ${prefix}${path}" ;;
        esac
      done
done | sed 's/{[a-zA-Z]*:guid}/{id}/g; s/{[a-zA-Z]*}/{id}/g' | grep '{id}' | sort -u > "$routes"

unprobed=0
skipped=0
total=$(wc -l < "$routes" | tr -d ' ')

while read -r verb path; do
  grep -qxF "$verb $path" "$covered" && continue

  # `|| true` because a grep that matches nothing exits 1, and with
  # `set -euo pipefail` that kills the script mid-report - which is exactly how
  # this section failed the first time it ran, printing its heading and then
  # nothing at all. `unreachable-endpoints.sh` carries the same note for the
  # same reason.
  reason=$({ excluded | grep -F "$verb $path|" || true; } | head -1 | cut -d'|' -f2-)

  if [ -n "$reason" ]; then
    skipped=$((skipped + 1))
  else
    echo "  ..    $verb $path"
    unprobed=$((unprobed + 1))
  fi
done < "$routes"

if [ "$unprobed" -eq 0 ]; then
  echo "  (nothing - every id-taking endpoint with a cross-tenant meaning was probed)"
fi

echo
echo "  id-taking endpoints: $total"
echo "  probed:              $((total - unprobed - skipped))"
echo "  deliberately not:    $skipped   (see 'excluded' in this script for why)"
echo "  NOT PROBED:          $unprobed"

echo
echo "=================================================="
echo "  clean refusals (404):        $pass"
echo "  refused but confirmed (403): $leaked"
echo "  NOT REFUSED:                 $fail"
echo "=================================================="

if [ "$fail" -gt 0 ]; then
  echo "A cross-tenant write was not refused. This is a tenant-isolation failure."
  exit 1
fi

if [ "$leaked" -gt 0 ]; then
  echo "Nothing was written across a tenant boundary, but some endpoints answer 403"
  echo "where they should answer 404 - a 403 confirms the id is real."
  exit 2
fi

echo "Every cross-tenant attempt was refused with 404."
