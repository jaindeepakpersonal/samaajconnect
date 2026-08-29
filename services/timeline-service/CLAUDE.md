# timeline-service

## Purpose

The Samaaj's shared timeline: announcements from the Samaaj, posts from
members, and the moderation queue that stands between the two.

This is the first **module-gated** service on the platform. Its gateway route
carries `Metadata.module: community`, so a Samaaj that has switched that module
off gets 404 on every path here — see "The module gate" below.

## Entities

| Entity | Status | Notes |
|---|---|---|
| `TimelinePost` | built | Tenant-scoped. The moderation lifecycle is the substance of it. |
| `PostComment` | built | Owned by the post; only an approved post accepts one. |
| `PostReaction` | built | At most one per member per post. |
| `ModerationAction` | built | Append-only within the aggregate. Every decision, with who made it. |
| `PostMedia` | not built | Waits on file storage — see below. |

## Commands

| Command | Policy | Status |
|---|---|---|
| `CreatePostCommand` | `Timeline.Post` | built |
| `ModeratePostCommand` | `Timeline.Moderate` | built |
| `AddCommentCommand` | `Timeline.Post` | built |
| `ReactToPostCommand` | `Timeline.Post` | built |
| `ReportPostCommand` | `Timeline.Post` | built |

## Queries

| Query | Policy | Status |
|---|---|---|
| `GetFeedQuery` | `Timeline.Post` | built |
| `GetPostQuery` | `Timeline.Post` | built |
| `GetModerationQueueQuery` | `Timeline.Moderate` | built |

## Events published

| Event | Topic | Raised by |
|---|---|---|
| `PostSubmittedDomainEvent` | `timeline.post.submitted.v1` | `TimelinePost.Create` |
| `PostModeratedDomainEvent` | `timeline.post.moderated.v1` | `TimelinePost.Moderate` |
| `PostReportedDomainEvent` | `timeline.post.reported.v1` | `TimelinePost.Report` |

## Events consumed

None. This service is a leaf: it publishes and reacts to nothing, which is why
there is no consumer here and no Kafka in its integration tests.

## API endpoints

| Method | Path | Roles |
|---|---|---|
| GET | `/v1/timeline/posts` | `Timeline.Post` |
| POST | `/v1/timeline/posts` | `Timeline.Post` |
| GET | `/v1/timeline/posts/moderation-queue` | `Timeline.Moderate` |
| GET | `/v1/timeline/posts/{id}` | `Timeline.Post` |
| POST | `/v1/timeline/posts/{id}/moderate` | `Timeline.Moderate` |
| POST | `/v1/timeline/posts/{id}/comments` | `Timeline.Post` |
| PUT | `/v1/timeline/posts/{id}/reaction` | `Timeline.Post` |
| POST | `/v1/timeline/posts/{id}/report` | `Timeline.Post` |
| GET | `/health` | anonymous |

## Authorization

**Every command and query here declares a permission and no `[RequiresRoles]`.**
That is a deliberate departure from the older services, which carry both. A role
list beside the permission is a second, longer answer to the same question that
has to be kept in step with `AuthorizationCatalog` by hand — and the failure
mode is silent: one command ends up with a shorter list than its neighbours and
a member can post but not comment. Whoever holds `Timeline.Post` may post.

Members hold `Timeline.Post` through the `Member` role; moderators hold it too,
because everyone with a login is a Member first.

## The module gate

The gateway route carries `Metadata.module: community`, which makes this the
first route `ModuleGateMiddleware` actually decides anything about. Two
behaviours are worth knowing:

**A disabled module answers 404, not 403.** A Samaaj that does not run the
community module should be indistinguishable from a platform that has no such
feature (`ARCHITECTURE.md` §6).

**So does a module route with no resolved Samaaj.** A Super Admin who has not
selected one has no tenant, the gate cannot check the module, and it refuses
rather than letting the request through unchecked — before the service is
reached at all. `CreatePostCommandHandler` still checks for a missing tenant;
that is defence in depth for a caller who bypasses the gateway, not the path a
real request takes.

**The gateway caches what a Samaaj runs for 60 seconds**, so switching a module
is not instant. The smoke script waits it out rather than pretending otherwise.

## Decisions worth knowing before you change this service

**A member post waits; an announcement does not.** The member-portal wireframe's
button says "Post for Review", and `TimelinePost.Create` is what makes that
true rather than a label. An announcement is created `Approved` because only
someone holding `Timeline.Moderate` may create one — routing it through a queue
would mean an administrator approving their own post, a step that reads as a
control and is not one.

**The feed shows a member their own pending posts, and shows them to nobody
else.** The wireframe puts "Your Post • Pending Review" in the same list, and it
is right: a member who posts and then cannot see it anywhere reasonably
concludes it was lost. That is why the feed is two repository calls merged
rather than one query that knows who is asking.

**Reporting removes nothing.** One member being able to take a post off a
community's timeline would be a heckler's veto. A report raises a count and puts
the post in front of a moderator; the decision stays there. Reported posts join
the same queue as new ones, because a separate "reports" screen is a screen
somebody has to remember to open.

**Who reported a post is never stored and never returned.** In a community
organisation where everyone knows each other, a reporter who could be identified
is a reporter who stays quiet. The consequence is that reports are not counted
per person — a determined member could report twice — and that is the trade
taken deliberately.

**Reporting answers the same way whether or not it counted.** A member who
learns their own report was ignored has learned how the queue is fed.

**Rejecting or hiding needs a reason; approving does not.** Those are the cases
where the member will ask why, and "no reason given" is not an answer.

**Events carry no post body and no moderation reason.**
audit-notification-service records payloads verbatim into an append-only table.
Moderation exists precisely because some of what members write should not end up
on the Samaaj's timeline — putting it somewhere deliberately hard to redact
would defeat that on the post that most needed it. A moderator's note about a
member is about that member, and stays out for the same reason.

**A post nobody can see reports as "not found", not "forbidden".** Commenting on
a pending post means the id was guessed; confirming it exists is the leak.

**No media, and that is a decision.** The wireframe has an "Attach Photo" button
and `DATA-MODEL.md` has `PostMedia` with a `ScanStatus`. Both are honest about
what is needed: `SECURITY-CHECKLIST.md` requires uploaded files to be size- and
type-restricted and virus-scanned before being served. The platform has no file
storage, and accepting a link to somebody else's host would put an unscanned
image in front of the whole Samaaj and send every viewer's address to that host —
a much larger surface than the single profile photo that precedent came from.
Media arrives with storage, tracked in `DEVELOPMENT_PLAN.md`.

**Domain-assigned keys are `ValueGeneratedNever`.** All four of them. Left as
EF's default, a child added to a tracked parent comes back Modified rather than
Added and the save fails against a row that was never there. This repo has hit
that on `Family` and again on `UserRole`; the configuration here was written
that way from the start.

## Dependencies

- **Postgres** `samaajconnect_timeline` — `ConnectionStrings__Default`
- **Kafka** — `Kafka__BootstrapServers`, producer only
- **Jwt** — `Jwt__SigningKey`, validation only. This service never mints tokens.
- No Redis dependency.

## Testing

- `Sangam.Timeline.UnitTests` — the aggregate. The moderation lifecycle,
  reactions and reporting are all pure decisions and belong here.
- `Sangam.Timeline.IntegrationTests` — Testcontainers Postgres, **no Kafka**.
  Unlike member-family-service there is nothing consumed to prove, and what
  these tests are about — the tenant query filter, and the outbox row landing in
  the same transaction as the post — are database claims. The configured broker
  address deliberately goes nowhere, so the dispatcher cannot ship a row the
  test is about to assert on.

```
dotnet test services/timeline-service/Sangam.Timeline.sln
```

`scripts/smoke-through-gateway.sh` covers the whole path through the gateway,
including switching the community module off and watching the timeline
disappear.
