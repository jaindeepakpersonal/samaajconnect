# Sourced by smoke-through-gateway.sh and tenant-isolation-probe.sh, never run.
# Needs $GATEWAY and json_field from the script sourcing it.

# ---- Signing in (2026-09-27): a CAPTCHA and a password, then a code ------------
#
# identity-tenant-service asks for both on every sign-in, and this script has
# no eyes to read a picture and no phone to receive a code. The compose stack
# sets SignInSecurity__RevealSecretsInLogs, so the CAPTCHA's answer and the
# sign-in code are written to the identity service's own log; these read them
# from there, the way a member reads the picture and their SMS. Nothing else on
# the platform - and no other environment - reveals either.

# The last line of the identity log matching $1 since $2, word $3 of the match.
identity_log_word() {
  local found=""
  for _ in $(seq 1 40); do
    found=$(docker compose logs --no-log-prefix --since "$2" identity-tenant-service 2>/dev/null \
      | { grep -o "$1" || true; } | tail -1 | awk -v n="$3" '{print $n}')
    [ -n "$found" ] && break
    sleep 0.25
  done
  printf '%s' "$found"
}

# $1, a login JSON body, with a freshly solved CAPTCHA added before its last brace.
with_captcha() {
  local since id answer
  since=$(date -u -d '-5 seconds' +%Y-%m-%dT%H:%M:%SZ)
  id=$(curl -s -X POST "$GATEWAY/v1/identity/captcha" | json_field captchaId)
  answer=$(identity_log_word "CAPTCHA $id answer [A-Z0-9]*" "$since" 4)
  printf '%s,"captchaId":"%s","captchaAnswer":"%s"}' "${1%\}}" "$id" "$answer"
}

# Signs in all the way and prints the response carrying the session, so
# `json_field accessToken` / `refreshToken` read it as before. $1 is the body.
sign_in() {
  local since response challenge identifier code
  since=$(date -u -d '-2 seconds' +%Y-%m-%dT%H:%M:%SZ)
  response=$(curl -s -X POST "$GATEWAY/v1/identity/login" -H 'Content-Type: application/json' \
    -d "$(with_captcha "$1")")

  if ! printf '%s' "$response" | grep -q '"codeRequired":true'; then
    printf '%s' "$response"
    return
  fi

  challenge=$(printf '%s' "$response" | json_field challengeToken)
  identifier=$(printf '%s' "$1" | json_field mobileOrEmail | tr '[:upper:]' '[:lower:]')
  code=$(identity_log_word "Sign-in code [0-9]* for $identifier" "$since" 3)

  curl -s -X POST "$GATEWAY/v1/identity/login/verify" -H 'Content-Type: application/json' \
    -d "{\"challengeToken\":\"$challenge\",\"code\":\"$code\"}"
}
