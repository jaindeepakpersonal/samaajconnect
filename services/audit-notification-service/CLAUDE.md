# audit-notification-service

## Purpose

Cross-cutting consumer. Every state change anywhere on the platform ends up
here as an immutable audit row, and some of them also become a notification
for a member.

It is scaffolded early rather than last on purpose: every other service needs
somewhere to publish to from day one, and retrofitting audit logging once five
services exist means revisiting all five (`docs/product/ROADMAP.md`).

## Entities

| Entity | Status | Notes |
|---|---|---|
| `AuditLog` | built | Append-only. No mutating method, no update or delete endpoint. One de-identifying exception - see below. |
| `Notification` | built | `RecipientUserId == null` is a Samaaj-wide broadcast. |
| `NotificationTemplate` | not built | Needed when a real email/SMS channel lands. |

## Commands

| Command | Policy | Status |
|---|---|---|
| `RecordIntegrationEventCommand` | `[InternalRequest]` | built |
| `ErasePersonalDataCommand` | `[InternalRequest]` | built |
| `MarkNotificationReadCommand` | any authenticated role | not built |

`RecordIntegrationEventCommand` is raised by this service's own Kafka consumer
and is not mapped to any endpoint. It carries `[InternalRequest]` rather than
`[AllowAnonymousRequest]`: "anonymous" means a real caller reached us without a
token, this means there is no caller at all. Keeping them distinct leaves the
genuinely externally-reachable unauthenticated surface greppable on its own.

## Queries

| Query | Policy | Status |
|---|---|---|
| `ListAuditLogsQuery` | `SuperAdmin`/`SamaajAdmin` + `Audit.Read` | built |
| `GetMyNotificationsQuery` | any authenticated role | built |
| `GetMyDataQuery` | any authenticated role | built |

## Events published

None yet. `NotificationSent` belongs here once a delivery channel exists. The
Outbox tables and dispatcher are already wired so the first event raised cannot
be lost.

## Events consumed

**Every versioned platform topic**, by regex subscription
(`^[a-z0-9-]+[.][a-z0-9.-]+[.]v[0-9]+$`) rather than an explicit list. A new
service's events are therefore audited the day it ships. A topic this service
has never been taught about still produces a row, with the action derived from
the topic name — `boli.bid.placed.v1` becomes action `Placed` on entity `Bid`.
An audit trail with a hole in it is worse than one containing an event nobody
has described yet.

`identity.user.erased.v1` is the one topic this service acts on rather than
simply records; see "Erasure" below.

Topics with specific handling are listed in
`Application/IntegrationEvents/KnownEvents.cs`. Today that is the four
`identity.*` topics; `identity.user.registered.v1` is the only one that also
raises a notification.

## API endpoints

| Method | Path | Roles |
|---|---|---|
| GET | `/v1/audit/logs` | `SuperAdmin`, `SamaajAdmin` + `Audit.Read` |
| GET | `/v1/notifications` | any authenticated role |
| GET | `/v1/audit/me/data-export` | any authenticated role |
| GET | `/health` | anonymous |

## Decisions worth knowing before you change this service

**The dedupe checks bypass the tenant query filter; the read paths never do.**
A Kafka consumer has no request and so no resolved tenant. A filtered
"have I seen this message?" check would compare against `Guid.Empty`, match
nothing, and turn every redelivery into a duplicate row — the exact failure the
check exists to prevent. `IAuditLogRepository` and `INotificationRepository`
therefore call `IgnoreQueryFilters`, and `IAuditLogQueries` — everything
reachable over HTTP — deliberately does not. That split is why the read side is
a separate interface.

**Idempotency is a unique index, not just a pre-check.** `SourceMessageId` is
unique on both tables. The handler's check is the readable path; the index is
what holds when two consumer instances process the same partition during a
rebalance.

**A message that will not record is eventually committed and skipped.** After
`Consumer:MaxAttempts` tries the consumer logs the full payload at Critical and
moves on. Committing an unrecorded message loses it, which is bad; refusing to
commit stalls the partition and loses everything queued behind it, which is
worse. The Critical log is the recovery path — treat one as an incident.

**The member data export covers actions they took, not actions about them.**
An audit log is largely a record of administrators' work. Handing someone
else's actions to a member because they asked for "their data" would turn a
transparency right (DPDP s.11) into a surveillance tool. The payload is
omitted for the same reason: it is the state of whatever changed, which may be
someone else's data.

**Metadata refresh is 30s, not Confluent's five-minute default.** Kafka only
matches a regex subscription against topics it already knows about, so the
default would leave a five-minute hole in the trail every time a new service
first publishes.

**An unparseable payload is still audited.** The raw text goes into
`AfterState`; only the actor, entity id and any notification are skipped.

**Erasure is the one exception to append-only, and it is deliberately hard to
reach.** DPDP s.8(7) and s.12 require erasure; SECURITY-CHECKLIST.md requires
audit rows to be immutable "ever". The resolution
(`docs/product/DPDP-COMPLIANCE.md`): the fact that an action happened survives,
and the person disappears from it. Notifications are deleted outright - they
are messages to a person and nothing else. Audit rows keep their action,
entity, topic and both timestamps, and lose the actor, the actor role and the
payload, which is where a name usually is.

The whole capability lives in `ErasePersonalDataCommandHandler` and
`IErasureRepository`. There is no endpoint, the handler is reachable only from
the consumer and only for `identity.user.erased.v1`, and neither repository
method takes arbitrary criteria - so the exception cannot be reached by
accident, and if counsel decides audit rows must be deleted outright, those two
files are the whole change.

**The erasure itself is recorded, after the de-identifying pass.** A Samaaj has
to be able to show it honoured the request, and a row written before the update
would have been wiped by it. That row carries the tombstone id in `EntityId`
and no actor: the account it refers to no longer exists and no other row still
carries it, so it maps to nobody.

**The de-identify and delete bypass the tenant filter, like the dedupe checks.**
Same reason - a consumer has no request and so no tenant. `ExecuteUpdate` and
`ExecuteDelete` run as SQL and never load an entity, which is what lets them
reach properties whose setters are private on an aggregate that exposes no
mutating method at all. There is an integration test against a real Postgres,
because none of that is provable against a substituted repository.

**A before-state is recorded for corrections and status changes.**
SECURITY-CHECKLIST.md asks for it, and "something changed" is not an audit
trail if nobody can tell what it changed from. `EventDescriptor.BeforeProperties`
names which payload properties describe the prior state; they land in
`AuditLog.BeforeState`.

Named properties rather than the whole payload, because the payload is kept
verbatim forever. A previous status or set of module keys is safe to keep; a
member's previous mobile number is not. Where the before-state would be personal
data the event carries the *names* of the fields that changed instead of their
values - see `members.profile.updated.v1`.

## Dependencies

- **Postgres** `samaajconnect_audit_notification` — `ConnectionStrings__Default`
- **Kafka** — `Kafka__BootstrapServers`, consumer *and* producer
- **Jwt** — `Jwt__SigningKey`, validation only. This service never mints tokens.
- No Redis dependency.

## Testing

- `Sangam.AuditNotification.UnitTests` — the topic catalogue and the recording
  handler. No I/O.
- `Sangam.AuditNotification.IntegrationTests` — Testcontainers Postgres **and
  Testcontainers Kafka**. Nothing is faked, unlike the identity service's
  tests: the whole claim this service makes is "events published elsewhere end
  up in the audit log", and a fake broker would let that pass while the
  consumer loop, the header contract or the regex subscription were all broken.

```
dotnet test services/audit-notification-service/Sangam.AuditNotification.sln
```

Still missing: a test that curls this service **through the gateway**
(CLAUDE.md §9). The gateway does not exist yet; add it in the same change that
adds the YARP route.
