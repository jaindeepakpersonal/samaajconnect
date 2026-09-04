#!/usr/bin/env bash
#
# Lists commands and queries that carry free text and have no validator.
#
# Root CLAUDE.md §4.3 asks for one FluentValidation validator per command or
# query. `ValidationBehavior` runs the validators it can find, so a request with
# none has **no input validation at all** - not a lighter check, none.
#
# 61 of the platform's 139 requests have no validator, and most of those are
# right: a query with no parameters has nothing to validate. The ones worth
# looking at are the requests carrying a string, because that is the input that
# can be the wrong length, and a length is the thing a database refuses.
#
# This found `DecideChildConversionCommand`. It takes a free-text decision note,
# had no validator, and `DecisionNote` is capped at 1000 characters in the
# column - so a longer note reached Postgres, was refused with SQLSTATE 22001,
# and came back to an administrator as a 500 saying only that something had gone
# wrong. Verified by posting 1001 characters before the validator existed.
#
# **Reported, never failed.** Two of the three findings on the day this was
# written were legitimate: `WithdrawConsentCommand.Purpose` is parsed against an
# enum in its handler and answers 404 on anything else, and
# `ListIssuesQuery.Category` is a parameterised equality filter where an unknown
# value simply matches nothing. Neither is stored. A validator on either would
# be ceremony, and failing on them would make this a list people learn to skim.
#
# The rule worth remembering is narrower than "every command needs a validator":
# **free text that is persisted must be bounded before it reaches the database**,
# because the database's refusal is a 500 and a validator's is a 400.
#
# It needs no running stack: it reads the source.
set -euo pipefail

cd "$(dirname "$0")/.."

echo "== requests carrying free text with no validator =="
echo

# Every request type: a record implementing ICommand<> or IQuery<>. Same scan as
# scripts/security-invariants.sh, and for the same reason it skips comments -
# a declaration can span lines and prose mentions type names constantly.
read_requests() {
  local prog
  prog=$(cat <<'AWKEOF'
{ line[FNR] = $0; n = FNR }
END {
  for (i = 1; i <= n; i++) {
    if (line[i] !~ /(public|internal) +(sealed +)?record +[A-Za-z0-9_]+/) { continue }

    decl = line[i]
    for (j = i + 1; j <= n && j <= i + 12; j++) {
      decl = decl " " line[j]
      if (line[j] ~ /(\{|;) *$/) { break }
    }

    if (decl !~ /: *I(Command|Query)</) { continue }

    match(line[i], /record +[A-Za-z0-9_]+/)
    name = substr(line[i], RSTART + 7, RLENGTH - 7)

    # The parameter list, so a request carrying a string can be told from one
    # carrying only ids and booleans.
    printf "%s\t%s\n", name, decl
  }
}
AWKEOF
  )

  find services -name '*.cs' -path '*/src/*' ! -path '*/obj/*' ! -path '*/bin/*' \
    | sort \
    | while read -r file; do
        awk "$prog" "$file"
      done
}

total=0
texty=0
missing=0

while IFS=$'\t' read -r name declaration; do
  [ -z "$name" ] && continue

  total=$((total + 1))

  # Only the parameter list, not the response type: `IQuery<IReadOnlyList<string>>`
  # is not a request carrying free text.
  parameters=${declaration%%:*}

  case "$parameters" in
    *string*) ;;
    *) continue ;;
  esac

  texty=$((texty + 1))

  validators=$({ grep -rl "AbstractValidator<${name}>" services --include='*.cs' 2>/dev/null \
    | grep -v '/obj/' || true; } | wc -l)

  if [ "$validators" -eq 0 ]; then
    printf '  %s\n' "$name"
    missing=$((missing + 1))
  fi
done <<< "$(read_requests)"

if [ "$missing" -eq 0 ]; then
  echo "  (none)"
fi

echo
echo "=================================================="
echo "  request types:            $total"
echo "  carrying free text:       $texty"
echo "  of those, no validator:   $missing"
echo "=================================================="
echo
echo "A listed request is one of two things:"
echo
echo "  - free text that reaches the database unbounded. The column refuses it"
echo "    and the caller gets a 500 rather than a 400. This is the case the"
echo "    sweep exists for."
echo "  - a string the handler itself resolves - parsed against an enum, or used"
echo "    as a parameterised filter - and never stored. Legitimate, and why this"
echo "    reports rather than fails."
