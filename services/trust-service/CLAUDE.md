# trust-service

## Purpose

Locality-wise Trust management: a Samaaj's local Trust, one per Locality, and
the roster of trustees who run it.

Behind the `trust` module key, **off by default**: a Samaaj switches it on
once it organises itself by locality Trusts.

**Built in three phases.** Add/update/delete trust members (phase 1);
elections and positions of trustees (phase 2); and the Trust's money
(phase 3, 2026-09-27) - donations, expenses, and a collections ledger that a
published Boli result in the Trust's locality opens against its winner. See
"The Trust's money" below. What is still open is whether Boli management
itself should be gated by a caller's Trust position.

## Entities

| Entity | Notes |
|---|---|
| `Trust` | Aggregate root. `TenantId`, `LocalityId`, `Name`, `Status` (Active/Inactive), `CreatedAt`. Exactly one per `(TenantId, LocalityId)` |
| `TrustMember` | Its own aggregate root, **not** owned by `Trust` — so a roster change raises its own event, the same reasoning panch-service's `PanelMember` gives for standing apart from its election. One member's seat: `MemberId`, `PositionTitle` (nullable free text), `Status` (Active/Removed), `AddedBy`/`AddedAt`, `RemovedBy`/`RemovedAt` |
| `TrustElection` | Aggregate root. An election for one named position on one Trust's roster — nominate, approve, vote, close, publish. Owns its `TrustElectionCandidate`s and **not** its votes. Copies volunteer-groups-service's `GroupElection` shape; see "Elections" below |
| `TrustElectionCandidate` | Owned by `TrustElection`. Nominated → Approved onto the ballot, or removed before voting opens |
| `Vote` | One Trust member's one vote in one election. Written directly, outside the aggregate — see "The guarantee" |
| `Donation` | Aggregate root. Pledged, then Collected with the Trust's next receipt number. A member or a named donor with an optional phone. Integer paise |
| `Expense` | Aggregate root. Recorded, then Approved. Category, description, amount, optional vendor |
| `Collection` | Aggregate root. An amount due to the Trust from a source (`BoliResult`, or a pledged `Donation`), owed by `PayerMemberId` - or `PayerName`, a donor with no account. Due, then Collected with a mode. One per source. `LastRemindedAt` holds the once-a-day reminder rule |

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
| `RecordDonationCommand` | `Trust.Manage` | A pledge: a member or a donor name, optional phone, amount, purpose, mode. No receipt number yet. Opens the pledge's Collection in the same transaction |
| `MarkDonationCollectedCommand` | `Trust.Manage` | Issues the Trust's next receipt number, and settles the pledge's Collection. 409 on one already collected |
| `RecordExpenseCommand` | `Trust.Manage` | Starts `Recorded` |
| `ApproveExpenseCommand` | `Trust.Manage` | Approving one already approved is success and changes nothing |
| `RecordCollectionPaymentCommand` | `Trust.Manage` | Records the mode it was paid by; for a pledge, also collects the donation and issues its receipt number. 409 once paid |
| `RemindPayerCommand` | `Trust.Manage` | An in-app reminder to the member who owes it. 409 once paid, for a donor with no account, and within a day of the last one |
| `ConsumeIntegrationEventCommand` | `[InternalRequest]` | Opens a `Due` Collection from `boli.result.published.v1` - see "The Trust's money" |

## Queries

| Query | Permission | Notes |
|---|---|---|
| `ListTrustsQuery` | `Members.Read` | This Samaaj's Trusts, newest first. Optionally narrowed to one locality |
| `GetTrustQuery` | `Members.Read` | One Trust |
| `ListTrustMembersQuery` | `Members.Read` | One Trust's roster, active members first |
| `ListTrustElectionsQuery` | `Members.Read` | This Trust's elections, newest first, with the caller's own vote on each |
| `GetTrustElectionQuery` | `Members.Read` | One election with its ballot and tally — never hidden |
| `ListDonationsQuery` | `Trust.Manage` | Newest first, optionally one status |
| `ListExpensesQuery` | `Trust.Manage` | Newest first, optionally one status |
| `ListCollectionsQuery` | `Trust.Manage` | Newest first, optionally one status, each with who owes it |
| `ListMyDuesQuery` | `Members.Read` | What the caller still owes to any Trust, oldest first. The payer is the signed-in member, never a parameter |

## Events published

- `TrustCreatedDomainEvent` — `trust.trust.created.v1`
- `TrustStatusChangedDomainEvent` — `trust.trust.status-changed.v1`
- `TrustMemberAddedDomainEvent` — `trust.member.added.v1` (also raised on reactivation)
- `TrustMemberUpdatedDomainEvent` — `trust.member.updated.v1` (also raised when publishing an election installs or clears a position)
- `TrustMemberRemovedDomainEvent` — `trust.member.removed.v1`
- `TrustElectionStatusChangedDomainEvent` — `trust.election.status-changed.v1`
- `TrustElectionResultsPublishedDomainEvent` — `trust.election.results-published.v1`
- `DonationRecordedDomainEvent` — `trust.donation.recorded.v1`; `DonationCollectedDomainEvent` — `trust.donation.collected.v1`
- `ExpenseRecordedDomainEvent` — `trust.expense.recorded.v1`; `ExpenseApprovedDomainEvent` — `trust.expense.approved.v1`
- `CollectionDueDomainEvent` — `trust.collection.due.v1` (ids, amount and payer); `CollectionSettledDomainEvent` — `trust.collection.settled.v1`
- `CollectionReminderSentDomainEvent` — `trust.collection.reminded.v1` (ids and source type, no amount), which audit-notification-service turns into the payer's notification

## Events consumed

`boli.result.published.v1`, by `IntegrationEventConsumer` on one explicit
topic, group `trust-service` (both in `ConsumerOptions`, not appsettings - the
rule pathshala-service's options explain).

## The Trust's money

Added 2026-09-27. Amounts are integer paise throughout.

**A pledge and a receipt are two steps.** A donation is recorded `Pledged`
with no receipt number; marking it collected issues the Trust's next number.
A number on a slip therefore always means money received.

**A published Boli result opens what its winner owes.** boli-service's
`boli.result.published.v1` carries the occasion's `LocalityId` (added for
this); `ConsumeIntegrationEventCommand` finds this Samaaj's Trust for that
locality, past the tenant filter since a consumer has no request, and opens a
`Due` Collection with the winner as `PayerMemberId`. A Samaaj-wide Boli, or a
locality with no Trust, opens nothing. Nothing on either screen creates a
Collection by hand - a typed figure would be a debt nobody incurred.

**The winner sees it.** `ListMyDuesQuery` answers the member portal's "What
you owe" card; a Trust officer records the payment and its mode in the admin
panel, which takes it off that card.

**A pledge is chased the same way (2026-09-27).** Recording a donation opens a
`Donation`-sourced Collection in the same transaction, owed by the donor's
account when they are a member, or carrying their name when they are not. The
two are one fact kept in two places, so each side settles the other: marking
the donation collected settles its Collection (by the pledged mode), and
recording the Collection's payment collects the donation and issues its
receipt number. `ChaseCollections` gave every pledge still outstanding its
Collection; `ChaseMigrationTests` runs it against legacy rows.

**A reminder is one notification, at most once a day.** `RemindPayerCommand`
raises `trust.collection.reminded.v1`, ids only; audit-notification-service
tells the payer a payment for "a Boli you won" or "your pledge" is still due,
and never the amount - that is on their own Trusts screen, not in an inbox.
A donor with no account cannot be reminded (there is nowhere to send it), and
the day rule lives on the aggregate (`Collection.ReminderInterval`) so a
double press cannot send two.

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

**A receipt number names exactly one donation.** Numbers run from 1 per
Trust. `IDonationRepository.ReserveNextReceiptNumberAsync` takes
`pg_advisory_xact_lock(namespace, trust key)` inside the request's own
transaction, then reads `MAX(receipt_number) + 1` past the tenant filter - the
sequence is everything this Trust has issued. The key is the Trust id's bytes
folded to 32 bits by hand, not `GetHashCode`, so every replica takes the same
lock. The filtered unique index on `(TrustId, ReceiptNumber)` is the backstop
for a reservation made outside a transaction, where the lock would be
released as soon as it was taken.

**One `Collection` per source, ever.** A unique index on
`(SourceType, SourceId)`. `ICollectionRepository.TryOpenAsync` inserts on its
own scope and catches `23505`, the `TryAddAsync` shape, which is what makes a
redelivered `boli.result.published.v1` a no-op rather than a second amount
due. The refused insert's `trust.collection.due.v1` outbox row is discarded
with it.

`TrustFinanceRepositoryTests` holds all three: ten concurrent collections
getting 1-10, the index refusing a duplicate number, and the Boli consumer
opening one Collection however often the event arrives.

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

- *Donations, expenses, and a payment-collection ledger* — built as planned
  (phase 3): `Donation`, `Expense` and `Collection` aggregates here, with
  `Collection` generalised over a `SourceType` rather than one ledger per
  source. Only `BoliResult` opens one today; `Donation` is there for pledges
  that should be chased the same way.
- *A locality-wise Boli* — done: `BoliOccasion` has a `LocalityId`, its
  result event carries it, and this service consumes it (see "The Trust's
  money"). Whether `Boli.Manage`/`Boli.PublishResults` should
  eventually be gated by a caller's Trust position, rather than staying
  Samaaj-wide, was deliberately deferred past this phase.

## Dependencies

- PostgreSQL — `samaajconnect_trust`
- Kafka via the Outbox (`OutboxDispatcher`), publishing; and one consumer
  (`IntegrationEventConsumer`) on `boli.result.published.v1`
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
