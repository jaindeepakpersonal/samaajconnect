# panch-service

## Purpose

Seats and records the **Samaaj Panch**: the ten-member jury a Samaaj elects
from its own members. It runs the elections, holds the seated roster, and
announces every change to that roster so other services (today,
social-issues-service) can decide who may judge.

Behind the `panch` module key, **off by default**: a Samaaj switches it on
when it is ready to elect a Panch.

## Entities

| Entity | Notes |
|---|---|
| `PanchElection` | Aggregate root. Title, the nomination and voting windows, results visibility, status. Owns its candidates and **not** its ballots |
| `Candidate` | One member's candidacy in one election. Nominated → Approved, or removed |
| `Ballot` | One member's one ballot in one election. Written directly, outside the aggregate |
| `Vote` | One choice on a ballot: voter, candidate. At most ten per ballot |
| `ElectionResult` | The frozen ranking and the seated member ids, as ordered ids in `jsonb` |
| `PanelMember` | The seated roster. One row per seated member per Samaaj. Its own aggregate so a removal raises its own event |

## Commands

| Command | Permission | Notes |
|---|---|---|
| `CreateElectionCommand` | `Panch.Manage` | Starts as `Draft`. Nominations close before voting opens |
| `UpdateElectionCommand` | `Panch.Manage` | Title, description, both windows, results visibility. `Draft`/`NominationsOpen` only - see below |
| `ExtendVotingCommand` | `Panch.Manage` | While voting is open, moves its close **later**. Never earlier |
| `DeleteElectionCommand` | `Panch.Manage` | `Draft`/`NominationsOpen` only, with its nominations. Never once it holds ballots |
| `MoveElectionCommand` | `Panch.Manage` | Strictly forward: `NominationsOpen`, `VotingOpen`, `Closed`. Refuses `VotingOpen` on an empty ballot |
| `NominateCandidateCommand` | `Members.Read` | A repeat nomination is a no-op reported as success |
| `DecideCandidateCommand` | `Panch.Manage` | Approve onto the ballot, or remove before voting opens |
| `CastBallotCommand` | `Members.Read` | Up to ten distinct approved candidates, no self-vote. One ballot per member, final |
| `PublishElectionResultsCommand` | `Panch.Manage` | Only from `Closed`. Freezes the ranking and **replaces the roster** with the winners |
| `RemovePanelMemberCommand` | `Panch.Manage` | Unseats one member. The seat stays empty until the next election |

## Queries

| Query | Permission | Notes |
|---|---|---|
| `ListElectionsQuery` | `Members.Read` | Newest first, each with the caller's own choices |
| `GetElectionQuery` | `Members.Read` | Ballot always; tally only when this caller may see it |
| `GetResultsQuery` | `Members.Read` | Reads the stored result, never recomputes |
| `GetPanelQuery` | `Members.Read` | The seated roster, and whether the asker sits on it |

## Events published

- `ElectionStatusChangedDomainEvent` — `panch.election.status-changed.v1`
- `ElectionUpdatedDomainEvent` — `panch.election.updated.v1`
- `ElectionVotingExtendedDomainEvent` — `panch.election.voting-extended.v1`
- `ElectionDeletedDomainEvent` — `panch.election.deleted.v1`
- `CandidateDecidedDomainEvent` — `panch.candidate.decided.v1`
- `PanelChangedDomainEvent` — `panch.panel.changed.v1`

## Events consumed

None. A ballot and a seat carry member ids and nothing else about the
member; names are resolved by the portals through the member directory.

## API endpoints

See `docs/product/API-CONTRACTS.md`.

## The guarantees

**The unique index on `(ElectionId, VoterMemberId)` on `ballots` is what
makes it one ballot per member.** Not the check in the handler, which two
concurrent requests both pass. A voter's ballot row and all of their
votes are inserted in one transaction, on the repository's own scope, so a
second submission violates the index and leaves nothing behind (and does
not poison the request's change tracker). A duplicate is reported as
`accepted: false` with the choices already held, not as an error.

**"At most ten" is validated on one request**, because a whole ballot is
one submission. That is why a ballot is a row of its own rather than ten
independent votes, where the cap would be a race.

**One candidacy per member per election** is a unique index on
`candidates (ElectionId, MemberId)`; two entries for one person would
split their votes.

## Decisions worth knowing before you change this service

**Ten seats, top ten win, and a winner needs at least one vote.** Ties are
broken by earliest nomination, so the ranking is stable and never depends
on row order. Fewer than ten candidates with votes means fewer than ten
seats.

**Publishing replaces the roster.** There is no term: a seated roster
stands until the next published election or an admin removes a member. A
removed member's seat stays empty; nobody is promoted.

**`panch.panel.changed.v1` carries the full member-id snapshot**, not a
delta. A consumer replaces its copy of the roster, so redelivery and
reordering under at-least-once delivery are harmless. Ids only, per the
rule for anything that reaches the audit trail.

**Ballots are never loaded with the election.** The tally is a `GROUP BY`
in the database.

**An election is corrected, extended and deleted by three different rules
(2026-10-25).** Until then a created election could not be changed at all:
nominations that closed with too few names, or a typo in the title, meant
leaving a dead election on the list and starting another.

- **Edit** (`PATCH`) replaces the title, description, both windows and
  results visibility, with the create form's window rules - while it is a
  draft or taking nominations. Nominations that closed too early are
  reopened by pushing their close out: the status never advanced, so there
  is nothing to undo. The same rule celebrity-voting and volunteer-groups use.
- **Extend voting** is the one change allowed once voting has opened, and it
  only moves the close **later**. Members were told the windows and whether
  the count is live, and some have voted against both. Moving the close
  earlier while ballots arrive would let whoever runs the election end it
  while the count suits them, and lock out anybody planning to vote on the
  last day; closing early stays possible, as the visible Close.
- **Delete** removes the election and its nominations while it is a draft or
  taking nominations - before any ballot can exist. From voting onward the
  election holds a record of how the Samaaj voted and is closed instead. The
  row is gone, so `panch.election.deleted.v1` (with who deleted it, and the
  stage it was at) is the only trace it existed; it is raised before the
  delete and drained in the same save.

Each names the administrator who acted, and the extension also carries the
close members were first told, so the audit row shows both.

**Every administrator's act on an election names them (2026-10-25).** Until
then the status change - opening nominations, opening voting, closing it,
publishing - carried no actor, and approving or turning down a nomination
raised no event at all. A browser walk through the whole flow found the
audit trail could not say who closed voting or who kept a nominee off the
ballot. `ElectionStatusChangedDomainEvent` now carries `ChangedBy`, and
`CandidateDecidedDomainEvent` records each decision with `DecidedBy` and
`Approved` or `TurnedDown`. A turned-down nomination is deleted, so its event
is the only trace it existed. A decision that changes nothing - approving
somebody already on the ballot, or trying to remove one after voting opens -
raises nothing. Creating an election is still unaudited: it is a draft
nobody can see until nominations open, and opening them names who did.

## Testing

- `Sangam.Panch.UnitTests` — the election rules: ranking, ties, the
  one-vote minimum, forward-only status, the panel events.
- `Sangam.Panch.IntegrationTests` — Testcontainers against real Postgres:
  the ballot cap, one ballot per voter under concurrency, the roster
  replaced on publish, removal, tenant isolation, outbox rows, and the
  unique ballot index read from `pg_indexes`.
