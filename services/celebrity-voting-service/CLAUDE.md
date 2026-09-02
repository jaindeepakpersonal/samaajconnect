# celebrity-voting-service

## Purpose

Runs the "Celebrities of Samaaj" award: a Samaaj puts members forward,
reviewers set the ballot, every member gets one vote, and the result is
frozen when it is announced.

Behind the `celebrity-voting` module key, on by default.

This is the platform's most contended write path, and the one place
`docs/product/SERVICES.md` calls correctness under concurrency a
requirement rather than a nice-to-have. Read "The guarantee" below
before changing anything under `Vote`.

## Entities

| Entity | Notes |
|---|---|
| `VotingCampaign` | Aggregate root. Title, the two windows, `TopN`, results visibility, status. Owns its candidates and **not** its votes |
| `Candidate` | One member's candidacy in one campaign. Nominated → Approved, or removed |
| `Vote` | One member's one vote. Written directly, outside the aggregate |
| `CampaignResult` | The frozen ranking, stored as ordered candidate ids in `jsonb` |

## Commands

| Command | Permission | Notes |
|---|---|---|
| `CreateCampaignCommand` | `CelebrityVoting.Configure` | Starts as `Draft` |
| `MoveCampaignCommand` | `CelebrityVoting.Configure` | Strictly forward. Refuses `VotingOpen` on an empty ballot |
| `NominateCandidateCommand` | `Members.Read` | A repeat nomination is a no-op reported as success |
| `DecideCandidateCommand` | `CelebrityVoting.Configure` | Approve onto the ballot, or remove before voting opens |
| `CastVoteCommand` | `Members.Read` | One per member per campaign. Refuses a self-vote |
| `PublishResultsCommand` | `CelebrityVoting.Configure` | Only from `Closed`. Computes once, then freezes |

## Queries

| Query | Permission | Notes |
|---|---|---|
| `ListCampaignsQuery` | `Members.Read` | Campaigns newest first, each with the caller's own vote |
| `GetCampaignQuery` | `Members.Read` | Ballot always; tally only when this caller may see it |
| `GetResultsQuery` | `Members.Read` | Reads the stored result, never recomputes |

## Events published

- `CampaignStatusChangedDomainEvent`
- `CampaignClosedDomainEvent`
- `ResultsPublishedDomainEvent`

## Events consumed

None. This service holds no copy of another service's data — a vote
carries a member id and nothing else about the member, so there is
nothing here to keep in step.

## API endpoints

See `docs/product/API-CONTRACTS.md` for the table, including the four
places the shipped shape departs from the requirements draft.

## The guarantee

**The unique index on `(CampaignId, VoterMemberId)` is what prevents
double voting.** Not the check in `CastVoteCommandHandler`, and not a
distributed lock.

Two requests from the same member arriving in the same millisecond both
pass any check-then-insert, because they both read before either writes.
At the close of voting that is the normal case, not the edge. Only the
database can refuse the second one.

`docs/product/SERVICES.md` offers "a Redis atomic lock *or* a unique DB
constraint". The constraint is strictly stronger: a lock has to decide
what to do when Redis is unreachable, and every answer to that is worse
than not needing one.

Three things follow, and none is optional:

- **The handler's `FindForVoterAsync` check is a courtesy.** It exists so
  that pressing the button twice produces "you have already voted"
  rather than a database error surfacing as a 500. It is not
  load-bearing and must not be mistaken for the guarantee.
- **`VoteRepository.TryCastAsync` writes on its own scope.** Two reasons.
  The request's transaction is held from the campaign read to the
  response, so writing there would serialise voters against each other
  for the length of a request rather than of an insert. And a unique
  violation poisons the change tracker it happened on — the failed entry
  stays `Added` and the next `SaveChanges` retries it — so on the
  request's context one refused vote would fail everything after it.
- **A duplicate is success, not failure.** `accepted: false`, with the
  vote they already hold. The member has done nothing wrong.

`VoteIndexTests` asserts the index directly against `pg_indexes` and
proves a second insert is refused with SQLSTATE 23505.
`ConcurrentVotingTests` proves the behaviour holds when twenty real
requests race. Both are needed: the second would also pass if a handler
happened to serialise its callers, which would stop being true the
moment the service ran on two instances.

## Decisions worth knowing before you change this service

**Votes are not part of the campaign aggregate.** A campaign in a large
Samaaj has thousands of votes; loading them to cast one more would read
the whole table on the busiest write path, exactly when it must not. So
`ICampaignRepository` includes candidates and never votes, and the tally
is a `GROUP BY` in the database rather than a count over a loaded
collection.

**Nominations and voting are never open at the same moment.**
`CreateCampaignCommandValidator` refuses a voting window that starts
before nominations close, so that members who vote early see the same
ballot as members who vote late. This is why the integration tests need
a movable clock (`TestClock`) rather than a fudged set of dates, and why
the smoke script has a real window to wait out.

**Status and clock are both required.** `AcceptsNominations` and
`AcceptsVotes` check the status *and* the window. The status is what an
administrator moved it to; the window is what the Samaaj was told. A
campaign left open past its closing date because nobody clicked Close
still stops taking votes.

**Moves are strictly forward.** Draft → NominationsOpen → VotingOpen →
Closed → Published, expressed as a next-state check rather than the
transition table `social-issues-service` needs, because the sequence is
short and has no branches. An election that can go backwards is not an
election.

**A candidate cannot be removed once voting has opened.** Removing them
would discard votes already cast for them.

**One candidacy per member per campaign**, however many people nominate
them — two entries for one person split their vote and make the result
meaningless. The aggregate refuses the second; a unique index on
`(CampaignId, MemberId)` is what holds if two nominators arrive at once.

**The published result is stored, not recomputed.** A result recomputed
on every read could change after it was announced — by a late vote, a
corrected one, a candidate removed — and an announced result that moves
is worse than no result at all. Publishing twice is refused for the same
reason: two rankings leave "the result" with no referent.

**`HiddenUntilClose` is a real setting, not decoration.** Members who
can see who is winning vote differently from members who cannot. Which a
Samaaj wants is theirs to decide, but it has to be decided before voting
opens rather than discovered afterwards. An administrator sees the tally
throughout, because somebody has to be able to tell whether the thing is
working. Note that `GetCampaignQueryHandler` does not fetch the tally at
all when the caller may not see it — fetching and then discarding would
work, and would be one refactor away from leaking.

**`Votes` is `int?` in the response, null when the tally is not visible.**
Null rather than zero: zero is a claim, and the wrong one.

## Dependencies

- PostgreSQL — `samaajconnect_celebrity_voting`
- Kafka via the Outbox (`OutboxDispatcher`), publishing only
- No Redis, and deliberately so — see "The guarantee"

## Testing

- `Sangam.CelebrityVoting.UnitTests` — the campaign as a thing that runs
  over time: windows, the forward-only sequence, one candidacy per
  member, who may see the running count. Plus `CampaignMappingsTests`,
  which covers **`RankBy` — the function that decides who is named the
  celebrity of a Samaaj, and which had no tests at all** until
  2026-09-02. It lives in the application layer rather than the
  aggregate, which is how it escaped a file named for the campaign.

**The tie-break is held by two mechanisms that agree, and a test can only
see that.** `Ballot` returns approved candidates ordered by
`NominatedAt`, and `RankBy` then adds `ThenBy(NominatedAt)` on top of a
sort LINQ already performs stably. Delete either one on its own and
nothing changes; delete both and an announced ranking can reshuffle
between two reads of the same numbers. So the test creates its
candidates *out of* chronological order — which distinguishes "one of
the two is working" from "neither is", and is the most a test at this
level can honestly claim. A test built on candidates created in order
passes whichever one you delete, which is what the first version of it
did.
- `Sangam.CelebrityVoting.IntegrationTests` — Testcontainers against a
  real Postgres. `VoteIndexTests` names the mechanism; the concurrency
  tests prove the behaviour.
- `scripts/smoke-through-gateway.sh` exercises the whole campaign
  through YARP, including the module gate in both directions.

`DEVELOPMENT_PLAN.md` also asks for a load test of the vote endpoint.
`ConcurrentVotingTests` is the correctness half of that and is the half
that can pass or fail. Measuring throughput under sustained load is a
different exercise and belongs with the Phase 5 performance work,
against a deployed environment rather than a Testcontainer.
