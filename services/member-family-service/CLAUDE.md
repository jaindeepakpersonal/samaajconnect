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
| POST | `/v1/families/{familyId}/join-requests/{requestId}/decide` | head of that family |
| GET | `/v1/children` | any member |
| POST | `/v1/children` | head of that family + `Family.Write` |
| POST | `/v1/children/{id}/conversion` | head of that family + `Family.Write` |
| GET | `/v1/children/conversion-requests` | `SamaajAdmin` + `Family.ApproveConversion` |
| POST | `/v1/children/conversion-requests/{requestId}/decide` | `SamaajAdmin` + `Family.ApproveConversion` |
| GET | `/v1/children/data-notice` | any member |
| GET | `/v1/members/me/data-export` | any authenticated role |
| GET | `/health` | anonymous |

## Decisions worth knowing before you change this service

**A profile's id is the user's id.** DATA-MODEL.md §3 says `Id (=UserId)`, and
following it literally buys two things: two services agree on one identifier for
a person without either owning the other's table, and a profile can be found
from a token's `sub` claim with no lookup in between. It also makes "have I
already created this profile?" a primary-key check rather than a dedupe table,
which is what makes the consumer idempotent.

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
