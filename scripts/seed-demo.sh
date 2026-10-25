#!/usr/bin/env bash
#
# Seeds a fresh stack with one realistic Samaaj, entirely through the gateway's
# public API - the same calls the two portals make, so what this produces is
# data the platform itself would have produced, events and audit rows included.
#
#   docker compose down -v && docker compose up -d
#   bash scripts/seed-demo.sh
#
# **A fresh database, not a re-run.** It creates fixed identifiers and names
# and stops at the first one that already exists, rather than guessing which
# half of a previous run to trust. `docker compose down -v` is the reset.
#
# What it makes:
#   - master data: the migration's seeded countries, states and cities as they
#     are, plus Parsad, Sarada and Chawand (Rajasthan) and five Udaipur
#     localities. Udaipur, Mumbai and Pune are already in the seeded list.
#   - one Samaaj, Shri Digambar Jain Dasha Nagda Samaaj, with every module on
#   - its five Gautras: Mundfoda, Pujavat, Pancholi, Mundaliya, Devda
#   - a Samaaj Admin, and an Area Admin for each locality
#   - 50 members, each with a random Gautra, in five households of ten (family
#     n lives in locality n), each with a family head
#   - five volunteer groups of ten, each with President, Vice President,
#     Secretary and Treasurer filled by election and everybody else a Volunteer
#   - ten published events
#   - a Celebrities of Samaaj campaign: five nominees, every member votes once,
#     and the ranking is published
#
# The accounts it creates sign in with the passwords below. They are demo
# credentials for a local stack and exist nowhere else.
#
# Like the smoke script, it reads CAPTCHA answers and sign-in codes from the
# identity service's log (lib/sign-in.sh), which only the compose stack writes.
set -euo pipefail

GATEWAY="${GATEWAY:-http://localhost:8080}"
SUPERADMIN="${SUPERADMIN:-superadmin@samaajconnect.local}"
SUPERADMIN_PASSWORD="${SUPERADMIN_PASSWORD:-superadminpassword}"

DEMO_DOMAIN="dashanagda.test"
MEMBER_PASSWORD="${MEMBER_PASSWORD:-dashanagda-member-2026}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-dashanagda-admin-2026}"

SAMAAJ_NAME="Shri Digambar Jain Dasha Nagda Samaaj"
SLUG="dasha-nagda"

# Ids the master-data migration seeds (Domain/Seeding/DefaultMasterData.cs).
INDIA="c0000000-0000-0000-0000-000000000001"
RAJASTHAN="d0000000-0000-0000-0000-000000000021"
UDAIPUR="e0000000-0000-0000-0000-000000000002"

# The same "random" Gautras on every run of a fresh stack.
RANDOM=2026

status() {
  curl -s -o /dev/null -w '%{http_code}' "$@"
}

json_field() {
  { grep -o "\"$1\":\"[^\"]*\"" || true; } | head -1 | cut -d'"' -f4
}

# shellcheck source=lib/sign-in.sh
source "$(dirname "$0")/lib/sign-in.sh"

step() { echo; echo "== $*"; }
note() { echo "   $*"; }

die() {
  echo
  echo "   FAILED: $*"
  exit 1
}

# Every id this script goes on to use passes through here. An empty one means
# the call before it was refused, and carrying on would seed against nothing.
need() {
  local name="$1" value="$2" context="${3:-}"
  [ -n "$value" ] || die "no $name came back${context:+ ($context)}"
}

expect() {
  local label="$1" expected="$2" actual="$3"
  [ "$expected" = "$actual" ] || die "$label: expected $expected, got $actual"
}

iso_in() {
  date -u -d "$1" +%Y-%m-%dT%H:%M:%S.000Z
}

# ---- Tokens -------------------------------------------------------------------
#
# Access tokens last fifteen minutes and this run takes longer, so each account's
# token is re-minted once it is ten minutes old rather than held for the run.
declare -A EMAIL PASS TOKEN TOKEN_AT USER_ID

sign_in_account() {
  local key="$1" token
  token=$(sign_in "{\"mobileOrEmail\":\"${EMAIL[$key]}\",\"password\":\"${PASS[$key]}\"}" \
    | json_field accessToken)
  need "token for ${EMAIL[$key]}" "$token"
  TOKEN[$key]=$token
  TOKEN_AT[$key]=$(date +%s)
}

# Sets T to a live token for $1. Not `$(...)`: that would mint into a subshell
# and the cache would never fill.
tok() {
  local key="$1"
  if [ -z "${TOKEN[$key]:-}" ] || [ $(( $(date +%s) - ${TOKEN_AT[$key]} )) -gt 600 ]; then
    sign_in_account "$key"
  fi
  T=${TOKEN[$key]}
}

EMAIL[super]=$SUPERADMIN
PASS[super]=$SUPERADMIN_PASSWORD

wait_for_stack() {
  local attempt=0
  until [ "$(status "$GATEWAY/health")" = "200" ] \
     && [ "$(status "$GATEWAY/v1/identity/tenants/directory")" = "200" ]; do
    attempt=$((attempt + 1))
    [ "$attempt" -lt 90 ] || die "the stack did not become ready"
    sleep 2
  done
}

# ---- The people ---------------------------------------------------------------
#
# Fifty members as five households of ten. Each household is laid out the same
# way, position by position: the head and their spouse, their children, two of
# the head's siblings, and two relatives. Relationships are recorded relative to
# the family admin (the join form asks "your relationship to them"), and the
# head is the household's admin.
MALE_NAMES=(Rajendra Mahendra Sunil Anil Prakash Dinesh Ramesh Suresh Ashok Vinod
            Manoj Sanjay Pankaj Rakesh Nitin Amit Vikas Rohit Ankit Sachin
            Gaurav Tarun Hemant Kamlesh Lalit)
FEMALE_NAMES=(Sushila Kanta Pushpa Sunita Manju Asha Rekha Kiran Neeta Sarita
              Anita Shobha Meena Usha Kusum Preeti Pooja Sneha Neha Ritu
              Swati Payal Divya Komal Shweta)

#                 0      1       2     3     4     5       6       7     8     9
POS_GENDER=(      Male   Female  Male  Female Male Male    Female  Female Male Female)
POS_AGE=(         58     55      32    29    27    54      50      24    30    26)
POS_RELATIONSHIP=(Head   Spouse  Child Child Child Sibling Sibling Child Other Other)

GAUTRA_NAMES=(Mundfoda Pujavat Pancholi Mundaliya Devda)
LOCALITY_NAMES=("Goverdhan Vilas" "Hiran Magri" "Sector 14" "Bhupalpura" "Fatehpura")

declare -a MEMBER_NAME MEMBER_GENDER MEMBER_DOB MEMBER_GAUTRA

male=0
female=0
for i in $(seq 0 49); do
  pos=$((i % 10))
  if [ "${POS_GENDER[$pos]}" = "Male" ]; then
    MEMBER_NAME[$i]="${MALE_NAMES[$male]} Jain"
    male=$((male + 1))
  else
    MEMBER_NAME[$i]="${FEMALE_NAMES[$female]} Jain"
    female=$((female + 1))
  fi
  MEMBER_GENDER[$i]=${POS_GENDER[$pos]}
  MEMBER_DOB[$i]=$(printf '%d-%02d-%02d' $((2026 - POS_AGE[pos])) $((1 + i % 12)) $((1 + (i * 7) % 28)))
  MEMBER_GAUTRA[$i]=$((RANDOM % 5))

  key=$(printf 'm%02d' "$i")
  EMAIL[$key]=$(printf 'member%02d@%s' $((i + 1)) "$DEMO_DOMAIN")
  PASS[$key]=$MEMBER_PASSWORD
done

mkey() { printf 'm%02d' "$1"; }

echo "Seeding $SAMAAJ_NAME through $GATEWAY"
wait_for_stack
tok super
SUPER=$T

# ---- Master data ----------------------------------------------------------------
step "Master data: cities and localities"

for city in Parsad Sarada Chawand; do
  CITY_ID=$(curl -s -X POST "$GATEWAY/v1/master-data/cities" -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $SUPER" \
    -d "{\"countryId\":\"$INDIA\",\"stateId\":\"$RAJASTHAN\",\"name\":\"$city\"}" | json_field id)
  need "city id" "$CITY_ID" "$city"
  note "city $city"
done
note "Udaipur, Pune and Mumbai are already in the seeded list"

declare -a LOCALITY_ID
for n in 0 1 2 3 4; do
  LOCALITY_ID[$n]=$(curl -s -X POST "$GATEWAY/v1/master-data/localities" -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $SUPER" \
    -d "{\"cityId\":\"$UDAIPUR\",\"name\":\"${LOCALITY_NAMES[$n]}\"}" | json_field id)
  need "locality id" "${LOCALITY_ID[$n]}" "${LOCALITY_NAMES[$n]}"
  note "locality ${LOCALITY_NAMES[$n]}, Udaipur"
done

# ---- The Samaaj -------------------------------------------------------------------
step "The Samaaj"

MODULES=$(curl -s "$GATEWAY/v1/identity/tenants/modules" \
  | grep -o '"key":"[^"]*"' | cut -d'"' -f4 | sed 's/.*/"&"/' | paste -sd, -)

TENANT_ID=$(curl -s -X POST "$GATEWAY/v1/identity/tenants" -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $SUPER" \
  -d "{\"name\":\"$SAMAAJ_NAME\",\"slug\":\"$SLUG\",\"enabledModules\":[$MODULES]}" | json_field id)
need "tenant id" "$TENANT_ID" "is the database fresh?"

expect "activating the Samaaj" 200 "$(status -X PATCH "$GATEWAY/v1/identity/tenants/$TENANT_ID/status" \
  -H 'Content-Type: application/json' -H "Authorization: Bearer $SUPER" -d '{"status":"Active"}')"
note "$SAMAAJ_NAME ($SLUG), every module on"

OVERRIDE="X-Tenant-Override-Id: $TENANT_ID"

# ---- Administrators -----------------------------------------------------------------
step "Administrators"

# Invites one administrator and redeems their code, so the account can sign in.
invite_admin() {
  local key="$1" name="$2" role="$3" locality="${4:-}" body response code
  tok super
  body="{\"fullName\":\"$name\",\"mobileOrEmail\":\"${EMAIL[$key]}\",\"roles\":[\"$role\"]"
  [ -n "$locality" ] && body="$body,\"localityId\":\"$locality\""
  body="$body}"

  response=$(curl -s -X POST "$GATEWAY/v1/identity/admins" -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $T" -H "$OVERRIDE" -d "$body")
  USER_ID[$key]=$(printf '%s' "$response" | json_field userId)
  code=$(printf '%s' "$response" | json_field activationCode)
  need "invited user id" "${USER_ID[$key]}" "$name: $response"
  need "activation code" "$code" "$name"

  expect "activating $name" 200 "$(status -X POST "$GATEWAY/v1/identity/activations/redeem" \
    -H 'Content-Type: application/json' \
    -d "{\"mobileOrEmail\":\"${EMAIL[$key]}\",\"code\":\"$code\",\"password\":\"${PASS[$key]}\"}")"
}

EMAIL[sa]="samaaj.admin@$DEMO_DOMAIN"
PASS[sa]=$ADMIN_PASSWORD
invite_admin sa "Bharat Jain" SamaajAdmin
note "Samaaj Admin: Bharat Jain"

AREA_ADMIN_NAMES=("Kailash Jain" "Narendra Jain" "Pradeep Jain" "Subhash Jain" "Mukesh Jain")
for n in 0 1 2 3 4; do
  key="area$n"
  EMAIL[$key]="area.admin.$((n + 1))@$DEMO_DOMAIN"
  PASS[$key]=$ADMIN_PASSWORD
  invite_admin "$key" "${AREA_ADMIN_NAMES[$n]}" AreaAdmin "${LOCALITY_ID[$n]}"
  note "Area Admin for ${LOCALITY_NAMES[$n]}: ${AREA_ADMIN_NAMES[$n]}"
done

# ---- Gautras -------------------------------------------------------------------------
step "Gautras"

declare -a GAUTRA_ID
tok sa
for n in 0 1 2 3 4; do
  GAUTRA_ID[$n]=$(curl -s -X POST "$GATEWAY/v1/master-data/gautras" -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $T" -d "{\"name\":\"${GAUTRA_NAMES[$n]}\"}" | json_field id)
  need "gautra id" "${GAUTRA_ID[$n]}" "${GAUTRA_NAMES[$n]}"
  note "${GAUTRA_NAMES[$n]}"
done

# ---- Members -------------------------------------------------------------------------
step "50 members register"

NOTICE_VERSION=$(curl -s "$GATEWAY/v1/identity/consent-notice" | json_field version)
need "consent notice version" "$NOTICE_VERSION"

for i in $(seq 0 49); do
  key=$(mkey "$i")
  expect "registering ${MEMBER_NAME[$i]}" 201 "$(status -X POST "$GATEWAY/v1/identity/register" \
    -H 'Content-Type: application/json' \
    -d "{\"tenantSlug\":\"$SLUG\",\"fullName\":\"${MEMBER_NAME[$i]}\",\"mobileOrEmail\":\"${EMAIL[$key]}\",\"password\":\"${PASS[$key]}\",\"consentedPurposes\":[\"Membership\"],\"noticeVersion\":\"$NOTICE_VERSION\"}")"
done
note "registered member01..member50@$DEMO_DOMAIN"

step "Profiles: Gautra, date of birth, address"

# The profile arrives over Kafka after registration, so each is waited for.
for i in $(seq 0 49); do
  key=$(mkey "$i")
  tok "$key"

  ready=0
  for _ in $(seq 1 30); do
    if [ "$(status -H "Authorization: Bearer $T" "$GATEWAY/v1/members/me")" = "200" ]; then
      ready=1
      break
    fi
    sleep 1
  done
  [ "$ready" -eq 1 ] || die "${MEMBER_NAME[$i]}'s profile never arrived"

  USER_ID[$key]=$(curl -s -H "Authorization: Bearer $T" "$GATEWAY/v1/members/me" | json_field id)
  need "member id" "${USER_ID[$key]}" "${MEMBER_NAME[$i]}"

  family=$((i / 10))
  expect "${MEMBER_NAME[$i]}'s profile" 200 "$(status -X PATCH "$GATEWAY/v1/members/${USER_ID[$key]}" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $T" \
    -d "{\"fullName\":\"${MEMBER_NAME[$i]}\",\"dateOfBirth\":\"${MEMBER_DOB[$i]}\",\"gender\":\"${MEMBER_GENDER[$i]}\",\"mobile\":\"+9198290$(printf '%05d' $((i + 1)))\",\"email\":\"${EMAIL[$key]}\",\"gautraId\":\"${GAUTRA_ID[${MEMBER_GAUTRA[$i]}]}\",\"professionId\":null,\"qualificationId\":null,\"permanentAddress\":{\"line\":\"$((10 + i)) ${LOCALITY_NAMES[$family]}\",\"cityId\":\"$UDAIPUR\",\"localityId\":\"${LOCALITY_ID[$family]}\"},\"temporaryAddress\":null,\"privacy\":{\"mobile\":\"SamaajOnly\",\"email\":\"Private\",\"address\":\"Private\",\"profession\":\"SamaajOnly\",\"dateOfBirth\":\"SamaajOnly\",\"qualification\":\"SamaajOnly\"},\"isListedInDirectory\":true}")"
done

for n in 0 1 2 3 4; do
  count=0
  for i in $(seq 0 49); do
    [ "${MEMBER_GAUTRA[$i]}" -eq "$n" ] && count=$((count + 1))
  done
  note "${GAUTRA_NAMES[$n]}: $count members"
done

# ---- Families ------------------------------------------------------------------------
step "Five households of ten"

declare -a FAMILY_ID
for f in 0 1 2 3 4; do
  head=$((f * 10))
  head_key=$(mkey "$head")
  tok "$head_key"
  HEAD_TOKEN=$T

  expect "${MEMBER_NAME[$head]} creates a household" 201 "$(status -X POST "$GATEWAY/v1/families" \
    -H "Authorization: Bearer $HEAD_TOKEN")"

  FAMILY=$(curl -s -H "Authorization: Bearer $HEAD_TOKEN" "$GATEWAY/v1/families/mine")
  FAMILY_ID[$f]=$(printf '%s' "$FAMILY" | json_field id)
  CODE=$(printf '%s' "$FAMILY" | json_field familyCode)
  need "family id" "${FAMILY_ID[$f]}"
  need "family code" "$CODE"

  for pos in $(seq 1 9); do
    i=$((head + pos))
    key=$(mkey "$i")
    tok "$key"
    expect "${MEMBER_NAME[$i]} asks to join" 200 "$(status -X POST "$GATEWAY/v1/families/join-requests" \
      -H 'Content-Type: application/json' -H "Authorization: Bearer $T" \
      -d "{\"familyCode\":\"$CODE\",\"relationship\":\"${POS_RELATIONSHIP[$pos]}\"}")"

    tok "$head_key"
    REQUEST_ID=$(curl -s -H "Authorization: Bearer $T" "$GATEWAY/v1/families/mine" \
      | grep -o "\"id\":\"[^\"]*\",\"memberProfileId\":\"${USER_ID[$key]}\"" | cut -d'"' -f4)
    need "join request id" "$REQUEST_ID" "${MEMBER_NAME[$i]}"

    expect "accepting ${MEMBER_NAME[$i]}" 200 "$(status -X POST \
      "$GATEWAY/v1/families/${FAMILY_ID[$f]}/join-requests/$REQUEST_ID/decide" \
      -H 'Content-Type: application/json' -H "Authorization: Bearer $T" -d '{"accept":true}')"
  done

  tok "$head_key"
  expect "naming the head" 200 "$(status -X PUT "$GATEWAY/v1/families/mine/head" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $T" \
    -d "{\"memberProfileId\":\"${USER_ID[$head_key]}\"}")"

  note "household of ${MEMBER_NAME[$head]} in ${LOCALITY_NAMES[$f]}: 10 members, head ${MEMBER_NAME[$head]}"
done

# ---- Volunteer groups ------------------------------------------------------------------
step "Five volunteer groups, with elections"

GROUP_NAMES=("Jain Seva Samiti" "Yuva Mandal" "Shiksha Samiti" "Jinvani Prachar Samiti" "Swasthya Seva Dal")
GROUP_FOCUS=("Social Service" "Youth" "Education" "Religious" "Health")
ELECTED_ROLES=("Vice President" "Secretary" "Treasurer")

# The role id for a title, read off whatever response lists the group's roles.
role_id() {
  printf '%s' "$1" | grep -o "\"id\":\"[^\"]*\",\"title\":\"$2\"" | head -1 | cut -d'"' -f4
}

# Runs one election for one role: $winner beats $loser, everybody in the group
# votes, and publishing installs the winner. Nominating needs a group member, so
# $nominator puts both forward; the Samaaj Admin runs the rest.
run_election() {
  local group_id="$1" role_id="$2" title="$3" winner="$4" loser="$5" nominator="$6"
  shift 6
  local voters=("$@")
  local now nom_end vote_end election_id winner_candidate loser_candidate candidate

  now=$(iso_in "now")
  nom_end=$(iso_in "+4 seconds")
  vote_end=$(iso_in "+10 minutes")

  tok sa
  election_id=$(curl -s -X POST "$GATEWAY/v1/volunteer-groups/groups/$group_id/elections" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $T" \
    -d "{\"roleId\":\"$role_id\",\"nominationStartAt\":\"$now\",\"nominationEndAt\":\"$nom_end\",\"votingStartAt\":\"$nom_end\",\"votingEndAt\":\"$vote_end\"}" \
    | json_field id)
  need "election id" "$election_id" "$title"

  expect "$title: nominations open" 200 "$(status -X POST "$GATEWAY/v1/volunteer-groups/elections/$election_id/status" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $T" -d '{"status":"NominationsOpen"}')"

  tok "$nominator"
  winner_candidate=$(curl -s -X POST "$GATEWAY/v1/volunteer-groups/elections/$election_id/candidates" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $T" \
    -d "{\"memberId\":\"${USER_ID[$winner]}\"}" | json_field candidateId)
  loser_candidate=$(curl -s -X POST "$GATEWAY/v1/volunteer-groups/elections/$election_id/candidates" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $T" \
    -d "{\"memberId\":\"${USER_ID[$loser]}\"}" | json_field candidateId)
  need "candidate id" "$winner_candidate" "$title"
  need "candidate id" "$loser_candidate" "$title"

  tok sa
  for candidate in "$winner_candidate" "$loser_candidate"; do
    expect "$title: approving a nomination" 200 "$(status -X POST \
      "$GATEWAY/v1/volunteer-groups/elections/$election_id/candidates/$candidate/decide" \
      -H 'Content-Type: application/json' -H "Authorization: Bearer $T" -d '{"approve":true}')"
  done

  while [ "$(date -u +%s)" -le "$(date -u -d "$nom_end" +%s)" ]; do sleep 1; done

  tok sa
  expect "$title: voting opens" 200 "$(status -X POST "$GATEWAY/v1/volunteer-groups/elections/$election_id/status" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $T" -d '{"status":"VotingOpen"}')"

  # Everybody votes for the winner except the winner, who votes for the other
  # candidate - nine to one.
  local voter pick
  for voter in "${voters[@]}"; do
    pick=$winner_candidate
    [ "$voter" = "$winner" ] && pick=$loser_candidate
    tok "$voter"
    expect "$title: a vote" 200 "$(status -X POST "$GATEWAY/v1/volunteer-groups/elections/$election_id/votes" \
      -H 'Content-Type: application/json' -H "Authorization: Bearer $T" -d "{\"candidateIds\":[\"$pick\"]}")"
  done

  tok sa
  expect "$title: voting closes" 200 "$(status -X POST "$GATEWAY/v1/volunteer-groups/elections/$election_id/status" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $T" -d '{"status":"Closed"}')"

  curl -s -X POST "$GATEWAY/v1/volunteer-groups/elections/$election_id/publish" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $T" -d '{}' \
    | grep -qF "\"winnerMemberIds\":[\"${USER_ID[$winner]}\"]" \
    || die "$title: publishing did not name the winner"
}

for g in 0 1 2 3 4; do
  # Group g is every fifth member, so each group draws from all five households.
  members=()
  for k in $(seq 0 9); do
    members+=("$(mkey $((g + 5 * k)))")
  done
  interim=${members[0]}

  tok sa
  GROUP_ID=$(curl -s -X POST "$GATEWAY/v1/volunteer-groups/groups" -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $T" \
    -d "{\"name\":\"${GROUP_NAMES[$g]}\",\"description\":\"${GROUP_FOCUS[$g]} work in the Samaaj.\",\"focusArea\":\"${GROUP_FOCUS[$g]}\",\"presidentMemberId\":\"${USER_ID[$interim]}\"}" \
    | json_field id)
  need "group id" "$GROUP_ID" "${GROUP_NAMES[$g]}"

  # The other nine apply, and the interim president accepts each.
  for member in "${members[@]:1}"; do
    tok "$member"
    expect "applying to ${GROUP_NAMES[$g]}" 200 "$(status -X POST "$GATEWAY/v1/volunteer-groups/groups/$GROUP_ID/applications" \
      -H 'Content-Type: application/json' -H "Authorization: Bearer $T" -d '{"note":"Happy to help."}')"
  done

  tok "$interim"
  for _ in $(seq 1 9); do
    APPLICATION_ID=$(curl -s -H "Authorization: Bearer $T" \
      "$GATEWAY/v1/volunteer-groups/groups/$GROUP_ID/applications" | json_field id)
    need "application id" "$APPLICATION_ID" "${GROUP_NAMES[$g]}"
    expect "accepting an application" 200 "$(status -X POST \
      "$GATEWAY/v1/volunteer-groups/groups/$GROUP_ID/applications/$APPLICATION_ID/decide" \
      -H 'Content-Type: application/json' -H "Authorization: Bearer $T" -d '{"accept":true}')"
  done

  # Volunteer and President are built in; the other three are defined here.
  tok sa
  for title in "${ELECTED_ROLES[@]}"; do
    ROLES=$(curl -s -X POST "$GATEWAY/v1/volunteer-groups/groups/$GROUP_ID/roles" \
      -H 'Content-Type: application/json' -H "Authorization: Bearer $T" \
      -d "{\"title\":\"$title\",\"seats\":1}")
  done

  PRESIDENT_ROLE=$(role_id "$ROLES" President)
  need "President role id" "$PRESIDENT_ROLE" "${GROUP_NAMES[$g]}: $ROLES"

  # members[1..4] win President, Vice President, Secretary and Treasurer;
  # each runs against the next one along, and members[9] nominates throughout.
  titles=(President "${ELECTED_ROLES[@]}")
  for r in 0 1 2 3; do
    title=${titles[$r]}
    rid=$(role_id "$ROLES" "$title")
    need "$title role id" "$rid" "${GROUP_NAMES[$g]}"
    run_election "$GROUP_ID" "$rid" "${GROUP_NAMES[$g]} $title" \
      "${members[$((r + 1))]}" "${members[$((r + 2))]}" "${members[9]}" "${members[@]}"
  done

  tok sa
  GROUP=$(curl -s -H "Authorization: Bearer $T" "$GATEWAY/v1/volunteer-groups/groups/$GROUP_ID")
  volunteers=$(printf '%s' "$GROUP" | grep -o '"rolePosition":"Volunteer"' | wc -l)
  note "${GROUP_NAMES[$g]}: President ${MEMBER_NAME[$((g + 5))]}, Vice President ${MEMBER_NAME[$((g + 10))]}, Secretary ${MEMBER_NAME[$((g + 15))]}, Treasurer ${MEMBER_NAME[$((g + 20))]}, $((volunteers)) Volunteers"
done

# ---- Events ----------------------------------------------------------------------------
step "Ten events"

EVENT_TITLES=("Paryushan Parv Pravachan Mala" "Das Lakshan Mahaparv" "Mahavir Jayanti Shobha Yatra"
              "Kshamavani Parv Sammelan" "Annual General Meeting" "Blood Donation Camp"
              "Free Medical Check-up Camp" "Scholarship Distribution Samaroh"
              "Yuva Sports Day" "Diwali Sneh Milan")
EVENT_DAYS=(7 14 21 28 35 42 49 56 63 70)
EVENT_CAPACITY=(null null null 300 null 100 150 200 80 null)

tok sa
for n in $(seq 0 9); do
  locality=null
  venue="Dasha Nagda Bhawan, Udaipur"
  if [ "$n" -ge 5 ]; then
    locality="\"${LOCALITY_ID[$((n - 5))]}\""
    venue="Community Hall, ${LOCALITY_NAMES[$((n - 5))]}"
  fi
  # 10:00 to 14:00 in Udaipur, which is 04:30 to 08:30 UTC.
  day=$(date -u -d "+${EVENT_DAYS[$n]} days" +%Y-%m-%d)
  start="${day}T04:30:00Z"
  end="${day}T08:30:00Z"

  EVENT_ID=$(curl -s -X POST "$GATEWAY/v1/events" -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $T" \
    -d "{\"title\":\"${EVENT_TITLES[$n]}\",\"description\":\"${EVENT_TITLES[$n]}, organised by the Samaaj.\",\"startAt\":\"$start\",\"endAt\":\"$end\",\"venue\":\"$venue\",\"eventTypeId\":null,\"organizerType\":\"Samaaj\",\"organizerId\":null,\"registrationEnabled\":true,\"capacity\":${EVENT_CAPACITY[$n]},\"localityId\":$locality}" \
    | json_field id)
  need "event id" "$EVENT_ID" "${EVENT_TITLES[$n]}"

  expect "publishing ${EVENT_TITLES[$n]}" 200 "$(status -X POST "$GATEWAY/v1/events/$EVENT_ID/publish" \
    -H "Authorization: Bearer $T")"
  note "${EVENT_TITLES[$n]} (in ${EVENT_DAYS[$n]} days)"
done

# ---- Celebrities of Samaaj ---------------------------------------------------------------
step "Celebrities of Samaaj"

NOMINEES=(0 11 22 33 44)
NOMINEE_CATEGORY=("Community service" "Education" "Religious" "Business" "Social work")
# Votes each nominee receives - fifty in all, one per member - so the ranking
# comes out in this order.
QUOTA=(15 12 10 8 5)

NOMINATIONS_OPEN=$(iso_in "-1 minute")
NOMINATIONS_CLOSE=$(iso_in "+40 seconds")
VOTING_CLOSES=$(iso_in "+7 days")

tok sa
CAMPAIGN_ID=$(curl -s -X POST "$GATEWAY/v1/celebrity-voting/campaigns" -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $T" \
  -d "{\"title\":\"Celebrities of Dasha Nagda Samaaj 2026\",\"description\":\"Members who made a difference this year.\",\"nominationStartAt\":\"$NOMINATIONS_OPEN\",\"nominationEndAt\":\"$NOMINATIONS_CLOSE\",\"votingStartAt\":\"$NOMINATIONS_CLOSE\",\"votingEndAt\":\"$VOTING_CLOSES\",\"topN\":5,\"resultsVisibility\":\"Live\"}" \
  | json_field id)
need "campaign id" "$CAMPAIGN_ID"

expect "nominations open" 200 "$(status -X POST "$GATEWAY/v1/celebrity-voting/campaigns/$CAMPAIGN_ID/status" \
  -H 'Content-Type: application/json' -H "Authorization: Bearer $T" -d '{"status":"NominationsOpen"}')"

declare -a CANDIDATE
for n in 0 1 2 3 4; do
  # Put forward by the member after them, never by themselves.
  tok "$(mkey $((NOMINEES[n] + 1)))"
  CANDIDATE[$n]=$(curl -s -X POST "$GATEWAY/v1/celebrity-voting/campaigns/$CAMPAIGN_ID/candidates" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $T" \
    -d "{\"memberId\":\"${USER_ID[$(mkey "${NOMINEES[$n]}")]}\",\"category\":\"${NOMINEE_CATEGORY[$n]}\"}" \
    | json_field candidateId)
  need "candidate id" "${CANDIDATE[$n]}" "${MEMBER_NAME[${NOMINEES[$n]}]}"

  tok sa
  expect "approving a nomination" 200 "$(status -X POST \
    "$GATEWAY/v1/celebrity-voting/campaigns/$CAMPAIGN_ID/candidates/${CANDIDATE[$n]}/decide" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $T" -d '{"approve":true}')"
  note "nominated ${MEMBER_NAME[${NOMINEES[$n]}]} (${NOMINEE_CATEGORY[$n]})"
done

note "waiting for nominations to close"
while [ "$(date -u +%s)" -le "$(date -u -d "$NOMINATIONS_CLOSE" +%s)" ]; do sleep 2; done

tok sa
expect "voting opens" 200 "$(status -X POST "$GATEWAY/v1/celebrity-voting/campaigns/$CAMPAIGN_ID/status" \
  -H 'Content-Type: application/json' -H "Authorization: Bearer $T" -d '{"status":"VotingOpen"}')"

# Each member takes the first nominee with votes still to give who is not
# themselves - nobody may vote for themselves.
for i in $(seq 0 49); do
  pick=-1
  for n in 0 1 2 3 4; do
    if [ "${QUOTA[$n]}" -gt 0 ] && [ "${NOMINEES[$n]}" -ne "$i" ]; then
      pick=$n
      break
    fi
  done
  [ "$pick" -ge 0 ] || die "no nominee left for ${MEMBER_NAME[$i]} to vote for"
  QUOTA[$pick]=$((QUOTA[pick] - 1))

  tok "$(mkey "$i")"
  curl -s -X POST "$GATEWAY/v1/celebrity-voting/campaigns/$CAMPAIGN_ID/votes" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $T" \
    -d "{\"candidateId\":\"${CANDIDATE[$pick]}\"}" | grep -q '"accepted":true' \
    || die "${MEMBER_NAME[$i]}'s vote was not accepted"
done
note "50 votes cast"

tok sa
expect "voting closes" 200 "$(status -X POST "$GATEWAY/v1/celebrity-voting/campaigns/$CAMPAIGN_ID/status" \
  -H 'Content-Type: application/json' -H "Authorization: Bearer $T" -d '{"status":"Closed"}')"

RESULT=$(curl -s -X POST "$GATEWAY/v1/celebrity-voting/campaigns/$CAMPAIGN_ID/results" \
  -H "Authorization: Bearer $T")
printf '%s' "$RESULT" | grep -q '"publishedAt"' || die "publishing the ranking: $RESULT"

for n in 0 1 2 3 4; do
  note "rank $((n + 1)): ${MEMBER_NAME[${NOMINEES[$n]}]} (${NOMINEE_CATEGORY[$n]})"
done

step "Done"
note "Samaaj:        $SAMAAJ_NAME ($SLUG)"
note "Samaaj Admin:  ${EMAIL[sa]}"
note "Area Admins:   area.admin.1..5@$DEMO_DOMAIN"
note "Members:       member01..member50@$DEMO_DOMAIN"
note "Passwords are at the top of this script."
