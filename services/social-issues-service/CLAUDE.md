# social-issues-service

## Purpose

Concerns members raise about their community — road safety near the school,
support for elderly members, lighting in the park — and the review that stands
between raising one and the Samaaj hearing about it. **The Samaaj Panch judges
them**: once a Samaaj has elected one (panch-service), only its members review an
issue, discuss it, record a decision, and — if required — hand the monitoring to
a volunteer group.

Module-gated on **`social-issues`**, its own key rather than `community`. It is
the first service to use a second module key, so it is also where the gate is
shown to work per-module rather than all-or-nothing.

## Entities

| Entity | Status | Notes |
|---|---|---|
| `SocialIssue` | built | Tenant-scoped. A seven-state workflow, declared as a table, plus the Panch's decision. |
| `IssueStatusHistory` | built | Append-only within the aggregate. Every move, with who and why. |
| `PanelMember` | built | This service's copy of who sits on each Samaaj's Panch, fed by `panch.panel.changed.v1`. |
| `IssueAttachment` | not built | Waits on virus scanning, not on storage any more — see below. |

## Commands

| Command | Policy | Status |
|---|---|---|
| `SubmitIssueCommand` | `Members.Read` | built |
| `ReviseIssueCommand` | `Members.Read` + is the author | built |
| `MoveIssueCommand` | `Members.Read`, then author-or-Panch-member per transition | built |
| `RecordDecisionCommand` | `Members.Read`, then Panch member | built. `DecisionPending` → `Closed`, with the decision text and an optional monitoring group |
| `ConsumeIntegrationEventCommand` | `[InternalRequest]` | built. Erases what a member wrote, from the erasure topic; replaces a Samaaj's Panch roster, from the panel topic |

## Queries

| Query | Policy | Status |
|---|---|---|
| `ListIssuesQuery` | `Members.Read` | built |
| `GetIssueQuery` | `Members.Read` | built |
| `GetApprovalQueueQuery` | `Members.Read`, then Panch member (or reviewer permission, read-only) | built |
| `GetReviewAccessQuery` | `Members.Read` | built. Whether the caller reviews, and whether the Samaaj has a Panch |

## Events published

| Event | Topic | Raised by |
|---|---|---|
| `IssueSubmittedDomainEvent` | `social-issues.issue.submitted.v1` | `SocialIssue.Create` |
| `IssueStatusChangedDomainEvent` | `social-issues.issue.status-changed.v1` | `SocialIssue.MoveTo` |
| `IssueDecidedDomainEvent` | `social-issues.issue.decided.v1` | `SocialIssue.RecordDecision`. Ids only |

`social-issues.issue.published.v1` is retired: publishing no longer exists. Its
audit descriptor stays, for the rows already written under it.

## Events consumed

| Event | Topic | What it does |
|---|---|---|
| user erased | `identity.user.erased.v1` | Replaces what that member wrote — the issues they raised, any reason they gave in a history entry, and a decision they recorded |
| panel changed | `panch.panel.changed.v1` | Makes this Samaaj's `PanelMember` rows match the roster the event carries. Idempotent: it is a snapshot, so redelivery changes nothing |

**This service shipped as a leaf and should not have stayed one.**
`DPDP-COMPLIANCE.md` states that any service consuming platform events must
subscribe to the erasure topic on the day it ships; six services did not, found
by the 2026-09-01 security-checklist pass. Social issues was one of the two
where it mattered: an issue carries free text its submitter wrote, which
identifies them whatever happens to the member id beside it.

What erasure does here is `SocialIssue.ErasePersonalDataOf`, and the rule is
**the words go and the shape stays**. An issue is a container — a reviewer's
decisions and reasons hang off it as history, and those are the reviewer's
records rather than the submitter's — so it is emptied rather than deleted. The
**status is not moved**: a published issue that vanished would leave a Samaaj
wondering what happened to something it was told about. What it said is gone;
that it existed is not the submitter's alone to erase.

The submitter's own reasons go with it, because a submitter is an actor in their
own workflow — resubmitting after changes were asked for — and those words are
theirs like any other.

`SubmittedByMemberId` is deliberately not cleared, for the reason given in
timeline-service's own note: it is open counsel question 6 in
`DPDP-COMPLIANCE.md` and should be answered once for every service holding a
bare member id.

## API endpoints

| Method | Path | Roles |
|---|---|---|
| GET | `/v1/social-issues` | `Members.Read` |
| POST | `/v1/social-issues` | `Members.Read` |
| GET | `/v1/social-issues/approval-queue` | Panch member, or `SocialIssues.Approve` read-only |
| GET | `/v1/social-issues/review-access` | `Members.Read` |
| GET | `/v1/social-issues/{id}` | `Members.Read` |
| PUT | `/v1/social-issues/{id}` | `Members.Read` + author |
| POST | `/v1/social-issues/{id}/status` | `Members.Read`, then per transition |
| POST | `/v1/social-issues/{id}/decision` | `Members.Read`, then Panch member |
| GET | `/health` | anonymous |

## The workflow

Seven states — Draft, Submitted, UnderReview, **DecisionPending**, Rejected,
ChangesRequested, Closed — and the transitions are a **table** in
`SocialIssue.Transitions` rather than logic scattered through methods. Adding a
state means adding rows there, not an `if` somewhere.

`Approved` and `Published` no longer exist. A reviewer's approval now puts an
issue in **DecisionPending**: in front of the Panch, visible to the Samaaj, and
waiting for a decision. The migration maps existing rows — Approved becomes
DecisionPending, Published becomes Closed — and keeps `published_at` as
`decided_at`, so an issue that was visible stays visible.

**DecisionPending → Closed is deliberately not a row in the table.** It carries a
payload — the decision text, and optionally the volunteer group that will
monitor — so it has its own method (`SocialIssue.RecordDecision`) and its own
command. A plain status move that closed a pending issue would leave a decided
issue with no decision, and `MoveIssueCommand` refuses it (409).

**Who reviews is the Panch, once there is one.** `ReviewAccessResolver` is the
one place that answers it, so the queue, the detail and the move handler cannot
disagree:

| Caller | Sees the queue | Can move and decide |
|---|---|---|
| A seated Panch member | yes | yes |
| A holder of `SocialIssues.Approve`, **no Panch seated** | yes | yes — the fallback |
| A holder of `SocialIssues.Approve`, **a Panch seated** | yes | **no** |
| Anybody else | no | no |

The fallback exists because the module that elects a Panch is off by default:
without it every issue in every Samaaj that has not elected one would stall the
day this shipped. It closes the moment a Panch is seated, because "only the Panch
judges" with an exception for administrators is a rule that does not hold. An
administrator keeps sight of the queue so that somebody can tell whether it is
being worked.

**Any Panch member may record the decision.** No quorum, no chair, no term: the
Panch discusses offline and one of its members writes down the outcome. Who did
is on the issue (`DecidedBy`) and in the history.

**Some moves belong to the author.** A member may withdraw their own issue until
the Panch has it. The table's `ByAuthor` column says which kind each move is,
and `MoveIssueCommandHandler` checks the actor against that, not against a role
claim alone.

**One command for every transition, not seven.** The table decides legality, so
seven handlers would be seven copies of the same tenant, author and permission
checks.

**The response says which moves this caller can make.** `availableTransitions`
is computed from the same table the aggregate enforces, and `canDecide` says
whether the decision form belongs on screen, so the buttons a screen shows and
the moves the server accepts cannot drift apart.

## Decisions worth knowing before you change this service

**Deciding straight from `Submitted` is allowed.** The admin wireframe's queue
card offers Approve, Reject and Request Changes on a submitted issue, so
requiring a separate "start review" click would be a step the design does not
have. `UnderReview` exists for a reviewer who wants to claim one first.
("Approve" is the move to `DecisionPending`.)

**Rejecting and requesting changes need a reason; approving does not.**
Declining somebody's concern about their own community needs an explanation, and
the author is told it.

**The history is append-only and travels with the detail.** It is what answers
"why was mine rejected?", and a screen that has to make a second request for
that will sometimes not make it.

**Editing stops once a reviewer has put it in front of the Panch.** A reviewer
who approved one thing and finds another being discussed has been made to
endorse something they never read.
An issue still under review *can* be corrected, because "Request Changes" only
means something if changes are possible.

**Visible to the Samaaj means DecisionPending, or Closed *with a decision*.**
A closed issue with no decision — withdrawn by its author, or rejected — stays
private; it used to become public just for being Closed, which made withdrawing
something publish it. `IsPublic` on the aggregate and the filter in
`IssueRepository.ListPublicAsync` say the same thing twice on purpose (one is
not translatable to SQL) and must be kept in step.

**The decision text stays off the bus.** `issue.decided.v1` carries ids — the
issue, who decided, the monitoring group — never the words, for the reason the
submitted event carries no description: audit-notification-service records
payloads verbatim into an append-only table. The words stay on the issue, where
erasure reaches them: a Panch member's decision is replaced if they erase their
account, and that the issue was decided, and when, is not.

**The roster is a local copy, replaced not patched.** `panch.panel.changed.v1`
carries the whole roster, so the consumer replaces a Samaaj's rows; applying the
same snapshot twice changes nothing, which is what makes at-least-once delivery
harmless. The consumer reads with no tenant, so `PanelRepository.ReplaceAsync`
takes the tenant from the event and ignores the query filter — the same as the
erasure path, and for the same reason: a filtered read compares every row to
`Guid.Empty` and a roster that silently never changes is the failure.

**The monitoring group is a loose id.** volunteer-groups-service owns groups;
checking one would be a synchronous call that couples the two. The portal offers
only real groups.

**An unpublished issue is its author's and its reviewers'.** Anyone else is told
it does not exist rather than that they may not see it — the difference confirms
one with that id is there. The same reasoning as an unapproved timeline post.

**Category is a closed list.** The published list and the reviewer's queue both
filter on it, and free text makes a filter that quietly misses things. The list
lives in `SubmitIssueCommandValidator.Categories` and `ReviseIssueCommand` reads
the same one — two lists would drift, and the way they drift is a revision that
cannot be saved.

**Events carry the category and nothing else a member wrote.** What somebody
says is wrong in their community can name neighbours, describe a dispute, or be
exactly the thing a reviewer decides not to publish — and
audit-notification-service records payloads verbatim in an append-only table.
Putting it there would permanently publish the one thing review exists to hold
back. The reviewer's reason stays out for the same reason: it is written to the
member it is about.

**Deciding raises its own event as well as the status change**, so a consumer
that only cares about decisions does not have to filter every move in the
workflow.

**No attachments, and that is a decision — but the reason changed and this
paragraph did not.** The wireframe has an "Attach Evidence" button and
`DATA-MODEL.md` has `IssueAttachment` with a `ScanStatus`. What used to be
written here was that "the platform has no file storage", and that stopped being
true on 2026-09-04: member photos, children's photos and Samaaj logos are stored
bytes now.

The rest of the reasoning survives intact, and it is stronger here than in
timeline-service. `SECURITY-CHECKLIST.md` asks for uploads to be size- and
type-restricted **and virus-scanned**; storage answered the first half and not
the second. Evidence attached to a social issue is exactly the file that needs
the second — a photograph of a place, a person, a document — and it carries a
second problem storage does nothing about: an issue can be raised about a
person, so an attachment can be a photograph of somebody who never consented to
being in it, retained for as long as the issue is. That is a DPDP question about
what may be collected, not a question about where the bytes live.

Accepting a link to somebody else's host remains refused outright: an unscanned
file in front of a reviewer, and the reviewer's address leaked to that host.

**The visibility filter lives in the repository, not as `IsPublic` in the
query.** A computed property cannot be translated to SQL, and the failure mode
is a silent client-side evaluation that loads every issue in the Samaaj.

## Dependencies

- **Postgres** `samaajconnect_social_issues` — `ConnectionStrings__Default`
- **Kafka** — `Kafka__BootstrapServers`. Produces its own events, and consumes
  `identity.user.erased.v1` and `panch.panel.changed.v1`
- **Jwt** — `Jwt__SigningKey`, validation only. This service never mints tokens.
- No Redis dependency.

## Testing

- `Sangam.SocialIssues.UnitTests` — the transition table, exhaustively, and the
  decision. Which moves are legal, which reach `DecisionPending`, which belong
  to the author, and what `RecordDecision` refuses are all pure decisions and
  belong here.
- `Sangam.SocialIssues.IntegrationTests` — Testcontainers Postgres, no Kafka.
  The tenant filter, the whole submitted → decided path, who may review with and
  without a Panch, the roster consumer, and the outbox row landing in the same
  transaction as the transition are database claims.

```
dotnet test services/social-issues-service/Sangam.SocialIssues.sln
```

`scripts/smoke-through-gateway.sh` walks the full path through the gateway —
submit, sent back, revised, resubmitted, put in front of the Panch, decided — and
switches this module off while leaving the timeline's untouched.
