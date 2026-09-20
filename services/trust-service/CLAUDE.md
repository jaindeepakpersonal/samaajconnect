# trust-service

## Purpose

Locality-wise Trust management: a Samaaj's local Trust, one per Locality, and
the roster of trustees who run it.

Behind the `trust` module key, **off by default**: a Samaaj switches it on
once it organises itself by locality Trusts.

**This is phase 2 of a larger plan.** The full scope discussed for this
service is: add/update/delete trust members (phase 1, shipped); elections
and positions of trustees (phase 2, this one); donation management; expense
management; and payment collection against both donations and Boli (auction)
results — with Boli itself becoming locality-wise so a Trust's own officer
eventually runs its locality's auctions. Donations, expenses and collection
are not built yet. See "Decisions worth knowing before you change this
service" for what each later phase is expected to need and why it was
deliberately left out of this one.

## Entities

| Entity | Notes |
|---|---|
| `Trust` | Aggregate root. `TenantId`, `LocalityId`, `Name`, `Status` (Active/Inactive), `CreatedAt`. Exactly one per `(TenantId, LocalityId)` |
| `TrustMember` | Its own aggregate root, **not** owned by `Trust` — so a roster change raises its own event, the same reasoning panch-service's `PanelMember` gives for standing apart from its election. One member's seat: `MemberId`, `PositionTitle` (nullable free text), `Status` (Active/Removed), `AddedBy`/`AddedAt`, `RemovedBy`/`RemovedAt` |
| `TrustElection` | Aggregate root. An election for one named position on one Trust's roster — nominate, approve, vote, close, publish. Owns its `TrustElectionCandidate`s and **not** its votes. Copies volunteer-groups-service's `GroupElection` shape; see "Elections" below |
| `TrustElectionCandidate` | Owned by `TrustElection`. Nominated → Approved onto the ballot, or removed before voting opens |
| `Vote` | One Trust member's one vote in one election. Written directly, outside the aggregate — see "The guarantee" |

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
| `OpenTrustElectionCommand` | `Trust.Manage` | Sets an election up for a position. Starts `Draft` |
| `MoveTrustElectionCommand` | `Trust.Manage` | Strictly forward: `NominationsOpen`, `VotingOpen`, `Closed`. Refuses `VotingOpen` on an empty ballot |
| `NominateTrustCandidateCommand` | `Members.Read` + active member of this Trust | A repeat nomination is a no-op reported as success |
| `DecideTrustCandidateCommand` | `Trust.Manage` | Approve onto the ballot, or remove before voting opens |
| `CastTrustVoteCommand` | `Members.Read` + active member of this Trust | Self-votes refused. A repeat vote is reported as accepted:false |
| `PublishTrustElectionResultsCommand` | `Trust.Manage` | Only from `Closed`. Installs the winner's position via `TrustMember.UpdatePosition`, clearing the same title from any other active member who held it. Publishing twice is safe and does not reinstall |

## Queries

| Query | Permission | Notes |
|---|---|---|
| `ListTrustsQuery` | `Members.Read` | This Samaaj's Trusts, newest first. Optionally narrowed to one locality |
| `GetTrustQuery` | `Members.Read` | One Trust |
| `ListTrustMembersQuery` | `Members.Read` | One Trust's roster, active members first |
| `ListTrustElectionsQuery` | `Members.Read` | This Trust's elections, newest first, with the caller's own vote on each |
| `GetTrustElectionQuery` | `Members.Read` | One election with its ballot and tally — never hidden |

## Events published

- `TrustCreatedDomainEvent` — `trust.trust.created.v1`
- `TrustStatusChangedDomainEvent` — `trust.trust.status-changed.v1`
- `TrustMemberAddedDomainEvent` — `trust.member.added.v1` (also raised on reactivation)
- `TrustMemberUpdatedDomainEvent` — `trust.member.updated.v1` (also raised when publishing an election installs or clears a position)
- `TrustMemberRemovedDomainEvent` — `trust.member.removed.v1`
- `TrustElectionStatusChangedDomainEvent` — `trust.election.status-changed.v1`
- `TrustElectionResultsPublishedDomainEvent` — `trust.election.results-published.v1`

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

**At most one vote per `(ElectionId, VoterMemberId)`.** The unique index on
`trust_election_votes`, not `CastTrustVoteCommandHandler`'s own courtesy
check beforehand — the identical guarantee volunteer-groups-service's and
celebrity-voting-service's own elections already hold the same way, copied
rather than reinvented.

## Elections

Added in phase 2. Copies volunteer-groups-service's `GroupElection` shape
(nominate → approve → vote → publish) deliberately without sharing code with
it — this repo's own stated convention: different enough bounded contexts
that coupling them would cost more than the duplication saves.

**The electorate is this Trust's own active roster, not the whole Samaaj.**
Only a current `Active` `TrustMember` of the election's own Trust may be
nominated, may nominate, or may vote — `volunteer-groups-service`'s
"electorate is the group, not the Samaaj" rule, applied here.
`NominateTrustCandidateCommand` and `CastTrustVoteCommand` both check this
against the separate `TrustMember` aggregate, since the election does not
hold the Trust's roster itself.

**Publishing installs the winner, not merely announces them.**
`PublishTrustElectionResultsCommand` freezes the winner on `TrustElection`,
then calls the exact same `TrustMember.UpdatePosition` method
`UpdateTrustMemberCommand` already uses — so this raises the identical
`TrustMemberUpdatedDomainEvent` phase 1 shipped, rather than a second event
for the same fact. It also clears that title from any other active member of
the same Trust who held it: a position has one holder, the same reasoning
`VolunteerGroup.ElectPosition` gives for clearing a same-titled predecessor.
Publishing a second time changes nothing and does not reinstall the position
a second time — the frozen winner is the answer, not a fresh computation.

**There is no permission split between "manage" and "run your own
election"**, unlike volunteer-groups-service's `Manage`/`Lead` pair. A Trust
has no single instance-level leader the way a `VolunteerGroup` has a
president — its positions are plural (President, Secretary, Treasurer) — so
every admin-side election command (`OpenTrustElectionCommand`,
`MoveTrustElectionCommand`, `DecideTrustCandidateCommand`,
`PublishTrustElectionResultsCommand`) stays behind the same `Trust.Manage`
that governs the roster. Nominating and voting are open to every active
Trust member, exactly as in volunteer-groups-service.

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
  `TrustElectionEndpointsTests` walks nominate → approve → vote → close →
  publish end to end and asserts the winner's position actually lands on
  their `TrustMember` row, including clearing a same-titled predecessor and
  publishing twice being safe; `TrustVoteIndexTests` names the double-voting
  guarantee directly against `pg_indexes`, the same proof
  volunteer-groups-service's and celebrity-voting-service's own tests of the
  same name already run.
- `scripts/smoke-through-gateway.sh` covers the whole path through the
  gateway, including switching the `trust` module on and off and confirming
  the route answers 404 once it is off.

```
dotnet test services/trust-service/Sangam.Trust.sln
```
