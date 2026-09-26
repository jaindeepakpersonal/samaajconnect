# events-service

## Purpose

Events a Samaaj or one of its volunteer groups is holding, and who is going.
Module-gated on `community`, the same key as the timeline and volunteer groups.

**Capacity and the waitlist are the substance of this service.** The
member-portal wireframe shows a "Full — Waitlist" pill and a "Join Waitlist"
button on a full event, and both are real states rather than labels.

## Entities

| Entity | Status | Notes |
|---|---|---|
| `SamaajEvent` | built | Tenant-scoped. Named this, not `Event`, because `event` is a C# keyword and `Event` beside `IDomainEvent` reads as the wrong thing. Carries an optional `EventTypeId` (a master-data-service `LookupItem`, `Category == EventType`) as of 2026-09-12 - see below. |
| `EventRegistration` | built | A place, or a place in the queue for one. |

## Commands

| Command | Policy | Status |
|---|---|---|
| `CreateEventCommand` | `Events.Publish` | built |
| `UpdateEventCommand` | `Events.Publish` | built |
| `PublishEventCommand` | `Events.Publish` | built |
| `CancelEventCommand` | `Events.Publish` | built |
| `RegisterForEventCommand` | `Members.Read` | built |
| `CancelRegistrationCommand` | `Members.Read` | built |

## Queries

| Query | Policy | Status |
|---|---|---|
| `ListEventsQuery` | `Members.Read` | built |
| `GetEventQuery` | `Members.Read` | built |
| `GetAttendeesQuery` | `Events.Publish` | built |

## Events published

| Event | Topic | Raised by |
|---|---|---|
| `EventPublishedDomainEvent` | `events.event.published.v1` | `SamaajEvent.Publish` |
| `EventRegistrationCreatedDomainEvent` | `events.registration.created.v1` | `SamaajEvent.Register` |
| `EventCapacityReachedDomainEvent` | `events.capacity.reached.v1` | `SamaajEvent.Register` |
| `EventWaitlistPromotedDomainEvent` | `events.waitlist.promoted.v1` | `SamaajEvent.CancelRegistration` |
| `EventCancelledDomainEvent` | `events.event.cancelled.v1` | `SamaajEvent.Cancel` |
| `EventUpdatedDomainEvent` | `events.event.updated.v1` | `SamaajEvent.Update` |

## Events consumed

None. A leaf service, like the other two Phase 2 contexts.

## API endpoints

| Method | Path | Roles |
|---|---|---|
| GET | `/v1/events` | `Members.Read` |
| POST | `/v1/events` | `Events.Publish` |
| GET | `/v1/events/{id}` | `Members.Read` |
| PATCH | `/v1/events/{id}` | `Events.Publish` |
| POST | `/v1/events/{id}/publish` | `Events.Publish` |
| POST | `/v1/events/{id}/cancel` | `Events.Publish` |
| GET | `/v1/events/{id}/attendees` | `Events.Publish` |
| POST | `/v1/events/{id}/registration` | `Members.Read` |
| DELETE | `/v1/events/{id}/registration` | `Members.Read` |
| GET | `/health` | anonymous |

## Authorization

Two permissions, and unlike volunteer-groups-service there is no third for
"organiser of *this* event". Whoever may publish an event may publish one, and
both holders of `Events.Publish` — Samaaj admins and volunteer group presidents
— are already administrators of something. The split there existed because a
group's president is an ordinary member; that does not apply here.

`Members.Read` covers seeing events and registering for them. Every member holds
it.

**An Area Admin holds `Events.Publish` for one locality.** `SamaajEvent.LocalityId`
is optional (null: the whole Samaaj) and set on create and edit, a `string` on
the wire like `EventTypeId`. An Area Admin (the `locality_id` token claim, read
into `IAreaScope`) sets events up only in their own locality; one elsewhere or
with none answers 404 to edit, publish, cancel and the attendee list, and its
drafts are invisible to them in the list and by id. Published events are not
touched: an Area Admin is a member of the whole Samaaj, and sees and registers
for any of them like anybody else.

## Decisions worth knowing before you change this service

**Creating and publishing are separate commands, because they are separate
decisions.** An event exists in somebody's head long before the Samaaj should be
told about it, and the admin wireframe lists Draft alongside Published for that
reason. Publishing raises the event; creating raises nothing.

**A draft answers "not found" to a member**, the same way an unapproved timeline
post does. A member reaching one has guessed its id, and confirming it exists is
the leak. A member who asks for drafts explicitly gets the published list rather
than a 403 — refusing would tell them drafts exist at all.

**RSVP and "join the waitlist" are one call.** From the member's side it is one
action; which they get depends on the room at that moment, not on which button
they pressed. Two endpoints would let somebody ask for a place on a full event
and be told no, which is a worse answer than being put in the queue.

**The response says where they stand.** "Waitlisted" alone tells a member very
little; `position` is the thing they actually want to know.

**Giving up a confirmed place promotes whoever has waited longest.** That
promotion is the entire reason a waitlist is worth having — without it the queue
is a list nobody ever comes off, which is worse than not offering one. Longest
wait first, because any other order needs explaining to the people it passes
over.

**A promoted member keeps their original queue position.** Refreshing the
timestamp on promotion would put them behind people who joined the waitlist
after them, which is the one thing a waitlist must not do. Somebody who
*cancelled* and comes back does get a fresh timestamp — they rejoin the back of
the queue, or cancelling would be free and the queue would never move.

**Capacity is nullable and zero is refused.** Null means no limit; zero would be
an event nobody can attend, which is a mistake rather than an intention. The
admin wireframe shows "94" with no denominator for exactly the unlimited case.

**Filling up is announced once, as the last place goes.** Not every time
somebody looks at a full event, and not again when a third person joins the
waitlist.

**Cancelling an event keeps its registrations and needs a reason.** People need
to be told, and an attendee list that vanished with the event is one nobody can
notify. "Cancelled" with no explanation is not an answer to somebody who
rearranged their day. A cancelled event cannot be republished: the people told
it was off will not be told again.

**A cancelled registration reads as no registration at all.** The screen asks
"am I going?", and somebody who cancelled is in the same position as somebody
who never registered.

**Cancelled events stay in the upcoming list until they would have happened.**
Somebody who planned around one should see that it is off, not find it simply
gone.

**Events carry ids and shape, never free text.** Title, description, venue and
the cancellation reason all stay out of the published events —
audit-notification-service stores payloads verbatim in an append-only table, and
those are the Samaaj's own copy.

**The attendee list needs `Events.Publish`.** Who else is going is a fact about
other people, and a Samaaj is a place where that matters.

**Domain-assigned keys are `ValueGeneratedNever`.** Both of them, as everywhere
else on this platform.

**`EventTypeId` is optional and, as of 2026-09-13, editable.** A Samaaj that
has not seeded any `EventType` lookup values yet can still create events; the
field is null until it does. It is a `string` on the wire, not a `Guid?` -
the same defense-in-depth matrimony-service's own opt-in command already established, so a
malformed id fails FluentValidation rather than crashing the JSON model
binder with an unhandled 400 before validation runs. Requires an additive
migration (`AddEventTypeId`, 2026-09-12) since, unlike `LookupItem.Category`,
this is a genuinely new column on an existing table, not a new value inside
an existing `HasConversion<string>()` one.

**Editing arrived a cycle after `EventTypeId`, and this file's own previous
sentence here said the opposite of what is now true.** It read "there is no
update command for an event at all... this one simply has nowhere to be
revised once set" - correct when written, and left standing after
`UpdateEventCommand` shipped. `Create`/`Update`/`Publish`/`Cancel` is the
whole lifecycle now. Worth noting as the reason to distrust a doc comment
that explains an absence: the absence is the part that goes stale first.

**`UpdateEventCommand` is allowed on a Draft or a Published event, refused
on a Cancelled one, and never touches the organiser.** There is nothing left
to correct once an event is called off - `Cancel` already keeps the reason
people were told, and a cancelled event cannot be republished either.
Reassigning `OrganizerType`/`OrganizerId` is a bigger structural change than
a correction (an event may already carry registrations against the
organiser who is answerable for it) and nothing asked for it, so those two
fields stay fixed at creation - the same narrow-scope choice
`CreateEventCommand`'s own field list already made.

**Dropping capacity below who is already registered is refused, not
silently applied.** `RegisteredCount` and `IsFull` are both computed from
the registrations table, so an organiser cutting capacity to fewer than the
people already holding a confirmed place would either need someone bumped
without being told, or would make the response's own numbers disagree with
each other. The handler checks this before calling `Update`, the same split
competition-service's own registration handler uses between a precondition
the caller needs a specific answer about and the aggregate method that
performs the write once it is known to be safe.

**`EventUpdatedDomainEvent` carries no field values.** Title, description
and venue are exactly the free text this service already keeps off every
other event's own topic; the fact that a correction happened, and when, is
what this one honestly records.

## Dependencies

- **Postgres** `samaajconnect_events` — `ConnectionStrings__Default`
- **Kafka** — `Kafka__BootstrapServers`, producer only
- **Jwt** — `Jwt__SigningKey`, validation only. This service never mints tokens.
- No Redis dependency.

## Testing

- `Sangam.Events.UnitTests` — the aggregate. Capacity, the waitlist and its
  promotion order are all pure decisions and belong here.
- `Sangam.Events.IntegrationTests` — Testcontainers Postgres, no Kafka. The
  tenant filter is applied by the DbContext, the unique index on
  `(event, member)` is what actually holds when two registrations race, and the
  outbox guarantee is transactional. None of the three survives a substituted
  repository.

```
dotnet test services/events-service/Sangam.Events.sln
```

`scripts/smoke-through-gateway.sh` covers the whole path through the gateway,
including a member being waitlisted on a full event and then promoted when the
place is given up.
