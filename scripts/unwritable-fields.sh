#!/usr/bin/env bash
#
# Lists domain properties that nothing in their service can ever write.
#
# This is the mirror of scripts/unreachable-endpoints.sh, and it exists because
# the repository found one of these the hard way. `Tenant.LogoUrl` sat on the
# record from the first migration with a private setter and **no assignment
# anywhere in the service**: no command took a logo, so the column was null on
# every row the platform ever had, while the admin wireframe drew an "Upload
# Logo" control with nothing behind it. Worse, the field was documented as
# carrying a third-party tracking risk - a security note about something that
# could not happen, which dilutes the notes that can.
#
# The general shape: **a field the API can read and nothing can write is the
# same family of gap as an endpoint with no caller.** One sweep finds the
# second; this finds the first.
#
# What counts as a write: `X = `, `X += `, `X -= `, `X++`, `X--`. Counting only
# `=` was the first version and it reported `ActivationCode.FailedAttempts`,
# which `RecordFailedAttempt()` increments - a false positive that would have
# trained somebody to skim the list.
#
# **Reported, never failed**, like the endpoint sweep. A property with no writer
# is not automatically a bug: one could legitimately be materialised by EF and
# never assigned in code. Read the list; do not clear it.
#
# It needs no running stack: it reads the source.
set -euo pipefail

cd "$(dirname "$0")/.."

echo "== domain properties nothing can write =="
echo

total=0
unwritable=0

for service in services/*-service; do
  domain=$(ls -d "$service"/src/*.Domain 2>/dev/null | head -1)
  [ -z "$domain" ] && continue

  name=$(basename "$service")

  # Every property with a private setter, and the file it is declared in. The
  # token before `{` is the property name - robust against generics, which carry
  # no spaces (`IReadOnlyCollection<string>`), where a type-then-name regex is
  # not.
  while IFS= read -r declaration; do
    [ -z "$declaration" ] && continue

    file=${declaration%%:*}
    property=${declaration#*:}

    [ -z "$property" ] && continue

    total=$((total + 1))

    # Anywhere in the service's own source. Migrations are excluded: a model
    # snapshot names every property as a string and assigns none of them, so
    # including them would change nothing, and excluding them keeps the sweep
    # fast on the services with dozens of migrations.
    writes=$({ grep -rhoE "\b${property}\b *(=[^=]|\+\+|--|\+=|-=)" "$service/src" \
      --include='*.cs' 2>/dev/null \
      | grep -v '/obj/' || true; } | wc -l)

    # And in raw SQL, under the snake_case name EFCore.NamingConventions maps
    # the property to.
    #
    # This is not a nicety. `Notification.DeliveryAttempts` is written by
    # `delivery_attempts = delivery_attempts + 1` inside the claim query, and it
    # has to be: the increment is atomic with claiming the row, which a C#
    # assignment could not be. Reporting it would put a permanent, correct entry
    # on this list - and a list with a known-good entry on it is one people learn
    # to skim, which is the failure this sweep exists to avoid rather than cause.
    if [ "$writes" -eq 0 ]; then
      snake=$(printf '%s' "$property" \
        | sed 's/\([a-z0-9]\)\([A-Z]\)/\1_\2/g' \
        | tr '[:upper:]' '[:lower:]')

      writes=$({ grep -rhoE "\b${snake}\b *=[^=]" "$service/src" \
        --include='*.cs' 2>/dev/null \
        | grep -v '/obj/' || true; } | wc -l)
    fi

    if [ "$writes" -eq 0 ]; then
      printf '  %-28s %-28s %s\n' "$name" "$property" "$(basename "$file")"
      unwritable=$((unwritable + 1))
    fi
  done <<< "$(
    find "$domain" -name '*.cs' ! -path '*/obj/*' ! -path '*/bin/*' -print0 \
      | xargs -0 grep -n '{ get; private set; }' 2>/dev/null \
      | awk -F: '{
          file = $1
          line = $0
          sub(/^[^:]*:[0-9]*:/, "", line)
          n = split(line, parts, " ")
          for (i = 1; i <= n; i++) {
            if (parts[i] == "{") { print file ":" parts[i - 1]; break }
          }
        }'
  )"
done

if [ "$unwritable" -eq 0 ]; then
  echo "  (none - every domain property has something that can set it)"
fi

echo
echo "=================================================="
echo "  properties with a private setter: $total"
echo "  nothing can write:                $unwritable"
echo "=================================================="
echo
echo "A listed property is one of two things:"
echo
echo "  - a field the API can read and nothing can write. This is the case the"
echo "    sweep exists for, and Tenant.LogoUrl was one for the platform's whole"
echo "    life: a response field, a wireframe control, and no command behind it."
echo "  - a value only EF materialises, assigned by nothing in code. Legitimate,"
echo "    and the reason this reports rather than fails."
