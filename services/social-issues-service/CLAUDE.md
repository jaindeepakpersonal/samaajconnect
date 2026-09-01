# social-issues-service

## Purpose

Concerns members raise about their community — road safety near the school,
support for elderly members, lighting in the park — and the review that stands
between raising one and the Samaaj hearing about it.

Module-gated on **`social-issues`**, its own key rather than `community`. It is
the first service to use a second module key, so it is also where the gate is
shown to work per-module rather than all-or-nothing.

## Entities

| Entity | Status | Notes |
|---|---|---|
| `SocialIssue` | built | Tenant-scoped. An eight-state workflow, declared as a table. |
| `IssueStatusHistory` | built | Append-only within the aggregate. Every move, with who and why. |
| `IssueAttachment` | not built | Waits on file storage — see below. |

## Commands

| Command | Policy | Status |
|---|---|---|
| `SubmitIssueCommand` | `Members.Read` | built |
| `ReviseIssueCommand` | `Members.Read` + is the author | built |
| `MoveIssueCommand` | `Members.Read`, then author-or-reviewer per transition | built |

## Queries

| Query | Policy | Status |
|---|---|---|
| `ListIssuesQuery` | `Members.Read` | built |
| `GetIssueQuery` | `Members.Read` | built |
| `GetApprovalQueueQuery` | `SocialIssues.Approve` | built |

## Events published

| Event | Topic | Raised by |
|---|---|---|
| `IssueSubmittedDomainEvent` | `social-issues.issue.submitted.v1` | `SocialIssue.Create` |
| `IssueStatusChangedDomainEvent` | `social-issues.issue.status-changed.v1` | `SocialIssue.MoveTo` |
| `IssuePublishedDomainEvent` | `social-issues.issue.published.v1` | `SocialIssue.MoveTo` |

## Events consumed

| Event | Topic | What it does |
|---|---|---|
| user erased | `identity.user.erased.v1` | Replaces what that member wrote — the issues they raised, and any reason they gave in a history entry |

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
| GET | `/v1/social-issues/approval-queue` | `SocialIssues.Approve` |
| GET | `/v1/social-issues/{id}` | `Members.Read` |
| PUT | `/v1/social-issues/{id}` | `Members.Read` + author |
| POST | `/v1/social-issues/{id}/status` | `Members.Read`, then per transition |
| GET | `/health` | anonymous |

## The workflow

Eight states, and the transitions are a **table** in `SocialIssue.Transitions`
rather than logic scattered through methods. Eight states have fifty-odd
plausible transitions, and the only way to see which are allowed is to have them
written in one place. Adding a state means adding rows there, not an `if`
somewhere.

Two things the table encodes that are easy to lose otherwise:

**Publishing is reachable only from `Approved`.** That single row is the promise
the member's screen makes — "member submissions are published only after valid
approval". There is an integration test that tries to publish a merely submitted
issue and expects 409.

**Some moves belong to the author and some to a reviewer.** A member may
withdraw their own issue right up until it is published; after that the Samaaj
has been told, and taking it back is the Samaaj's decision rather than theirs.
The table's `ByAuthor` column says which kind each move is, and
`MoveIssueCommandHandler` checks the actor against that — not against a role
claim alone.

**One command for every transition, not seven.** The table decides legality, so
seven handlers would be seven copies of the same tenant, author and permission
checks — and the way that goes wrong is one of them quietly missing a check the
others have.

**The response says which moves this caller can make.** `availableTransitions`
is computed from the same table the aggregate enforces, so the buttons a screen
shows and the moves the server accepts cannot drift apart. A screen offering
Approve on something the server will refuse is worse than one offering nothing.

## Decisions worth knowing before you change this service

**Deciding straight from `Submitted` is allowed.** The admin wireframe's queue
card offers Approve, Reject and Request Changes on a submitted issue, so
requiring a separate "start review" click would be a step the design does not
have. `UnderReview` exists for a reviewer who wants to claim one first.

**Rejecting and requesting changes need a reason; approving does not.**
Declining somebody's concern about their own community needs an explanation, and
the author is told it.

**The history is append-only and travels with the detail.** It is what answers
"why was mine rejected?", and a screen that has to make a second request for
that will sometimes not make it.

**Editing stops once a reviewer has decided.** A reviewer who approved one thing
and finds another published has been made to endorse something they never read.
An issue still under review *can* be corrected, because "Request Changes" only
means something if changes are possible.

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

**Publishing raises its own event as well as the status change**, so a consumer
that only cares about publication does not have to filter every move in an
eight-state workflow.

**No attachments, and that is a decision.** The wireframe has an "Attach
Evidence" button and `DATA-MODEL.md` has `IssueAttachment` with a `ScanStatus`.
`SECURITY-CHECKLIST.md` requires uploads to be size- and type-restricted and
virus-scanned before being served; the platform has no file storage; and
evidence attached to a social issue is exactly the kind of file that would need
it — photographs of a place, a person, a document. Accepting a link to somebody
else's host would put an unscanned file in front of a reviewer and leak their
address to that host. It arrives with storage, tracked in `DEVELOPMENT_PLAN.md`.

**`PublicStatuses` lives in the repository, not as `IsPublic` in the query.** A
computed property cannot be translated to SQL, and the failure mode is a silent
client-side evaluation that loads every issue in the Samaaj.

## Dependencies

- **Postgres** `samaajconnect_social_issues` — `ConnectionStrings__Default`
- **Kafka** — `Kafka__BootstrapServers`, producer only
- **Jwt** — `Jwt__SigningKey`, validation only. This service never mints tokens.
- No Redis dependency.

## Testing

- `Sangam.SocialIssues.UnitTests` — the transition table, exhaustively. Which
  moves are legal, which reach `Published`, and which belong to the author are
  all pure decisions and belong here.
- `Sangam.SocialIssues.IntegrationTests` — Testcontainers Postgres, no Kafka.
  The tenant filter, the whole submitted → published path, and the outbox row
  landing in the same transaction as the transition are database claims.

```
dotnet test services/social-issues-service/Sangam.SocialIssues.sln
```

`scripts/smoke-through-gateway.sh` walks the full path through the gateway —
submit, sent back, revised, resubmitted, approved, published — and switches this
module off while leaving the timeline's untouched.
