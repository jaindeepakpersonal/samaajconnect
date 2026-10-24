# competition-service

## Purpose

Runs a Samaaj competition end to end: an organiser sets one up, members
register while registration is open, the organiser runs it across one or
more judged rounds, assigned judges score each entry, and a round's result
is computed once and frozen when it is published.

Module-gated on `competitions`, off by default: not every Samaaj runs one,
the same stance `ModuleCatalog` already takes for `boli`, `matrimony` and
`businesses`.

A competition is not an Event - it is deliberately a standalone service,
with no dependency on events-service and no change to it.

## Entities

| Entity | Notes |
|---|---|
| `Competition` | Aggregate root. Title, the registration window, status. Owns its registrations, judges and rounds - all bounded and few, the same reasoning `VotingCampaign` owns its candidates |
| `CompetitionRegistration` | One member's registration. Registered/Withdrawn; withdrawing then registering again reactivates the same row rather than inserting a second one |
| `CompetitionJudge` | A member the organiser named a judge. Grants no platform role - the same "a judge is just a member" shape volunteer-groups-service's own group lead already takes |
| `Round` | One judged round. Created Open; Closed stops new entries and scores and allows publishing. No automatic advancement between rounds - the organiser creates entries by hand in every round, including the first |
| `Entry` | One participant's place in one round. A separate, tenant-scoped aggregate - not owned by `Competition` or `Round` - for the same "don't let the busy child balloon the parent" reasoning that keeps businesses-service's `Order` its own aggregate |
| `Score` | One judge's mark for one entry. Also separate and never loaded by a parent, the same reasoning as `Vote`/`Bid` - but unlike those, an **upsert** while its round is open, since correcting a mark before judging closes is the ordinary case here, not ballot-stuffing |
| `RoundResult` | A round's frozen ranking: entry ids in rank order, stored as `jsonb`. Mirrors `CampaignResult` |
| `CompetitionPhoto` | A competition's cover photo bytes. `Competition.PhotoId` points at it; it cascades with the competition |

`Competition.CompetitionTypeId` is a master-data-service `LookupItem` id,
`Category = CompetitionType` - a flat, admin-managed platform-wide list
alongside `EventType`/`BusinessCategory`, added 2026-09-13 with no
migration since `LookupItem.Category` is already `HasConversion<string>()`.
This service stores the id and nothing else - no referential integrity
across the service boundary, the same stance every service holding one of
these ids already takes.

## Commands

New permission `Competition.Manage`, minted in identity-tenant-service and
granted directly to `SamaajAdmin` - the same shape `CelebrityVoting.Configure`
already has (an organiser permission, not a new platform role).

| Command | Permission | Notes |
|---|---|---|
| `CreateCompetitionCommand` | `Competition.Manage` | Starts `Draft`. Optional `LocalityId` |
| `UpdateCompetitionCommand` | `Competition.Manage` | Corrects the details, locality included. Draft or `RegistrationOpen` only; 409 after, when members have signed up to them |
| `MoveCompetitionCommand` | `Competition.Manage` | Strictly forward: Draft → RegistrationOpen → RegistrationClosed → InProgress → Closed |
| `RegisterForCompetitionCommand` | `Members.Read` | Only while registration is open. A repeat registration is a no-op reported as success |
| `WithdrawRegistrationCommand` | `Members.Read`, own registration only | |
| `AssignJudgeCommand` | `Competition.Manage` | Assign or remove, one command for both directions |
| `CreateRoundCommand` | `Competition.Manage` | Created `Open`. Refused once the competition itself is `Closed` |
| `CloseRoundCommand` | `Competition.Manage` | |
| `CreateEntryCommand` | `Competition.Manage` | Refuses a participant who is not currently registered, a duplicate entry in the same round, and a closed round |
| `RecordScoreCommand` | `Members.Read`, caller must be an assigned judge for this competition | Upsert while the round is open |
| `PublishRoundResultsCommand` | `Competition.Manage` | Only from a closed round. Computes once, then freezes; publishing again returns the same frozen result |
| `UploadCompetitionPhotoCommand` | `Competition.Manage` | The cover photo, replacing any. At any stage - a picture is not a detail members registered against. 404 for another Samaaj's |
| `RemoveCompetitionPhotoCommand` | `Competition.Manage` | Twice is success |

## Queries

Reuse `Members.Read` rather than minting a read-side key, the same shape
businesses-service's and volunteer-groups-service's own list/get queries
already take.

| Query | Permission | Notes |
|---|---|---|
| `ListCompetitionsQuery` | `Members.Read` | This Samaaj's competitions, each with the caller's own registration status. `?localityId=` narrows to one locality |
| `GetCompetitionQuery` | `Members.Read` | Rounds, registration count, judges, and whether the caller may manage it |
| `GetRoundQuery` | `Members.Read` | A round's entries. Marks are visible only to the organiser or an assigned judge - never to an ordinary member, before or after publication |
| `GetRoundResultQuery` | `Members.Read` | The published ranking. 404 until a result exists |
| `GetCompetitionPhotoQuery` | `Members.Read` | The cover photo bytes, for any signed-in member of the Samaaj |

## Events published

- `CompetitionStatusChangedDomainEvent`
- `CompetitionUpdatedDomainEvent` — `competition.competition.updated.v1` (ids and who)
- `CompetitionRegistrationCreatedDomainEvent`
- `RoundClosedDomainEvent`
- `RoundResultsPublishedDomainEvent`

All four have `audit-notification-service` `KnownEvents.cs` descriptors.

## Events consumed

None. A leaf service: it holds no copy of another service's data, so there
is nothing here to keep in step.

## API endpoints

All under `/v1/competitions/**`. See `docs/product/API-CONTRACTS.md`.

## Decisions worth knowing before you change this service

**There is no automatic advancement between rounds.** An organiser creates
one `Entry` per participant they want in a given round, by hand, for every
round including the first - a simple one-round competition just has one
round with an entry for everyone who registered, and an elimination format
has the organiser create fewer entries in the next round after reviewing the
previous one's published result. "Does an entry exist for this round"
already answers "is this participant still in it," which is what makes an
automatic top-N cutoff (with its own edge cases - a tie at the cutoff, a
withdrawal mid-competition) not worth building for what the backlog asked
for.

**A score is an upsert, not a never-amend record like `Vote`.** Correcting a
misrecorded mark before judging closes is the ordinary case here, not
ballot-stuffing, so `IScoreRepository.UpsertAsync` updates an existing row
rather than refusing a second write. The unique index on
`(EntryId, JudgeMemberId)` is what makes "upsert" well-defined: it is still
written on its own scope, for the same two reasons celebrity-voting-service's
own `VoteRepository.TryCastAsync` is - keeping a judge scoring many entries
from serialising against other judges for the length of a request, and
keeping a unique-violation on a race from poisoning the request's own change
tracker. Unlike a vote, losing the race is retried as an update rather than
refused.

**Individual judge scores are never shown to an ordinary member, before or
after publication.** Only the organiser and this competition's own assigned
judges see per-entry marks while a round is open; everyone sees the
published ranking (rank and average) once a `RoundResult` exists. The same
reasoning social-issues-service keeps a reviewer's own reason private.

**The published ranking is stored, not recomputed - but the averages shown
alongside it are still computed fresh on every read.** The *order* is frozen
at publish time so an announced result cannot reshuffle from a later
correction, mirroring `CampaignResult`'s own reasoning; the averages
displayed next to that order are re-tallied each time, the same choice
celebrity-voting-service's own results query makes for its tally.

**Domain-assigned keys are `ValueGeneratedNever`.** Every aggregate's `Id`
is a fresh `Guid.NewGuid()` from its own factory or constructor. This
repository has hit the EF default several times already in other services;
see their own `CLAUDE.md`s.

## Locality

Added 2026-09-27. A competition may name the locality it is held for
(`LocalityId`, a loose reference into master-data-service's Locality, like an
event's), or none for the whole Samaaj. It bounds nobody - every member may
register for any competition - and exists for the admin panel's locality
filter (`GET /v1/competitions?localityId=`).

## Venue, city and photo

Added 2026-10-24 (the user's request). `Venue` is where it is held in the
organiser's words (300 characters, blank means none) and `CityId` the city or
village, a master-data id beside the existing `LocalityId`; both are set and
corrected with the rest of the details, under `UpdateCompetitionCommand`'s
"until registration closes" rule.

The cover photo is `CompetitionPhoto`, events-service's `EventPhoto`
copied: the bytes in this service's own database, sniffed by `ImageContent`
(the sixth copy, held identical to the others by
`scripts/security-invariants.sh`), served `Cache-Control: private` with an
ETag. Unlike the details it can change at any stage. Replacing one removes the
old in the same transaction, and `photoUrl` carries `?v={photoId}` so a
replaced photo is a new URL. The upload and removal re-check the competition's
tenant themselves rather than trusting the query filter (root CLAUDE.md
section 6). Neither raises an event, as no other detail edit beyond
`CompetitionUpdatedDomainEvent` does. `CompetitionPhotoTests` covers it.

## Dependencies

- **Postgres** `samaajconnect_competition` - `ConnectionStrings__Default`
- **Kafka** - `Kafka__BootstrapServers`, publishing only
- **Jwt** - `Jwt__SigningKey`, validation only. This service never mints
  tokens.
- No Redis dependency.

## Testing

- `Sangam.Competition.UnitTests` - `Competition` (the forward-only sequence,
  one registration per member, judge assign/remove idempotency, round
  creation refused once closed) and `CompetitionMappings.RankBy` - the
  ranking/tie-break function - tested the same deliberately-out-of-order way
  `CampaignMappingsTests` is, so the test can tell "the tie-break is doing
  the work" from "the entries happened to already be in the right order".
- `Sangam.Competition.IntegrationTests` - Testcontainers Postgres. The full
  register → assign judge → create round → create entries → score → close →
  publish path end to end, the score unique-index upsert holding under
  twenty concurrent writes from one judge (`ScoreIndexTests`, mirroring
  `VoteIndexTests`' discipline of naming the index directly), and tenant
  isolation.

```
dotnet test services/competition-service/Sangam.Competition.sln
```

`scripts/smoke-through-gateway.sh` covers this service through the gateway:
the module toggled on, a competition set up and registration opened, two
members registering, a third assigned as judge, a round opened with an
entry per registrant, the judge scoring both, the round closed and
published, the higher-scored entry named first and visible to an ordinary
member, and the module toggled off again with every route answering 404
while the underlying data survives.
