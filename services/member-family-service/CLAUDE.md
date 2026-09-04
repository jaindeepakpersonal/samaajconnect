# member-family-service

## Purpose

Who a member is, and which household they belong to. Owns the Samaaj member
directory and the family join flow.

This is the first service whose primary aggregate is created by **another**
service's event rather than by one of its own endpoints, so it is the platform's
worked example of a cross-service flow with no synchronous call.

## Entities

| Entity | Status | Notes |
|---|---|---|
| `MemberProfile` | built | Tenant-scoped. Its `Id` **is** the user id from identity-tenant-service. |
| `Family` | built | Household, joined by quoting a family code. |
| `FamilyMember` | built | Membership or a pending request; carries the relationship. |
| `ChildProfile` | built | No login of its own; the family manages the record. |
| `ChildConversionRequest` | built | Admin-approved. |
| `ParentalConsent` | built | Owned by `ChildProfile`; required to create one (DPDP s.9). |
| `StoredImage` | built | A photo the platform hosts: bytes, sniffed type, and whose. |

## Commands

| Command | Policy | Status |
|---|---|---|
| `CreateProfileForNewUserCommand` | `[InternalRequest]` | built |
| `UpdateProfileCommand` | self, or `Members.Write` | built |
| `CreateFamilyCommand` | any member | built |
| `RequestJoinFamilyCommand` | any member | built |
| `DecideJoinRequestCommand` | head of that family | built |
| `CreateChildProfileCommand` | head of that family + `Family.Write` | built |
| `RequestChildConversionCommand` | head of that family + `Family.Write` | built |
| `DecideChildConversionCommand` | `SamaajAdmin` + `Family.ApproveConversion` | built |
| `EraseMemberDataCommand` | `[InternalRequest]` | built |
| `WithdrawJoinRequestCommand` | any member, their own request | built |

## Queries

| Query | Policy | Status |
|---|---|---|
| `SearchMembersQuery` | `Members.Read` | built |
| `GetMyProfileQuery` | any authenticated role | built |
| `GetMyFamilyQuery` | any member | built |
| `ListFamilyChildrenQuery` | any member | built |
| `ListConversionRequestsQuery` | `SamaajAdmin` + `Family.ApproveConversion` | built |
| `GetChildDataNoticeQuery` | any member | built |
| `GetMyDataQuery` | any authenticated role | built |
| `GetChildNamesQuery` | `SamaajAdmin`/`SuperAdmin` + `Members.Read` | built |

## Events published

| Event | Topic | Raised by |
|---|---|---|
| `MemberProfileUpdatedDomainEvent` | `members.profile.updated.v1` | `MemberProfile.Update` |
| `FamilyCreatedDomainEvent` | `members.family.created.v1` | `Family.Create` |
| `ChildConversionApprovedDomainEvent` | `members.child-conversion.approved.v1` | `ChildConversionRequest.Approve` |

## Events consumed

| Topic | Why |
|---|---|
| `identity.user.registered.v1` | Create the initial profile |
| `identity.child-conversion.completed.v1` | Mark the child Converted and link the account |
| `identity.user.erased.v1` | Erase everything this service holds about that member |

An explicit topic list, unlike audit-notification-service's catch-all regex.
This service *acts* on what it consumes, so subscribing to anything it has no
handler for would mean quietly committing offsets for messages it did nothing
with. It is also a list rather than a pattern because librdkafka's regex support
is not the full grammar - a pattern with alternation silently matched nothing
here, which looks exactly like a broker problem and is not one.

## API endpoints

| Method | Path | Roles |
|---|---|---|
| GET | `/v1/members` | `Members.Read` |
| GET | `/v1/members/me` | any authenticated role |
| PATCH | `/v1/members/{id}` | self, or `Members.Write` |
| POST | `/v1/families` | any member |
| GET | `/v1/families/mine` | any member |
| POST | `/v1/families/join-requests` | any member |
| DELETE | `/v1/families/join-requests/mine` | any member, their own request |
| POST | `/v1/families/{familyId}/join-requests/{requestId}/decide` | head of that family |
| GET | `/v1/children` | any member |
| POST | `/v1/children` | head of that family + `Family.Write` |
| POST | `/v1/children/{id}/conversion` | head of that family + `Family.Write` |
| GET | `/v1/children/conversion-requests` | `SamaajAdmin` + `Family.ApproveConversion` |
| POST | `/v1/children/conversion-requests/{requestId}/decide` | `SamaajAdmin` + `Family.ApproveConversion` |
| GET | `/v1/children/names` | `SamaajAdmin`, `SuperAdmin` + `Members.Read` |
| GET | `/v1/children/data-notice` | any member |
| GET | `/v1/members/me/data-export` | any authenticated role |
| GET | `/health` | anonymous |

## Decisions worth knowing before you change this service

**A member could ask to join a household and never take it back.** A pending
request counts as belonging to one — deliberately, so nobody can ask two
families at once and have both heads accept — and nothing could cancel it. A
head who was slow, or who never looked, or who erased their account, left that
member permanently unable to join anywhere or create a household of their own.
The only way out ran through somebody else. That is not an edge case; it is what
happens when a head is simply slow.

`WithdrawJoinRequestCommand` is the member's own way out. It takes no
parameters: a member has at most one standing request by construction, so naming
which one would be asking for a fact the caller cannot get wrong and the server
already knows.

**It withdraws a request and never a membership.** If the head accepted while
the member was deciding to withdraw, the call is refused by name rather than
quietly succeeding — otherwise "cancel my request" would silently mean "leave my
family" for exactly the person whose request had just been accepted. Leaving a
household you are in is a different act with different consequences, and it does
not exist yet.

**Headship passes to the longest-standing member when the head erases**, and
this file used to say the opposite. It said re-heading "belongs in an admin
command, not here". That was wrong: an admin command needs an administrator to
notice, and nothing tells them — so a household whose head erased stayed frozen
until somebody complained. Four things stopped at once, because all four are
gated on `IsHead`: deciding a join request, adding a child, starting a
conversion, and seeing the family code to invite anyone. A household of five was
unusable because one of them exercised a right.

Doing it in the consumer means the headless state never exists rather than
existing until repaired. Longest-standing is the rule because it needs no
judgement and can be explained in a sentence — and it is the earliest to have
*joined*, not to have asked, so a request accepted last week does not outrank a
member of ten years. A household with nobody left keeps the departed id and is
inert: no member can see it and no request can reach it, because the code is only
ever shown to a head.

**The platform hosts photos now, and that was a DPDP fix rather than a feature.**
`PhotoUrl` was a link a client supplied, validated as absolute `http(s)` — which
closed the `javascript:` hole and said plainly that it did nothing about the real
one. Every member who opened the directory fetched the picture from whatever host
it named, handing that host their IP address; on a `ChildProfile` that is exactly
the third-party tracking of children s.9(3) prohibits. `StoredImage` holds the
bytes and `PhotoImageId` replaces the URL on both aggregates.

**The bytes live in this service's own database, and the alternative was
considered rather than skipped.** MinIO in compose and S3 in production is the
obvious answer and it was rejected for this scale: a Samaaj runs to a few thousand
members with one photo each, capped at 2 MB and typically a tenth of that. What an
object store would cost is a second place data lives — one that
`scripts/backup-restore-drill.sh` does not dump, so a platform that has spent real
effort proving its backups restore would have quietly acquired a store outside
them. In the database they are inside the existing dump, inside the tenant query
filter, and inside the transaction that writes the profile row. `IImageStore` is
the seam that makes changing that one implementation and a migration.

**The type is read from the bytes, never from the upload.** A declared
`Content-Type` is a string the uploader chose. `ImageContent.Sniff` reads the
signature, and the type served back to a browser is the one it derived — so
`StoredImage.Capture` has no `contentType` parameter at all, and a test asserts
that absence. JPEG, PNG and WebP only. **SVG is excluded and the exclusion is
load-bearing**: an SVG is a document that can carry script and these are served
from the platform's own origin, so accepting one would trade the tracking hole
for a stored-scripting one.

**Authorization is the profile's, which is why the bytes are served here.** Who
may see a member's photo is who may see the member; who may see a child's photo
is their household. Those rules already lived in this service. A media service
would have had to be told them, asked about them, or handed a signed URL — three
ways for a second copy of a rule to disagree with the first. It also gives
`SECURITY-CHECKLIST.md`'s "authorization-checked per request, not just obscured
by a random URL" for free.

**`Members.Write` opens a member's photo and not a child's.** An administrator
correcting a member's details is administrative work; a child's photograph is
not. Same line `DecideJoinRequestCommand` draws, for the same reason.

**Replacing a photo deletes the old row in the same transaction**, and the
aggregate hands the id back rather than deleting anything itself — it does not
know about the images table. A method that silently orphaned the previous row
would leave a photograph of somebody in the database with nothing referring to
it and no path that would ever find it again. Erasure uses
`RemoveAllForOwnerAsync` rather than the id the profile holds, because "we
deleted the one we knew about" is not what erasure means.

**What is not done: virus scanning.** Sniffing proves the bytes begin like an
image and says nothing about what a decoder does with the rest of them. That
needs a scanner in the deployment, and `ImageContent`'s remarks say so rather
than letting the size and type checks read as complete.

**A profile's id is the user's id.** DATA-MODEL.md §3 says `Id (=UserId)`, and
following it literally buys two things: two services agree on one identifier for
a person without either owning the other's table, and a profile can be found
from a token's `sub` claim with no lookup in between. It also makes "have I
already created this profile?" a primary-key check rather than a dedupe table,
which is what makes the consumer idempotent.

**A cross-service read re-checks the tenant itself, as belt and braces.**
`GetChildNamesQuery` answers another service's ids with names, and its handler
compares every row's `TenantId` against `ITenantContext` as well as relying on
the global query filter. The filter is not broken - that was claimed here for a
day and it was wrong, see the note below. The check stays because this read has
the IDOR shape SECURITY-CHECKLIST.md is about: the ids come from another
service, and the answer is children's data.

**A fault-injection experiment left a broken build, and it cost most of a
cycle.** The way this repo checks that a test really tests something is to break
the thing and watch it fail. That is sound, and it has caught real problems - but
the restore has to be verified before anything is concluded from the next run.
It was not, once: `ListByIdsAsync` was deliberately given `IgnoreQueryFilters()`,
and results read afterwards were attributed to the restored code. That produced a
confident, written-up, security-flagged report of a tenant-filter bug that does
not exist.

The cheap habit that would have caught it: after restoring, re-run the
*unmodified* case and confirm it passes before drawing any conclusion from a
failure. And treat "this contradicts how EF demonstrably works" as evidence about
the build, not about EF.

**Being unlisted is not a privacy level, and could not be one.** Per-field
privacy cannot remove a member from the directory: someone who marks every
field Private is still there under their name, because a directory listing *is*
a name. `MemberProfile.IsListedInDirectory` is the wireframe's "Profile listed
in directory" checkbox, which had no field behind it until now.

**It hides a member from the directory search and from nothing else.**
`GetByIdAsync` still returns them, and has to: a volunteer group's president
needs to see who applied, a timeline post has an author, a household has
members. Making an unlisted profile 404 would break all three and would turn a
listing preference into an access control it was never meant to be. Three of
the four `SearchAsync` callers therefore pass `includeUnlisted: true` - the
household ones and the DPDP s.11 export - and only the directory itself passes
false. A Samaaj admin passes true as well, for the same reason they see through
privacy levels: a member an administrator cannot find is a member nobody can
help.

**The setting is required on update, not defaulted.** `UpdateProfileCommand`
takes `bool?` and the validator refuses null, exactly like `Privacy`. Defaulting
it to true would have put a member who opted out back into the directory the
next time they edited their address - silently, and by a client that had never
heard of the field.

**Erasure now takes the profile out of the directory too.** The row survives so
family links do not dangle, and until this flag existed there was no way to say
"keep the row, drop the listing" - so an erased profile stayed in the directory
as a row reading "Erased member". The migration backfills the ones that were
already there.

**Privacy is per field, not one toggle.** SECURITY-CHECKLIST.md is explicit
about this. `FieldPrivacy` carries a level for mobile, email, address,
profession and date of birth separately, and `MemberMappings.ToDirectoryResponse`
is the single place a profile becomes a directory row — so there is one place to
check the rules are applied. A hidden field comes back **null, not masked**: a
mask still leaks length and shape.

**New profiles start closed.** Email and address default to Private, mobile and
profession to SamaajOnly. Closed by default and opened deliberately, rather than
open by default and hoping the member notices.

**Search matches names and localities only.** Never contact details, whatever
their privacy level says. Matching on a private mobile number would let anyone
confirm one digit-guess at a time — the filtering afterwards would be beside the
point.

**A Samaaj admin sees every field; deciding join requests is still the head's
call.** Correcting a member's details is administrative work (SERVICES.md), so
`Members.Write` sees through privacy levels. Who is in someone's household is
not administrative, so `DecideJoinRequestCommand` is restricted to the head even
for admins.

**A member belongs to one household, and a pending request counts.** Otherwise
someone could ask two families at once and both heads could accept. A previously
*rejected* request may be made again — circumstances and minds both change.

**Family codes are per Samaaj, and a code from elsewhere is "no such family".**
Not "wrong Samaaj": the difference would confirm a code exists somewhere on the
platform.

**Family codes avoid 0/O and 1/I/L.** These travel by being read aloud between
relatives, which is exactly when those characters go wrong.

**A bool with a database default needs `ValueGeneratedNever` too.**
`HasDefaultValue(true)` on `IsListedInDirectory` made it `ValueGeneratedOnAdd`,
and EF then leaves a CLR-default value out of the INSERT so the database default
can apply. The CLR default of a bool is `false` - which is exactly the value
that means "not listed" - so inserting an unlisted profile wrote no column and
the row came back listed, with the aggregate and the database quietly
disagreeing. Updates were never affected, which is what made it a landmine
rather than an outage: it only bites a row created unlisted. Three integration
tests caught it. `HasDefaultValue` is worth keeping - it is what lets the column
be added to a live table without emptying every directory - so the fix is to say
the aggregate owns the value.

**Family and FamilyMember ids are `ValueGeneratedNever`.** The aggregates assign
them. Left as EF's default `ValueGeneratedOnAdd`, a child added to a *tracked*
parent comes back as Modified rather than Added, and the save fails with a
concurrency exception against a row that was never there. This cost an hour;
apply the same setting to any future domain-assigned key.

**An unusable consumed payload is skipped, not retried.** The consumer would
otherwise exhaust its retries on one bad message and stall everything queued
behind it. The Warning log is the signal.

**The completion consumer reads children through `GetForConsumerAsync`.** A
consumer has no request and so no resolved tenant, and the ordinary
tenant-filtered lookup therefore finds nothing - which reads as "this child was
deleted" and silently drops the event. Any new consumer path needs the same
treatment; this caught us twice.

## Adult-child conversion

Decided 2026-08-28: **admin-approved**. The family head raises the request once
the child turns 18; a Samaaj admin holding `Family.ApproveConversion` decides.
Self-service was the alternative and was rejected as the less safe default -
creating a platform login is not something a household should do unilaterally,
and the rule is easy to relax later if approval proves too slow.

Three things about the flow are deliberate:

**Eligibility is derived from the date of birth, not stored.** DATA-MODEL.md
lists `EligibleForConversion` as a status, but a stored one needs a nightly job
to move children into it and is silently wrong on any day that job does not run.
The stored status records only the two things that are actual decisions - `Minor`
and `Converted` - and `IsEligibleForConversion(today)` computes the rest.

**The approval event carries no credential.** audit-notification-service records
every event payload verbatim into an append-only table, so a password or even a
hash travelling on `members.child-conversion.approved.v1` would land somewhere
deliberately impossible to redact. The event carries the name and the chosen
identifier; how the new member first authenticates is identity-tenant-service's
problem.

**Approval does not mark the child `Converted`.** The login does not exist until
identity-tenant-service has consumed the event and created it. A child record
claiming an account nobody can sign in to would be worse than one that lags by a
second.

The loop closes in identity-tenant-service, which consumes the approval,
creates a `PendingActivation` account, and announces
`identity.child-conversion.completed.v1` once the new member has redeemed an
activation code. Only then does the child record here become `Converted`.

## DPDP and children's data

Full mapping in `docs/product/DPDP-COMPLIANCE.md`. This service holds the
platform's largest compliance surface, because `ChildProfile` exists by design
and section 9 treats anyone under 18 as a child.

**A child record cannot be created without recorded parental consent.** The
factory requires the attesting member and throws without one; the validator
refuses the request before it gets there. Section 9 makes the consent the basis
on which the data may be held, so this is a precondition rather than a field.

**The attestation is stored verbatim, not just its version.** Keeping only a
version number would mean reconstructing the wording from source control to
answer "what did they actually agree to?".

**Section 9(3) is satisfied by absence.** The platform does no tracking, no
behavioural monitoring and no advertising to children, and the notice says so.
Keep it that way - this is the obligation most easily broken by adding an
innocuous-looking analytics call.

**Withdrawing parental consent is erasure, not a toggle.** Unlike a member's own
consent, this is not a switch: the record exists because of it. That is why
`ParentalConsent` sits on the child rather than in a log of decisions.

## Erasure

A member erased their account in identity-tenant-service; everything here has
to follow. `docs/product/DPDP-COMPLIANCE.md` has the platform-wide picture.

**Children go with the head who vouched for them.** Their records exist on that
person's parental consent (s.9), and consent that no longer exists cannot keep
justifying the data it covered. The birth year survives, shifted to 1 January:
age is what decides conversion eligibility so the row still has to behave, and
the exact birthday is how a child would be recognised.

**The household stays; the membership row goes.** Who was in whose household is
personal data about the erased member, so their `FamilyMember` row is removed
in every case. Deleting the `Family` itself was the alternative and is wrong:
it would take the remaining members' join with it and orphan the child rows -
other people's records restructured because one person exercised their own
right. The cost is that a household whose head has erased can no longer decide
a join request. Re-heading one is a known gap and belongs in an admin command,
not in a consumer.

**Privacy levels are closed as well as the fields cleared.** A profile left
`Public` keeps appearing in the directory as a visible row, which is not what
erasure means to the person who asked for it.

**A cross-tenant write is refused at `SaveChanges`, not only by the
handler.** `TenantWriteGuard` compares every added or modified
`ITenantScopedEntity` against `ITenantContext.TenantId` and throws when they
disagree. Handlers still do their own check - a 404 saying no such row exists in
this Samaaj is a far better answer than an exception - but a handler that
forgets one looks exactly like one that does not need it, so the rule is also
enforced where it cannot be skipped. The guard is silent when no tenant is
resolved, because consumers legitimately have none.

## Dependencies

- **Postgres** `samaajconnect_member_family` — `ConnectionStrings__Default`
- **Kafka** — `Kafka__BootstrapServers`, consumer and producer
- **Jwt** — `Jwt__SigningKey`, validation only
- No Redis dependency.

## Testing

- `Sangam.MemberFamily.UnitTests` — the privacy rules field by field, and the
  family aggregate. No I/O. The privacy mapping is `internal` but is the
  security-relevant code here, so it is tested directly rather than only
  through an endpoint.
- `Sangam.MemberFamily.IntegrationTests` — Testcontainers Postgres and Kafka.
  The broker is real because the claim being tested is a cross-service one: a
  registration published elsewhere produces a profile here.

```
dotnet test services/member-family-service/Sangam.MemberFamily.sln
```

`scripts/smoke-through-gateway.sh` covers the same path through the gateway,
including waiting for the profile to arrive over Kafka after registration.

**Running every service's suite back to back needs a pause between them.** This
is the largest set of integration tests on the platform and the one that fails
first: with eleven solutions started in a row, its Postgres and Kafka containers
compete with the previous solution's still shutting down, and it failed 13 of 67
once and 0 of 67 on the next run with a ten-second gap. CI is unaffected - it
runs one job per service. If this suite fails in a local sweep, re-run it alone
before believing it.
