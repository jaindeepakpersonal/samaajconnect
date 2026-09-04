#!/usr/bin/env bash
#
# The module keys agree in the three places that have to know them.
#
# Both sides already say the rule in prose. `ModuleCatalog`'s remarks: "Adding a
# module means adding a key here, a route block in `gateway/` with matching
# Metadata.module, and a row in the table below." `libs/shared`'s `ModuleKeys`:
# "Adding a module means adding it in three places." Neither was enforced by
# anything, which is the shape this repository keeps finding - a rule stated
# where it will be read only by somebody who already knows it.
#
# What goes wrong is specific, and has happened:
#
#   - **A portal key the catalogue has never heard of.** The filter does not
#     fail. It never matches, so the feature is missing from the portal for
#     every Samaaj, forever, with nothing logged anywhere. member-portal's Home
#     filtered its Events and Volunteer tiles on `Events` and `VolunteerGroups`,
#     neither of which is a module key, and both tiles were invisible to
#     everybody until somebody read the catalogue.
#   - **A gateway route gated on a key no Samaaj can enable.** ModuleGateMiddleware
#     looks for the route's key in the tenant's enabled list and answers 404 when
#     it is absent. A key that cannot be enabled makes the route permanently 404,
#     indistinguishable from a Samaaj that switched the module off.
#   - **A catalogue key with no route.** A toggle in the admin panel that
#     switches nothing.
#
# Each list is read from the file that owns it. There is no fourth copy here.
#
# It needs no running stack: it reads the source.
set -euo pipefail

cd "$(dirname "$0")/.."

CATALOG=services/identity-tenant-service/src/Sangam.IdentityTenant.Domain/Tenants/ModuleCatalog.cs
SHARED=libs/shared/src/tenant/module-keys.ts
GATEWAY_CONFIG=gateway/src/Sangam.Gateway/appsettings.json

for file in "$CATALOG" "$SHARED" "$GATEWAY_CONFIG"; do
  if [ ! -f "$file" ]; then
    echo "  FAIL  $file is missing - this check cannot read the module keys"
    exit 1
  fi
done

pass=0
fail=0

ok()  { echo "  ok    $1"; pass=$((pass + 1)); }
bad() { echo "  FAIL  $1"; fail=$((fail + 1)); }

# The catalogue: `public const string Community = "community";`
catalog=$(grep -oE 'public const string [A-Za-z]+ = "[a-z-]+";' "$CATALOG" \
  | grep -oE '"[a-z-]+"' | tr -d '"' | sort -u)

# The shared library: `Community: 'community',` inside ModuleKeys.
shared=$(sed -n '/export const ModuleKeys = {/,/} as const;/p' "$SHARED" \
  | grep -oE ": *'[a-z-]+'" | grep -oE "'[a-z-]+'" | tr -d "'" | sort -u)

# The gateway: `"module": "community"` in a route's metadata. Repeats are fine -
# three routes share `community` - so this is the distinct set.
gateway=$(grep -oE '"module": *"[a-z-]+"' "$GATEWAY_CONFIG" \
  | grep -oE '"[a-z-]+"$' | tr -d '"' | sort -u)

echo "== module keys =="
echo

for name in catalog shared gateway; do
  eval "value=\$$name"
  if [ -z "$value" ]; then
    echo "  FAIL  read no keys out of the $name - its shape changed"
    exit 1
  fi
done

printf '  catalogue (%s): %s\n' \
  "$(printf '%s\n' "$catalog" | grep -c .)" "$(printf '%s' "$catalog" | tr '\n' ' ')"
printf '  libs/shared (%s): %s\n' \
  "$(printf '%s\n' "$shared" | grep -c .)" "$(printf '%s' "$shared" | tr '\n' ' ')"
printf '  gateway (%s): %s\n' \
  "$(printf '%s\n' "$gateway" | grep -c .)" "$(printf '%s' "$gateway" | tr '\n' ' ')"
echo

compare() {
  local what="$1" expected="$2" actual="$3" missing extra

  missing=$(comm -23 <(printf '%s\n' "$expected") <(printf '%s\n' "$actual"))
  extra=$(comm -13 <(printf '%s\n' "$expected") <(printf '%s\n' "$actual"))

  if [ -z "$missing" ] && [ -z "$extra" ]; then
    ok "$what"
    return 0
  fi

  bad "$what"
  [ -n "$extra" ] && printf '%s\n' "$extra" \
    | sed 's/^/          not in ModuleCatalog: /'
  [ -n "$missing" ] && printf '%s\n' "$missing" \
    | sed 's/^/          in ModuleCatalog and missing here: /'
  return 0
}

compare "libs/shared's ModuleKeys matches ModuleCatalog" "$catalog" "$shared"

# The gateway is the one asymmetric case, and deliberately: identity, audit and
# notifications are platform infrastructure with no module key at all, so the
# gateway names a subset of the catalogue rather than all of it. A key it uses
# that the catalogue lacks is still a route nobody can reach.
unknown_gate=$(comm -13 <(printf '%s\n' "$catalog") <(printf '%s\n' "$gateway"))

if [ -z "$unknown_gate" ]; then
  ok "every module a gateway route gates on is a key a Samaaj can enable"
else
  bad "a gateway route gates on a module no Samaaj can enable - permanently 404:"
  printf '%s\n' "$unknown_gate" | sed 's/^/          /'
fi

ungated=$(comm -23 <(printf '%s\n' "$catalog") <(printf '%s\n' "$gateway"))

if [ -z "$ungated" ]; then
  ok "and every module in the catalogue gates at least one route"
else
  bad "a module can be switched on and off and gates nothing:"
  printf '%s\n' "$ungated" | sed 's/^/          /'
fi

# There is deliberately no check here for a module key written as a bare string
# in a component, which is the mistake `ModuleKeys` was written to prevent. It
# was tried and removed for two reasons, both worth recording so it is not tried
# again.
#
# It cannot be done by grepping for the key names: `path: 'pathshala'` in both
# apps' route tables is a URL segment that happens to spell a module, and four
# of those were the only thing the check ever found.
#
# And the real case is already a compile error. `ModuleKey` is the union of the
# catalogue's values and `ModuleTile.moduleKey` is typed to it, so the original
# bug - `moduleKey: 'Events'`, a key the platform has never had, which made two
# Home tiles invisible to every Samaaj - does not compile any more. A literal
# that *is* a real key is assignable and harmless. The type system holds this
# one; a script here would only be a worse copy of it.

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
