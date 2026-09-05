#!/usr/bin/env bash
#
# Every CSS class a component's template uses resolves to a rule somewhere it
# can actually see - the app's own src/styles.css, or that component's own
# `styles:` block or styleUrl file.
#
# Nothing fails when a class resolves to nothing. The element renders
# unstyled and, because most of this platform's layout comes from
# `label`/`.input`/flex defaults rather than the missing rule, it mostly
# looks right - the same silent failure member-portal's own CLAUDE.md already
# named for `.muted`/`.visually-hidden` and a `.warn` pill that shipped in the
# wrong colour. Both apps' own notes said "check class names against
# styles.css before using them" and nothing held either app to it.
#
# Found on 2026-09-05: six classes across seven admin-portal screens matched
# nothing. `.input.inline` was pasted into four screens' own local styles
# with a drift already between two of the four copies (220px in three,
# 200px in the fourth) before becoming a fifth screen's unstyled class
# instead of a fifth copy - promoted to src/styles.css once, along with
# `.code` and `.confirm`, each of which existed in one screen's local styles
# while a second screen used it and defined nothing.
#
# It needs no running stack: it reads the source.
set -uo pipefail

cd "$(dirname "$0")/.."

pass=0
fail=0

check_app() {
  local app="$1"
  local global_css="apps/$app/src/styles.css"
  local global_classes
  global_classes=$(grep -oE '\.[a-zA-Z][a-zA-Z0-9_-]*' "$global_css" | sed 's/^\.//' | sort -u)

  local file used used_bind all_used styleurl dir ownfile own available cls

  for file in $(find "apps/$app/src/app" -name "*.ts" ! -name "*.spec.ts" | sort); do
    used=$(grep -oE 'class="[^"]*"' "$file" | sed 's/class="//;s/"$//' | tr ' ' '\n' \
      | grep -vE '^\{\{|^$' || true)
    used_bind=$(grep -oE '\[class\.[a-zA-Z0-9_-]+\]' "$file" | sed 's/\[class\.//;s/\]//' || true)
    all_used=$(printf '%s\n%s\n' "$used" "$used_bind" | grep -v '^$' | sort -u || true)

    [ -z "$all_used" ] && continue

    # A component's own styles live in an external file named by styleUrl, or
    # inline in its own `styles:` template literal - never both.
    styleurl=$(grep -oE "styleUrl:\s*'[^']*'" "$file" | sed "s/styleUrl:\s*'//;s/'$//" | head -1)

    if [ -n "$styleurl" ]; then
      dir=$(dirname "$file")
      ownfile="$dir/$(basename "$styleurl")"
      own=""
      [ -f "$ownfile" ] && own=$(grep -oE '\.[a-zA-Z][a-zA-Z0-9_-]*' "$ownfile" | sed 's/^\.//' | sort -u)
    else
      own=$(awk '/styles: `/{flag=1; next} /^  `,?$/{flag=0} flag' "$file" \
        | grep -oE '\.[a-zA-Z][a-zA-Z0-9_-]*' | sed 's/^\.//' | sort -u)
    fi

    available=$(printf '%s\n%s\n' "$global_classes" "$own" | sort -u)

    for cls in $all_used; do
      case "$cls" in *"{{"*|*"}}"*) continue ;; esac

      if ! grep -qxF "$cls" <<< "$available"; then
        echo "  FAIL  $app :: $file :: .$cls resolves nowhere"
        fail=$((fail + 1))
      fi
    done
  done
}

echo "== every class a template uses resolves to a rule it can see =="
echo

check_app member-portal
check_app admin-portal

if [ "$fail" -eq 0 ]; then
  echo "  ok    every class used in either app's templates is defined somewhere"
  pass=1
fi

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
