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
| `ChildProfile` | not built | Next. |
| `ChildConversionRequest` | not built | Blocked on an open decision — see below. |

## Commands

| Command | Policy | Status |
|---|---|---|
| `CreateProfileForNewUserCommand` | `[InternalRequest]` | built |
| `UpdateProfileCommand` | self, or `Members.Write` | built |
| `CreateFamilyCommand` | any member | built |
| `RequestJoinFamilyCommand` | any member | built |
| `DecideJoinRequestCommand` | head of that family | built |
| `CreateChildProfileCommand` | `FamilyHead` | not built |
| `RequestChildConversionCommand` / `ApproveChildConversionCommand` | — | not built |

## Queries

| Query | Policy | Status |
|---|---|---|
| `SearchMembersQuery` | `Members.Read` | built |
| `GetMyProfileQuery` | any authenticated role | built |
| `GetMyFamilyQuery` | any member | built |
| `ListEligibleConversionsQuery` | — | not built |

## Events published

| Event | Topic | Raised by |
|---|---|---|
| `MemberProfileUpdatedDomainEvent` | `members.profile.updated.v1` | `MemberProfile.Update` |
| `FamilyCreatedDomainEvent` | `members.family.created.v1` | `Family.Create` |

## Events consumed

`identity.user.registered.v1`, to create the initial profile.

The subscription is a single topic, unlike audit-notification-service's
catch-all regex. This service *acts* on what it consumes, and subscribing to
events it has no handler for would mean quietly committing offsets for messages
it did nothing with.

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

**An unusable `UserRegistered` payload is skipped, not retried.** The consumer
would otherwise exhaust its retries on one bad message and stall every
registration queued behind it. The Warning log is the signal.

## Open decision blocking the children work

`DEVELOPMENT_PLAN.md` lists **adult-child conversion: admin-approved vs
self-service**, with admin-approved as the recommended default. Resolve it
before building `ChildConversionRequest`; the two shapes differ in who the
approval flow even involves.

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
