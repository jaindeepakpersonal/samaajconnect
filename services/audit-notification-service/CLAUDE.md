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
| `Notification` | built | `RecipientUserId == null` is a Samaaj-wide broadcast. Also the delivery record for anything leaving the platform - see "Outbound delivery". |
| `NotificationRead` | built | One member having read one message. A row rather than a column, because a broadcast is one notification a whole Samaaj shares - see "Read state". |
| `NotificationTemplate` | not built | Titles and bodies are still written inline in `KnownEvents`. Needed once there is more than one message and somebody wants to change the wording without a deploy. |

## Commands

| Command | Policy | Status |
|---|---|---|
| `RecordIntegrationEventCommand` | `[InternalRequest]` | built |
| `ErasePersonalDataCommand` | `[InternalRequest]` | built |
| `MarkNotificationReadCommand` | any authenticated role | built |
| `MarkAllNotificationsReadCommand` | any authenticated role | built |
| `BroadcastNotificationCommand` | `SuperAdmin`/`SamaajAdmin` + `Notifications.Broadcast` | built |

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
| `ListBroadcastsQuery` | `SuperAdmin`/`SamaajAdmin` + `Notifications.Broadcast` | built |

## Events published

`notifications.broadcast.sent.v1` — and this service publishes it **to itself**.
The consumer subscribes to every versioned topic, so an announcement goes out
through the outbox and comes back as an audit row with the administrator who
sent it on it. Without that, one person putting a message in front of an entire
Samaaj would have left nothing but a log line.

The event carries the title and not the body. Audit payloads are kept verbatim
and forever; the body is up to 2000 characters that are already stored, once, on
the notification the event names.

`NotificationSent` is still not raised. Nothing on the platform would consume
one, and an event with no consumer is a topic to keep in step for no reason.

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
`Application/IntegrationEvents/KnownEvents.cs`. `identity.user.registered.v1` is
still the only one that raises a notification, and the only one that carries a
contact address to send it to - most events name a member by id and nothing
else, deliberately, because a payload holding a mobile number is a payload that
later has to be redacted.

**`identity.session.revoked.v1` had a descriptor added on 2026-09-04, and
identity-tenant-service's own docs explain why it mattered more than most.** A
run of `ReuseDetected` on one account is described there as "the closest thing
this platform has to an intrusion signal - treat it as an incident", and until
that date the event was never raised at all, so this service had never once
recorded one. Now that it is, the derived default would have given the audit
row no actor and no entity id - exactly the row an administrator reads to
answer "who did this?", left blank for precisely the event that matters most.
The descriptor names the session as the entity and the account as the actor.

**`identity.user.status-changed.v1` followed the day after, for the same
reason.** Suspending or reinstating an account is done *to* it, by an
administrator, so the derived default's blank actor would have been wrong in
the same way it was for the session-revocation event: the row that answers
"who did this?" is exactly the one this event exists to produce. The payload's
own field is `changedByUserId` rather than `userId`, to say plainly that it
names the actor and not the account being acted on, and the descriptor points
there rather than at the account.

## API endpoints

| Method | Path | Roles |
|---|---|---|
| GET | `/v1/audit/logs` | `SuperAdmin`, `SamaajAdmin` + `Audit.Read` |
| GET | `/v1/notifications` | any authenticated role |
| POST | `/v1/notifications/{id}/read` | any authenticated role |
| POST | `/v1/notifications/read-all` | any authenticated role |
| POST | `/v1/notifications/broadcast` | `SuperAdmin`, `SamaajAdmin` + `Notifications.Broadcast` |
| GET | `/v1/notifications/broadcasts` | `SuperAdmin`, `SamaajAdmin` + `Notifications.Broadcast` |
| GET | `/v1/audit/me/data-export` | any authenticated role |
| GET | `/health` | anonymous |

## Read state

**Read-ness is a fact about a person and a message, and it lives in
`notification_reads`.** It used to be `Notification.ReadAt` plus a `Read` value
in `NotificationStatus`, and that shape could not express a broadcast: a
Samaaj-wide announcement is one row with no recipient, shared by everybody, so
the first member to open it would have marked it read for all of them.

Direct notifications could have kept the column while broadcasts gained a table.
They did not, because two mechanisms for one idea is how the second gets
forgotten - by the erasure path, by the unread count, by whoever adds the next
channel. One table holds both, and `Notification` has no read state at all.

**`NotificationStatus` is now purely about delivery.** `Read` is gone from it,
and the number 4 is left as a gap rather than reused. The enum was two state
machines wearing one name.

**Marking read is an insert-or-nothing, not a check then a write.** Opening the
same notification twice at once is ordinary - two tabs, a double tap, a client
retrying - and a check followed by an insert lets both attempts past the check,
leaving the unique index on `(notification_id, user_id)` to turn the loser into
a 500. `TryRecordReadAsync` is one `INSERT ... ON CONFLICT DO NOTHING`, and the
caller is told it was already read, which is what happened.

**The two checks on marking read are both load-bearing.** The notification must
be in the caller's Samaaj *and* addressed to them or to everybody. A tenant check
alone lets a member mark another member's notification read; an addressed-to
check alone lets them reach into another Samaaj's broadcast, because a broadcast
has no recipient and so passes "is it mine" for everyone. Both refuse with 404,
so neither confirms the id exists.

**Erasure deletes read rows explicitly, not only by cascade.** Deleting a
member's own notifications cascades to their read rows for those. A broadcast
belongs to the Samaaj and survives - and the row saying this person opened it on
Tuesday would survive with it, which is a record of one member's behaviour and
exactly what erasure is for.

**Mark-all-as-read chooses its set in SQL.** Reading a page of notifications and
writing rows for those would leave a member with more notifications than the
page holds still unread after pressing the button that says otherwise. It also
passes the tenant explicitly: raw SQL does not get the global query filter, and
a statement that did not name the Samaaj would mark every Samaaj's broadcasts
read at once.

## Broadcasts

**One row addressed to nobody, not one row per member.** A Samaaj of two
thousand people would otherwise mean two thousand copies of the same sentence,
and erasing one member would have to find theirs among them.

**This Samaaj, and never all of them.** The handler calls
`ITenantContext.RequireTenantId()`, so a Super Admin who has not chosen a Samaaj
cannot broadcast to every Samaaj on the platform. The admin wireframe's Audience
dropdown offers exactly that under "All Members", along with "Specific Role";
neither is built. A write that deliberately crosses tenants is not something any
other part of this platform does, and it should not arrive as a side effect of a
dropdown; role membership lives in identity-tenant-service.

**In-app only, and not for want of a channel.** There is a delivery channel now,
but this service holds no directory of member addresses - it learns one from an
event that happens to carry it. There is no set of addresses here to send a
Samaaj-wide message to, which is the same missing piece as the DPDP s.8(6) duty
to reach every affected person.

**Nothing stops the same announcement twice, deliberately.** Two identical
messages an hour apart are two messages, and any rule that guessed otherwise -
same title within five minutes, say - would eventually swallow one somebody
meant to send. `ListBroadcastsQuery` is the answer instead: the admin screen
shows what has already gone out, which makes a duplicate visible before it is
sent rather than after.

**`Notifications.Broadcast` is its own permission key.** Managing members and
putting a message in front of every one of them are different powers, and the
role matrix lets a Samaaj hand out the second without the first.

## Outbound delivery

In-app notifications are delivered by being written: the row *is* the message.
Anything else has to leave the platform, and that is what this part does.

```
event → RecordIntegrationEventCommandHandler → Notification(Pending, destination)
      → NotificationDispatcher (polls, claims)
      → INotificationChannel adapter
      → Sent / Pending again / Failed
```

**There is no provider. `LoggingNotificationChannel` writes the message to the
log and reports success.** Everything above the transport is real — deciding a
message is due, addressing it, queueing, retrying, giving up — and the last step
is a log line. Replacing it is one class implementing `INotificationChannel` and
one registration in `Infrastructure/DependencyInjection.cs`.

The cost of that stand-in is stated where it matters: a notification marked
`Sent` today means "handed to the channel", not "reached a person". The
dispatcher says so at Warning on every start, naming the channels involved.

**A message that leaves the platform is a second row, not a flag.** One event
raises an in-app notification and, if it carried a contact address, an outbound
copy. They are genuinely different — one is delivered by existing, the other has
a destination, attempts and failures — and the unique index on
`(source_message_id, channel)` is what keeps a redelivery from duplicating
either. That index replaced one on `source_message_id` alone, which is why the
dedupe check now takes a channel.

**`GET /v1/notifications` returns in-app rows only. The data export does not.**
The notification list is the member's message list, and without the filter every
message the platform also emailed would appear in the portal twice. The DPDP
s.11 export is a different question — everything this service holds about them —
so it uses `ListEveryChannelForRecipientAsync` and includes the destination.

Two methods rather than one with a flag, because they were briefly one: adding
the in-app filter to the shared method silently narrowed the export in the same
edit, and nothing failed. `The_member_sees_one_message_and_the_export_sees_both_copies`
is what would now catch it.

**The claim is the one write that skips the aggregate, and atomicity is why.**
`NotificationRepository.ClaimPendingAsync` marks a batch `Sending` in a single
conditional `UPDATE`. Two dispatchers that each read a Pending row and then
write it have both sent the message; a member gets two texts. Publishing a Kafka
event twice is free because consumers are idempotent — there is no idempotency
on the far side of a phone, which is why this dispatcher is stricter than
`OutboxDispatcher` about the same-looking problem.

That claim was verified by breaking it: replacing the statement with a select
followed by an update fails
`NotificationDeliveryTests.Two_dispatch_passes_at_once_never_send_the_same_message_twice`.
Removing only `FOR UPDATE SKIP LOCKED` does **not** fail it — so that clause is
throughput (a second dispatcher proceeds instead of blocking), and the atomicity
of the one statement is the correctness.

**The attempt is counted at claim time, not on failure.** A process that dies
mid-send has already spent its attempt. Counting on failure would let a message
that reliably kills the sender be retried forever, because a crash records
nothing.

**A row abandoned in `Sending` is returned to the queue after a timeout, and it
may already have been sent.** That is the honest reading of a crash between the
provider accepting a message and this recording it, and the requeue writes a
failure reason saying so. Delivery is at-least-once, like everything else here.
`StalledAfterMinutes` must stay comfortably longer than a real send, or a slow
provider is asked to deliver the same message twice.

**Which channel a message goes on is derived from the address.** The platform
stores one `MobileOrEmail` per login rather than separate fields, so
`ContactAddress.ChannelFor` decides from the shape of the string. It refuses
anything ambiguous, and an unclassifiable address raises no outbound copy at all
rather than a guess sent to the wrong place — the member still gets the in-app
notification, and the event is not refused, because refusing it would stall the
partition behind one malformed identifier.

**The welcome message is not contact verification.** `identity.user.registered.v1`
now sends a welcome to the identifier the member registered with. Nothing checks
that it arrived or that whoever reads it is the person who registered, so
`User.IsContactVerified` stays false. OTP is still unbuilt; this is a channel it
could use, not the thing itself.

**By default the log gets a redacted address and no body.** A notification is
addressed to one person and written for them, so logging both puts personal data
where erasure cannot reach. `NotificationDelivery:Logging:RevealContent` turns
that off, is on in `docker-compose.yml` because that stack is local development,
and announces itself at Warning on every start.

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
- **A notification provider** — none. `NotificationDelivery__*` configures the
  dispatcher; the channels behind it are the logging stand-in. Local messages
  come out of `docker compose logs -f audit-notification-service`.
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

**The dispatcher does not run on its own in the integration tests**
(`NotificationDelivery:Enabled` is false in the factory). Tests call
`DispatchBatchAsync` when they want a pass — otherwise a background loop claims
a seeded Pending row out from under the assertion about it, and the test that
"fails" is the honest one. `NotificationDispatcher` is registered as a singleton
and *then* handed to the host so a test can resolve the same instance.

Delivery is deliberately split across the two projects, and the split is not
arbitrary: the state machine (`MarkDelivered`, `RecordDeliveryFailure`,
`ReleaseStalledClaim`) is unit-testable and unit-tested, while the attempt
counter it reads is incremented by SQL — so the attempt limit, the claim and the
stall timeout are only provable against a real Postgres.

```
dotnet test services/audit-notification-service/Sangam.AuditNotification.sln
```

Through the gateway (CLAUDE.md §9): `scripts/smoke-through-gateway.sh` calls
`GET /v1/notifications` on 8080 with a member token, so the YARP route is
exercised rather than only the service.
