# boli-service

## Purpose

Runs a Samaaj's Boli — the auctions held at Paryushan, a temple
anniversary, or a fundraiser. A manager announces an occasion, defines
the types of Boli it offers, opens each one for bidding, closes it,
records who won, and announces it.

Behind the `boli` module key.

Bidding is a contended write path with a real correctness requirement,
and unlike celebrity voting the requirement is not "one each" but "one
highest". Read "The guarantee" before changing anything under `Bid`.

## Entities

| Entity | Notes |
|---|---|
| `BoliOccasion` | Aggregate root. Title, date, status, and where it is held: `LocalityId`, `CityId` (both nullable) and `Venue` (free text, up to 300). Owns its `BoliType`s and **not** its Boli |
| `BoliType` | A label a Samaaj reuses — "Mangal Deep", "Swapna". Nobody bids on a type |
| `Boli` | Aggregate root. One item being bid for: window, floor, increment, status. Owns **no** bids |
| `Bid` | One offer. Written directly, outside the aggregate. Never amended or deleted |
| `BoliResult` | Who won and for how much. Recorded first, published second |

## Commands

| Command | Permission | Notes |
|---|---|---|
| `CreateOccasionCommand` | `Boli.Manage` | Starts `Upcoming`. Takes an optional `LocalityId`, `CityId` and `Venue` |
| `UpdateOccasionCommand` | `Boli.Manage` | Corrects title, description, date, locality, city and venue until the occasion closes; 409 after, when results have been announced against it |
| `DefineBoliTypeCommand` | `Boli.Manage` | One name per occasion, case-insensitively |
| `MoveOccasionCommand` | `Boli.Manage` | Upcoming → Active → Closed. Never backwards |
| `OpenBoliCommand` | `Boli.Manage` | Creates it `Scheduled` and starts it. Takes the anti-sniping window |
| `PlaceBidCommand` | `Members.Read` | Under a row lock. Being outbid is success with `accepted: false` |
| `CloseBoliCommand` | `Boli.Manage` | Idempotent. Also takes the lock — closing races the last bids |
| `RecordResultCommand` | `Boli.Manage` | From the highest bid. The winner is not a parameter |
| `PublishResultCommand` | `Boli.PublishResults` | Idempotent, and irreversible through this API |

## Queries

| Query | Permission | Notes |
|---|---|---|
| `ListOccasionsQuery` | `Members.Read` | Newest first. Optional `?localityId=` and `?cityId=` narrow to one place |
| `GetOccasionQuery` | `Members.Read` | Types and the Boli under it, each with its live highest |
| `GetActiveBoliQuery` | `Members.Read` | Only those actually taking bids — status *and* clock. Optional `?localityId=` and `?cityId=` narrow to one place's occasions. Each Boli carries its occasion's title and place |
| `GetBoliQuery` | `Members.Read` | One Boli, its highest, and the minimum next bid |
| `GetBidHistoryQuery` | `Members.Read` | Amounts and times. Never who bid |
| `GetBidLedgerQuery` | `Boli.Manage` | Every bid with who placed it and when, highest first - the organiser's record. See below |
| `GetBoliResultQuery` | `Members.Read` | 404 until recorded; no winner named until published |
| `GetPublishedResultsQuery` | `Members.Read` | Everything announced, newest first, each with its occasion and place, and the winner by member id |
| `ListPendingResultsQuery` | `Boli.PublishResults` | Recorded and not yet announced, oldest first |

## Events published

- `OccasionClosedDomainEvent` — `boli.occasion.closed.v1`
- `OccasionUpdatedDomainEvent` — `boli.occasion.updated.v1` (ids and who)
- `BoliClosedDomainEvent` — `boli.closed.v1`
- `BoliResultPublishedDomainEvent` — `boli.result.published.v1`. Carries the occasion's `LocalityId` (null for a Samaaj-wide occasion), added so trust-service can open what the winner owes against that locality's Trust; additive, so the topic stays v1
- `BoliExtendedDomainEvent` — `boli.extended.v1`, only when a closing bid moved the window

## Events consumed

None. A bid carries a member id and nothing else about the member, so
there is no copy of another service's data here to keep in step.

## API endpoints

See `docs/product/API-CONTRACTS.md`.

**The paths really do read `/v1/boli/boli/{id}`.** The gateway routes
`/v1/boli/**` to this service, and the resource under it is a Boli. It
looks like a typo and is not one — it is the same shape as
`/v1/pathshala/pathshalas`, which reads better only because "pathshala"
has a plural and "Boli" does not. Do not "fix" it without changing
`API-CONTRACTS.md` and the member portal together.

## The guarantee

**A Boli has exactly one highest bid.** Two things hold that, and both
are needed.

**The row lock.** `IBoliRepository.LockForBiddingAsync` takes
`SELECT ... FOR UPDATE` on the Boli row inside the request's
transaction, which `TransactionBehavior` has already opened. Placing a
bid is a check-then-insert — read the highest, decide whether the new
amount clears it, write — and two bidders who read before either writes
both pass the check. In the last minute of a Boli that is the normal
case, not the edge. The lock serialises them.

That is deliberately the *opposite* of what
`celebrity-voting-service` does, where the vote is pushed onto its own
scope precisely to avoid serialising voters. Here serialisation is the
point: two people cannot both be the highest bidder, and that is the
domain rather than a limitation. Bids on different Boli do not contend
at all, which is what keeps it cheap when a Samaaj runs twenty at once.

**The unique index on `(BoliId, Amount)`.** A lock is a convention. A
future code path can forget to take it, and an in-process lock would
stop working the moment this ran on two instances. Two bids at one
amount would leave "the highest bid" with no single referent and the
winner decided by whichever row sorted first. The database refusing the
second is what makes that impossible rather than unlikely.

`BidIndexTests` asserts the index against `pg_indexes` and proves a
second insert is refused with SQLSTATE 23505.
`ConcurrentBiddingTests` proves the behaviour when twenty real requests
race. Both are needed: the second would also pass if a handler happened
to serialise its callers, which would stop being true the moment the
shape of the code changed.

**Being outbid is success, not failure.** `accepted: false`, with the
current highest and the amount they now need. Somebody outbid while
their form was open has done nothing wrong, and a 409 would be telling
them off for being slow.

## Decisions worth knowing before you change this service

**`LocalityId` is a tag/filter only in this phase, deliberately.** A
Samaaj's Boli can now be told apart by locality — `BoliOccasion.LocalityId`
is a loose reference into master-data-service's `Locality`, the same
no-referential-integrity convention `Trust.LocalityId` and
`Business.LocalityId` already use — but `Boli.Manage`/`Boli.PublishResults`
stay Samaaj-wide permissions, unchanged. A manager who can run one locality's
Boli can still run every locality's. Gating that by a caller's position on
`trust-service`'s own Trust for the locality is the intended next step, once
that service's roster is something this service can check against, and was
deliberately deferred so neither build blocked the other. `LocalityId` is
nullable and additive: every occasion that existed before this field, and
every one a Samaaj chooses to create without picking a locality, keeps
running exactly as before — a Samaaj too small to split by locality has no
reason to ever set it.

**Who bid is the organiser's to see, and nobody else's (2026-10-24).**
`GetBidLedgerQuery` (`GET /v1/boli/boli/{id}/bids/ledger`) is the bid history
with the member id on every row, behind `Boli.Manage` - a Samaaj has to be able
to answer "who bid, and when" when a result is questioned, and the admin panel
shows it in each Boli's popup with names resolved there. `GetBidHistoryQuery`
is unchanged: a member still sees amounts, times and which bids are theirs,
because naming rivals while bidding is open is exactly what the record-then-
publish split exists to prevent. The winner, once announced, is named to every
member - `BoliResultResponse.WinningMemberId` was already that - and both
portals now show the name.

**Where an occasion is held is three loose fields (2026-10-24).** `Venue` is the
manager's own words ("Shri Mahavir Mandir"); `CityId` and `LocalityId` are
master-data ids, with no referential integrity, the same as `LocalityId` above.
A Boli takes its occasion's place - it has none of its own - so `BoliResponse`
and `BoliResultResponse` copy it from the occasion when they are built, and
`?cityId=` on the occasion list and the active list filters on the occasion.
All three are nullable and additive, so every occasion before them reads as held
"in the whole Samaaj", which is what it was.

**A Boli should not be won by arriving last.** Without an auto-extend
window, whoever bids a second before the close takes it — not because
they valued it more but because nobody had time to answer, which is the
opposite of what an auction is for. `AutoExtendSeconds` is that window,
per Boli, chosen by the Samaaj, and 0 (off) is the default so nothing
that existed before it behaves differently.

**The extension is measured from the bid, not from the old close**, and
this is the whole design rather than a detail. Adding a fixed amount to
`EndAt` would still reward waiting: bid with one second to go and the
room gets one second plus the window, while bidding early costs the
sniper nothing. Measured from the bid, every bid buys everybody the same
full window — so there is no moment better to bid at than any other,
which is what makes sniping pointless rather than merely harder.

**There is no cap on repeats**, deliberately. A Boli that keeps extending
is a Boli people are still bidding on, and ending it on a timer while
hands are still up is the thing being fixed. A Samaaj wanting a hard stop
closes it, which is one click and already exists. The validator caps the
*window* at an hour instead: longer than that and it extends on
essentially every bid, which is not anti-sniping, it is an auction that
never ends.

`ExtendIfClosing` is called from `PlaceBidCommandHandler` under the same
row lock as the bid itself, so two bidders racing in the last second
cannot both read the old closing time and write conflicting new ones —
the lock that makes "one highest bid" true doing a second job. It raises
`boli.extended.v1` only when the window actually moved, because an event
per bid on this service's busiest write path would be an outbox row each
time for something almost never true.

**Money is `long` paise, never a floating-point type.** A Boli is money,
and floating-point error shows up as a winning bid a rupee off what
somebody actually offered — in a record the Samaaj announces and
collects against.

**A bid is never amended or deleted.** The bid history is what a Samaaj
shows when somebody asks how a Boli went, and a history that can be
edited afterwards answers that question with whatever the editor
preferred. A bidder who wants out is outbid, not erased.

**The bid history never carries a member id.** Only "is this mine". A
public running list of who is prepared to pay what turns an auction into
a statement about people's means. The wireframe says as much — "name
hidden until close" — and after the close only the winner is named.

**A recorded result names nobody, for anybody.** Not even the manager
who recorded it. The two steps exist so that nothing is announced before
it is announced, and a response shape that carries the winner "but only
to the right caller" is one authorization mistake away from announcing
it early. `WinningMemberId` is null until `PublishedAt` is not.

**The winner is not a parameter.** `RecordResultCommand` takes only the
Boli id and reads the highest bid. Accepting a winner would let a
recorded result name somebody who never made the highest bid, with the
append-only bid history sitting beside it contradicting it.

**The middle of the two-step workflow was unlistable, and that is the
same bug as having no workflow.** Recording and publishing are separate
acts, so a result sits between them — and until `ListPendingResultsQuery`
existed, the only read that reached one was `GetBoliResultQuery`, which
needs the Boli id you are looking for. Finding what was waiting meant
walking every occasion, then every Boli under it, asking each for a
result. Nothing was going to do that, so nothing did, and a result
recorded on the day was announced only if somebody remembered it.

The queue is oldest first, unlike the published list. It is a work queue,
and the one that has been waiting longest is the one most likely to have
been forgotten.

**The pending queue names an amount and never a winner**, and its shape
is separate from `BoliResultResponse` for exactly that reason. Adding
`WinningMemberId` "but only for the publisher" would replace one
invariant that is easy to keep — *one record names the winner, and only
after publication* — with one that is not. Nothing is lost: the winner is
read from the highest bid and is not something the publisher chooses, so
the amount is what identifies it. It does carry `RecordedBy`, which the
member-facing shape must not: who recorded a result is the second pair of
eyes' business and nobody else's.

**Publishing has its own permission.** `Boli.PublishResults`, separate
from `Boli.Manage`, which the platform's authorization catalogue already
anticipated. Both are currently granted to the same two roles, so it
separates nothing yet — but it is the right thing to gate on, and a
Samaaj wanting a second pair of eyes on announcements can grant one
without the other without this service changing.

**Publishing twice is success and changes nothing.** A retried request
has to be safe. The first announcement's `PublishedBy` and `PublishedAt`
survive, so a repeat cannot quietly reassign who announced it.
Recording twice, after publication, is refused — that is a correction,
and SERVICES.md requires corrections to be a distinct audited workflow
rather than a second publish.

**`EligibilityRule` is free text this service does not enforce, and the
comment saying so is load-bearing.** Real eligibility is things like
"one per family" or "members who have completed their Paryushan pledge"
— facts held in other services, or in nobody's database at all. A rule
engine here would produce a language that cannot express what a Samaaj
means and would still need a person to check it. It is shown to bidders
and enforced by the Samaaj.

**Status and clock are both required.** `AcceptsBids` checks the status
*and* the window. A Boli left `Open` past its closing time because
nobody clicked Close still stops taking bids, and one whose window has
not arrived does not take them early. `GetActiveBoliQuery` filters on
that rather than on the status column, which is why it filters in memory
rather than in SQL — one definition of "open now", on the aggregate.

**Bids are not part of the Boli aggregate, and Boli are not part of the
occasion.** A popular Boli takes hundreds of bids in its last minutes;
loading them to accept one more would read the whole table on the one
path that must stay fast exactly when it is busiest. The highest is a
`MAX` in the database.

## Dependencies

- PostgreSQL — `samaajconnect_boli`
- Kafka via the Outbox (`OutboxDispatcher`), publishing only
- No Redis. The lock is a database row lock, which needs nothing else
  running and has no "what if it is unreachable" case

## Testing

- `Sangam.Boli.UnitTests` — the Boli as a thing that runs over time:
  the window, the floor, the increment, the forward-only occasion, and
  the two-step close-then-publish.
- `Sangam.Boli.IntegrationTests` — Testcontainers against a real
  Postgres. `BidIndexTests` names the mechanism; `ConcurrentBiddingTests`
  proves the behaviour; `BoliLifecycleTests` walks a Boli from announced
  to announced. `OccasionLocalityTests` proves `LocalityId` is additive and
  non-breaking (an occasion created without one behaves exactly as before)
  and that both `ListOccasionsQuery` and `GetActiveBoliQuery` filter
  correctly when a locality is given and return everything when it is not.
  `BidLedgerAndPlaceTests` holds that the ledger names bidders only for
  `Boli.Manage`, that the member history still names nobody, and that the
  venue and city round-trip and filter.
- `scripts/smoke-through-gateway.sh` runs the same walk through YARP.
  **This service had no gateway coverage at all until the admin screens
  were built**, which root `CLAUDE.md` §9 asks for on every service — the
  module key was toggled on and off around it but no Boli endpoint was
  ever called through the gateway. Nothing had noticed, because every
  Boli endpoint was reachable only by curl in the first place, and the
  suite above curls the service directly.
