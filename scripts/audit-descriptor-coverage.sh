#!/usr/bin/env bash
#
# Every domain event topic on the platform has an entry in
# audit-notification-service's KnownEvents catalogue.
#
# KnownEvents.Describe's fallback for an undescribed topic is not a
# best-effort guess - it is EntityIdProperty: null, explicitly. Five cycles
# running found the same shape of gap behind that one line: a domain event
# whose own doc comment promised the audit trail would answer "who did this?"
# - GroupApplicationDecidedDomainEvent, IssueStatusChangedDomainEvent,
# PostModeratedDomainEvent, ParentalConsentWithdrawnDomainEvent,
# RoleMatrixChangedDomainEvent - while the derived default kept none of that
# promise, silently, because an audit row is written either way and nothing
# ever fails.
#
# Every one of the platform's 50 topics has a considered descriptor as of
# 2026-09-05 - not every one needed an actor, but every one got a real entity
# id and a real action name rather than the fully-generic derived default.
# This is what holds that: a new domain event ships with no descriptor and
# this fails on the day it lands, rather than being found by rereading
# KnownEvents.cs against ten services' worth of Domain folders by hand, which
# is what actually happened five times in a row.
#
# It needs no running stack: it reads the source.
set -euo pipefail

cd "$(dirname "$0")/.."

KNOWN_EVENTS=services/audit-notification-service/src/Sangam.AuditNotification.Application/IntegrationEvents/KnownEvents.cs

if [ ! -f "$KNOWN_EVENTS" ]; then
  echo "  FAIL  $KNOWN_EVENTS is missing - this check cannot read the descriptor catalogue"
  exit 1
fi

# Every "topic.name.v1" string declared as a Topic => "..." across every
# service's own Domain layer - the same shape IDomainEvent.Topic always takes,
# checked once here rather than per service.
declared=$(grep -rhoE '"[a-z][a-z0-9.-]+\.v[0-9]+"' services/*/src/*.Domain --include=*.cs \
  | tr -d '"' | sort -u)

# The dictionary keys in KnownEvents.cs - the topics this service has been
# taught about specifically, as opposed to falling through to the derived
# default.
known=$(grep -oE '"[a-z][a-z0-9.-]+\.v[0-9]+"\] = new\(' "$KNOWN_EVENTS" \
  | grep -oE '"[a-z][a-z0-9.-]+\.v[0-9]+"' | tr -d '"' | sort -u)

if [ -z "$declared" ]; then
  echo "  FAIL  read no topics out of services/*/Domain - this check's own pattern is broken"
  exit 1
fi

if [ -z "$known" ]; then
  echo "  FAIL  read no topics out of KnownEvents.cs - this check's own pattern is broken"
  exit 1
fi

echo "== every domain event topic has an audit descriptor =="
echo

missing=$(comm -23 <(printf '%s\n' "$declared") <(printf '%s\n' "$known"))

if [ -z "$missing" ]; then
  echo "  ok    all $(printf '%s\n' "$declared" | grep -c .) topics have a KnownEvents descriptor"
  echo
  echo "1 passed, 0 failed"
  exit 0
fi

echo "  FAIL  $(printf '%s\n' "$missing" | grep -c .) topic(s) fall through to the derived default:"
printf '%s\n' "$missing" | sed 's/^/          /'
echo
echo "        Add an entry to KnownEvents.cs naming at least a real Action and"
echo "        EntityIdProperty - an ActorIdProperty only where the event carries"
echo "        somebody distinct from its subject to name."
echo
echo "0 passed, 1 failed"
exit 1
