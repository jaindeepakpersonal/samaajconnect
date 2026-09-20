# trust-service

## Purpose

Locality-wise Trust management: a Samaaj's local Trust, one per Locality, and
the roster of trustees who run it.

Behind the `trust` module key, **off by default**: a Samaaj switches it on
once it organises itself by locality Trusts.

**This is phase 1 of a larger plan.** The full scope discussed for this
service is: add/update/delete trust members; elections and positions of
trustees; donation management; expense management; and payment collection
against both donations and Boli (auction) results — with Boli itself
becoming locality-wise so a Trust's own officer eventually runs its
locality's auctions. None of that beyond the roster is built yet. See
"Decisions worth knowing before you change this service" for what each later
phase is expected to need and why it was deliberately left out of this one.

## Entities

| Entity | Notes |
|---|---|
| `Trust` | Aggregate root. `TenantId`, `LocalityId`, `Name`, `Status` (Active/Inactive), `CreatedAt`. Exactly one per `(TenantId, LocalityId)` |
| `TrustMember` | Its own aggregate root, **not** owned by `Trust` — so a roster change raises its own event, the same reasoning panch-service's `PanelMember` gives for standing apart from its election. One member's seat: `MemberId`, `PositionTitle` (nullable free text), `Status` (Active/Removed), `AddedBy`/`AddedAt`, `RemovedBy`/`RemovedAt` |

`LocalityId` is a loose reference into master-data-service's `Locality`, with
no referential integrity across the service boundary — the same convention
`Business.LocalityId` and `BiodataProfile.LocalityId` already use.

## Commands

| Command | Permission | Notes |
|---|---|---|
| `CreateTrustCommand` | `Trust.Manage` | Refuses a duplicate `(TenantId, LocalityId)` — a 409, backed by a real unique index, not just the handler's own read |
| `ChangeTrustStatusCommand` | `Trust.Manage` | Active/Inactive. Setting the status it already has is success and changes nothing |
| `AddTrustMemberCommand` | `Trust.Manage` | Adding somebody already active is a no-op reported as success. Adding somebody previously removed reactivates their old row rather than inserting a second one |
| `UpdateTrustMemberCommand` | `Trust.Manage` | Changes an active trustee's position title. 409 on a removed member |
| `RemoveTrustMemberCommand` | `Trust.Manage` | Soft: sets `Status = Removed`. Removing somebody already removed is a no-op reported as success |

## Queries

| Query | Permission | Notes |
|---|---|---|
| `ListTrustsQuery` | `Members.Read` | This Samaaj's Trusts, newest first. Optionally narrowed to one locality |
| `GetTrustQuery` | `Members.Read` | One Trust |
| `ListTrustMembersQuery` | `Members.Read` | One Trust's roster, active members first |

## Events published

- `TrustCreatedDomainEvent` — `trust.trust.created.v1`
- `TrustStatusChangedDomainEvent` — `trust.trust.status-changed.v1`
- `TrustMemberAddedDomainEvent` — `trust.member.added.v1` (also raised on reactivation)
- `TrustMemberUpdatedDomainEvent` — `trust.member.updated.v1`
- `TrustMemberRemovedDomainEvent` — `trust.member.removed.v1`

## Events consumed

None. A leaf service, like volunteer-groups-service and timeline-service.

## API endpoints

See `docs/product/API-CONTRACTS.md`.

## The guarantee

**A Samaaj has exactly one Trust per locality.** The unique index on
`(TenantId, LocalityId)` is what holds this, not `CreateTrustCommandHandler`'s
own "does one already exist" read — two concurrent requests both pass that
read. `ITrustRepository.TryAddAsync` inserts on its own scope and catches the
Postgres unique-violation SQLSTATE (`23505`), returning false rather than
throwing, the same shape `boli-service`'s bid index and `panch-service`'s
ballot index both use. `TrustEndpointsTests` proves the index directly against
`pg_indexes`.

**At most one `TrustMember` row per `(TrustId, MemberId)` ever.** Also a real
unique index, not a convention. `AddTrustMemberCommandHandler` reads any
existing row for the pair first and either creates, reactivates, or no-ops —
but the index is what would refuse a genuine race between two adds, not that
read.

## Decisions worth knowing before you change this service

**Membership is admin-curated, not derived from a member's own locality.**
`MemberProfile.Locality` (member-family-service) says where somebody lives;
it says nothing about who administers the Trust there. Confirmed with the
user rather than assumed: an admin explicitly adds and removes trustees
through `Trust.Manage`, exactly matching the "add/update/delete trust
members" ask, rather than a Trust's roster silently following wherever its
members happen to live.

**Exactly one Trust per locality, not several.** Also confirmed with the
user. A Samaaj that wants more than one Trust body per area is out of scope
for this design; `CreateTrustCommand` enforces the single-Trust rule as a
real database guarantee rather than a soft default that a later feature
would have to work around.

**`PositionTitle` is free text and is not a platform role**, the identical
stance `volunteer-groups-service`'s `RolePosition` takes and for the same
reason: what somebody is called on a Trust grants nothing anywhere and
should not need a deployment to add. It is set by hand in this phase because
there is no election yet.

**Reactivating a removed member is a deliberate design choice, not an
oversight.** A plain unique index on `(TrustId, MemberId)` would make
`AddTrustMemberCommand` permanently refuse a member who had ever been
removed and later rejoined. `TrustMember.Reactivate` clears the removal and
raises `trust.member.added.v1` again on the *same* row, so the roster's
history — who was ever a trustee here — stays truthful without the schema
needing a second table for "current roster" versus "history".

**What the later phases will need, so this phase doesn't foreclose them:**

- *Elections for trustee positions* — the plan is to copy
  `volunteer-groups-service`'s `GroupElection` shape (nominate → approve →
  vote → publish), scoped to one Trust's own roster as the electorate, the
  same "copy the template, don't couple the services" choice this repo
  already made for `GroupElection` itself relative to
  `celebrity-voting-service`'s `VotingCampaign`. Publishing an election is
  expected to call `TrustMember`'s position-setting path the way
  `VolunteerGroup.ElectPosition` does today, not introduce a second way to
  hold a position.
- *Donations, expenses, and a payment-collection ledger* — expected to be new
  aggregates in this same service (`Donation`, `Expense`, `Collection`), with
  `Collection` generalised over a `SourceType` (`Donation` vs `BoliResult`)
  rather than duplicating collection logic per source.
- *A locality-wise Boli* — a separate, later change to `boli-service` itself
  (adding `LocalityId` to `BoliOccasion`), deliberately decoupled from this
  service for now so neither build blocks the other. The intended integration
  point once both exist is this service consuming `boli.result.published.v1`
  to open a `Due` `Collection` row against the winning member — see that
  event's own descriptor in audit-notification-service's `KnownEvents.cs` for
  what it already carries. Whether `Boli.Manage`/`Boli.PublishResults` should
  eventually be gated by a caller's Trust position, rather than staying
  Samaaj-wide, was deliberately deferred past this phase.

## Dependencies

- PostgreSQL — `samaajconnect_trust`
- Kafka via the Outbox (`OutboxDispatcher`), publishing only
- No Redis dependency

## Testing

- `Sangam.Trust.UnitTests` — the aggregate rules: duplicate-locality
  refusal at the domain layer, reactivate-on-re-add, the no-op on
  re-adding an already-active member, and refusing an update on a removed
  member.
- `Sangam.Trust.IntegrationTests` — Testcontainers against a real Postgres.
  `TrustEndpointsTests` proves both unique indexes against `pg_indexes`,
  tenant isolation, outbox rows landing in the same transaction as the state
  change, and an end-to-end walk: create Trust → add member → update
  position → remove → list, including the reactivation and no-op paths
  through the real endpoints rather than only at the domain layer.
- `scripts/smoke-through-gateway.sh` covers the whole path through the
  gateway, including switching the `trust` module on and off and confirming
  the route answers 404 once it is off.

```
dotnet test services/trust-service/Sangam.Trust.sln
```
