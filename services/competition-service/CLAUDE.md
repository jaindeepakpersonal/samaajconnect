# competition-service

## Purpose

Runs a Samaaj competition end to end: an organiser sets one up - its type,
how many rounds, the max marks, and whether an audition comes first - members
apply while registration is open, judging starts and everybody who applied is
entered, assigned judges mark each participant, and the winners are computed
once and frozen when they are published.

Module-gated on `competitions`, off by default: not every Samaaj runs one,
the same stance `ModuleCatalog` already takes for `boli`, `matrimony` and
`businesses`.

A competition is not an Event - it is deliberately a standalone service,
with no dependency on events-service and no change to it.

## Entities

| Entity | Notes |
|---|---|
| `Competition` | Aggregate root. Title, the registration window, status, and the format (2026-10-25): `RoundCount`, `MaxMarks`, `AuditionRequired`, `AuditionDate`, `FinalistCount` - see "Format, audition and winners". Owns its registrations, judges and rounds - all bounded and few, the same reasoning `VotingCampaign` owns its candidates |
| `CompetitionRegistration` | One member's registration. Registered/Withdrawn; withdrawing then registering again reactivates the same row rather than inserting a second one |
| `CompetitionJudge` | A member the organiser named a judge, on a `Panel`: Main, Audition or Both. Grants no platform role - the same "a judge is just a member" shape volunteer-groups-service's own group lead already takes |
| `Round` | One judged round, of a `Kind`: Audition or Main. Made from the format when judging starts. Created Open; Closed stops new entries and scores and allows publishing |
| `Entry` | One participant's place in one round. A separate, tenant-scoped aggregate - not owned by `Competition` or `Round` - for the same "don't let the busy child balloon the parent" reasoning that keeps businesses-service's `Order` its own aggregate |
| `Score` | One judge's mark for one entry. Also separate and never loaded by a parent, the same reasoning as `Vote`/`Bid` - but unlike those, an **upsert** while its round is open, since correcting a mark before judging closes is the ordinary case here, not ballot-stuffing |
| `RoundResult` | A round's frozen ranking: entry ids in rank order, stored as `jsonb`. Mirrors `CampaignResult` |
| `CompetitionResult` | The final standings, frozen: member ids best first, `jsonb`, one per competition. `RoundResult`'s shape one level up |
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
| `CreateCompetitionCommand` | `Competition.Manage` | Starts `Draft`. Optional `LocalityId`, and the format - every format field optional, a competition without them is one judging out of 100 with no audition |
| `UpdateCompetitionCommand` | `Competition.Manage` | Corrects the details and the format. Draft or `RegistrationOpen` only; 409 after, when members have signed up to them. Format fields left out stay as they were |
| `MoveCompetitionCommand` | `Competition.Manage` | Strictly forward: Draft → RegistrationOpen → RegistrationClosed → InProgress → Closed. Starting judging (InProgress) makes the rounds and enters everybody who applied |
| `RegisterForCompetitionCommand` | `Members.Read` | Only while registration is open. A repeat registration is a no-op reported as success |
| `WithdrawRegistrationCommand` | `Members.Read`, own registration only | |
| `AssignJudgeCommand` | `Competition.Manage` | Assign or remove, one command for both directions. `Panel` Main, Audition or Both (Main when left out, and the only one offered without an audition - 409 otherwise); assigning again on another panel moves the judge |
| `CreateRoundCommand` | `Competition.Manage` | Created `Open`. Refused once the competition itself is `Closed` |
| `CloseRoundCommand` | `Competition.Manage` | Closing the audition also picks its finalists and enters them in every main round; 409 when no judge has marked anybody |
| `CreateEntryCommand` | `Competition.Manage` | Refuses a participant who is not currently registered, a duplicate entry in the same round, and a closed round. For a late addition - judging enters everybody else |
| `RecordScoreCommand` | `Members.Read`, caller must be an assigned judge on the panel that judges this round | Upsert while the round is open. Out of the competition's `MaxMarks` (400 above it); 409 on the judge's own entry |
| `PublishRoundResultsCommand` | `Competition.Manage` | Only from a closed round. Computes once, then freezes; publishing again returns the same frozen result |
| `PublishCompetitionResultsCommand` | `Competition.Manage` | The final standings. Only once every main round is closed (409 before); computes once, then freezes |
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
| `GetCompetitionResultQuery` | `Members.Read` | The published final standings. 404 until published. Everybody gets the winners (rank 3 or better, ties included); only `Competition.Manage` gets every place |
| `GetScoresheetQuery` | `Competition.Manage` | Every judge's mark on every entry, round by round, each entry's average, who went through the audition, and the standings now. The organiser's panel |
| `GetMyJudgingQuery` | `Members.Read`, caller must be a judge of it (403 otherwise) | The judge's own sheet: the rounds their panel judges, every participant bar themselves, and the marks they gave |
| `GetCompetitionPhotoQuery` | `Members.Read` | The cover photo bytes, for any signed-in member of the Samaaj |

## Events published

- `CompetitionStatusChangedDomainEvent`
- `CompetitionUpdatedDomainEvent` — `competition.competition.updated.v1` (ids and who)
- `CompetitionRegistrationCreatedDomainEvent`
- `RoundClosedDomainEvent`
- `RoundResultsPublishedDomainEvent`
- `CompetitionResultsPublishedDomainEvent` — `competition.results.published.v1`: member ids best first, and `publishedBy`
- `FinalistsSelectedDomainEvent` — `competition.finalists.selected.v1`: who went through the audition, and `decidedBy` - whoever closed it

Every one has an `audit-notification-service` `KnownEvents.cs` descriptor; the
two final ones name their actor, because "who announced the winners" and "who
closed the audition that decided who took part" are the two decisions about
people this service makes.

## Events consumed

None. A leaf service: it holds no copy of another service's data, so there
is nothing here to keep in step.

## API endpoints

All under `/v1/competitions/**`. See `docs/product/API-CONTRACTS.md`.

## Decisions worth knowing before you change this service

**Entries come from the format, not from the organiser's hand (2026-10-25).**
This file said there was no automatic advancement and that an organiser
created every entry one at a time; the user asked instead for members to apply
and be judged in every round, with an audition's top N going through. "Does an
entry exist for this round" is still what says a participant is in it - only
who creates the rows changed. See "Format, audition and winners" below.
`CreateEntryCommand` stays, for somebody added late.

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
after publication.** A judge sees only the marks they gave, on their own
sheet; the organiser sees every judge's mark on the scoresheet (2026-10-25,
the user's request - "a panel to see score entered by all judges"); everyone
sees the published winners and their totals. The same reasoning
social-issues-service keeps a reviewer's own reason private.

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

## Format, audition and winners

Added 2026-10-25 (the user's request). A competition is set up with its
format, which stays editable until registration closes: `RoundCount` (0 to
20), `MaxMarks` (1 to 1000, what one judge gives one participant in one
round), and `AuditionRequired` with its `AuditionDate` and `FinalistCount`.
`CompetitionFormatValidator` holds the rules for both create and update; the
fields are optional on the wire, so a caller that predates them still works.

**The rounds are made when judging starts** (`Competition.SetUpRounds`, on the
move to InProgress): an Audition round first if there is one, then Round 1 to
N - or a single round called "Final" when `RoundCount` is 0, so scoring has one
shape everywhere and members are simply shown its top three. Rounds an
organiser opened by hand before then (`CreateRoundCommand`, kept for the API
and the smoke run) stand in for the main rounds. The same transaction enters
everybody who applied: into the audition when there is one, otherwise into
every main round.

**Closing the audition decides it.** The top `FinalistCount` by average
audition mark go through - **and everyone tied with the last of them**, the
user's choice of top-N with ties not split by anything nobody chose - and are
entered in every main round. A participant no judge marked did not audition
and never goes through, even with places to spare; an audition nobody has
marked is refused (409) rather than closed with no finalists.
`CompetitionScoring.SelectFinalists` is the rule, unit-tested.

**Judges sit on a panel.** Main, Audition or Both, chosen when assigned and
changed by assigning again; a competition with no audition has only Main.
`RecordScoreCommand` refuses a judge marking the other panel's round (403),
their own entry (409), and a mark above `MaxMarks` (400).

**The winners are the highest totals.** A participant's total is the sum of
their main-round averages, every round counting equally, an unmarked entry
counting nothing; audition marks only decide who takes part. Equal totals
share a place - 1, 2, 2, 4 - rather than being split by something no judge
applied, so "the top three" can be more than three people. Totals are compared
rounded, so two equal sums cannot differ by a floating-point last digit.
`CompetitionScoring.Rank` and `Winners` are the rule. The final standings are
published only once every main round is closed, frozen as
`CompetitionResult`, and shown to members as the winners alone.

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
- `CompetitionScoringTests` (unit) - the top N with ties at the cut-off, an
  unmarked participant never going through, shared places, the rounds a format
  makes, and judge panels.
- `CompetitionFormatTests` (integration) - the format round-tripping and its
  validation, everybody entered when judging starts, marks out of the max, a
  judge kept off the other panel's round and their own entry, the audition's
  finalists reaching every main round, and the winners published only once
  every round is closed, with the scoresheet the organiser's alone.
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
