# matrimony-service

## Purpose

Matrimony biodata and proposals within a Samaaj — a member prepares a
biodata profile, searches other members' biodata, sends a proposal, and
accepts or declines the ones sent to them.

Module-gated on `matrimony`, off by default: not every Samaaj wants this
running, the same stance `ModuleCatalog` already takes for `boli`.

Visibility is within the caller's own Samaaj only — there is no cross-tenant
search or proposal, and every read and write is scoped by the ordinary
tenant query filter like any other service.

## Entities

| Entity | Status | Notes |
|---|---|---|
| `BiodataProfile` | built | Tenant-scoped. `Id` **is** the member's own id — "has this member opted in?" is a primary-key check, the same convention `MemberProfile` itself uses. |
| `Proposal` | built | Tenant-scoped. A peer-to-peer fact between two `BiodataProfile`s, not owned by either one. |
| `MatrimonyEligibility` | built | Tenant-scoped, keyed `(TenantId, MemberId)`. Whether My Profile's marital status allows a biodata, fed by member-family-service's event - see "Who may hold a biodata" below. |

`BiodataProfile`'s starting values (name, DOB, Gautra, profession,
qualification, city, locality) are supplied by the caller at opt-in time,
copied once from whatever member-portal already holds via
`GET /v1/members/me` — **a one-time copy, not a link**. Editing biodata
afterwards never reaches back into the base profile, the same "same as
permanent" precedent member-family-service's own address shortcut set. No
synchronous call from this service to member-family-service exists anywhere;
member-portal is the party already authenticated to both.

`Diet` and `FamilyType` are plain domain enums, not master-data-service
categories — a small, fixed, platform-wide list nobody needs to
tenant-customize, the same reasoning `Gender` gets in member-family-service
rather than being a `LookupItem`. `MaritalStatusId`, `GautraId`,
`ProfessionId`, `QualificationId`, `CityId` and `LocalityId` are all
master-data-service `LookupItem` ids; this service stores the id and nothing
else.

**No photo field, and no separate contact fields.** Biodata reuses the
member's own existing photo (`GET /v1/members/{id}/photo`, already
authorized for any same-Samaaj member holding `Members.Read`) and never
copies Mobile or Email — those stay behind the base profile's own
`FieldPrivacy` and are reached through the existing member detail screen
once two members want to talk. This service holds no photo bytes and no
contact address of its own.

## Consent

`MatrimonyConsent` is an owned type on `BiodataProfile`, modelled on
member-family-service's `ParentalConsent` but adapted for self-consent
rather than parent-for-child: `GivenAt`, `NoticeVersion` and `Attestation`
captured verbatim at opt-in, `WithdrawnAt` nullable, a derived
`Stands => WithdrawnAt is null`. There is no `WithdrawnByMemberId` — consent
here is always given and withdrawn by the same person, so the field would
carry no information the record doesn't already have for free.

`MatrimonyConsentNotice` is a static class mirroring
member-family-service's `ChildDataNotice`: a `CurrentVersion` constant, what
is held and who sees it once opted in, and the attestation text the member
agrees to. Read via `GetMatrimonyConsentNoticeQuery`, shown before the
opt-in form the same way member-family-service's own child-data notice query
is shown before adding a child.

`BiodataProfile.OptIn` refuses construction without both `ConsentGiven` and
a non-empty `NoticeVersion`, unconditionally, in the aggregate itself — the
same belt-and-braces double-gate `ChildProfile.Create` already applies to
child consent. The command validator refuses `ConsentGiven: false` first,
so a member who declines gets a readable 400 rather than the aggregate's own
exception.

**Withdrawal deactivates; it does not erase — the opposite of
`ParentalConsent.DeIdentify()`, and deliberately so.** A `Proposal` is a
joint record between two members, each with a legitimate interest in their
own history — did I propose, was I accepted — not one member's data alone.
`WithdrawConsent` sets `Consent.WithdrawnAt` and `IsActive = false`, which
takes the profile out of search and blocks new proposals, but every
`Proposal` row the member was ever part of survives untouched, including
the other party's own view of it. Full destructive erasure
(`BiodataProfile.Erase`) only happens through the `identity.user.erased.v1`
consumer, on full account deletion — withdrawal is a preference, erasure is
a right, and this service keeps the two as different acts for the same
reason `ChildProfile` does.

Age is computed at call time (`BiodataProfile.AgeInYearsOn`), never stored —
the same "eligibility computed, not stored" precedent
`IsEligibleForConversion(today)` already sets for adult-child conversion.
Both `OptIn` and `Update` refuse an under-18 date of birth.

## Commands

Self-scoped, gated by the same role list member-family-service's own
profile-update command uses (`Member, FamilyHead, VolunteerGroupPresident,
PathshalaTeacher, PathshalaStudent, ContentModerator, BoliManager,
SamaajAdmin, SuperAdmin`) — no new permission key is minted, following that
command's own precedent of role-only gating for a purely self-scoped write.

| Command | Policy | Status |
|---|---|---|
| `OptIntoMatrimonyCommand` | role list above | built |
| `UpdateBiodataCommand` | role list above | built |
| `WithdrawMatrimonyConsentCommand` | role list above | built |
| `SendProposalCommand` | role list above | built |
| `RespondToProposalCommand` | role list above, recipient only | built |
| `WithdrawProposalCommand` | role list above, sender only | built |
| `EraseMatrimonyDataCommand` | `[InternalRequest]` | built |
| `RecordMaritalStatusCommand` | `[InternalRequest]` | built. Upserts `MatrimonyEligibility` from `members.marital-status.changed.v1`, applying only a change newer than the one held |

`SendProposalCommand` refuses a new proposal while a `Pending` or `Accepted`
one already exists between the same two members, in either direction — the
same "a pending request counts" reasoning member-family-service's own
join-request command already applies. A `Declined` or `Withdrawn` proposal may be re-sent: circumstances
and minds both change, the same justification already on record for a
rejected `GroupApplication`.

`EraseMatrimonyDataCommand` is raised only by this service's own Kafka
consumer, never mapped to an endpoint — see "Events consumed" below.

## Queries

Reuse the existing `Members.Read` permission rather than minting a new key,
mirroring volunteer-groups-service's own list/get queries' use of it.

| Query | Policy | Status |
|---|---|---|
| `GetMatrimonyConsentNoticeQuery` | `Members.Read` | built |
| `GetMyBiodataQuery` | `Members.Read` | built |
| `GetBiodataQuery` | `Members.Read` | built |
| `SearchBiodataQuery` | `Members.Read` | built |
| `ListSentProposalsQuery` | `Members.Read` | built |
| `ListReceivedProposalsQuery` | `Members.Read` | built |
| `GetMyMatrimonyEligibilityQuery` | `Members.Read` | built. `Eligible`, `NotEligible` or `Unknown` - so the screen can say why before offering a form |

`GetMyBiodataQuery` returns the caller's own biodata whether active or not,
so a withdrawn member can still see and reactivate it — reads as 204, not
200 with a null body, when nobody has opted in yet (an ASP.NET Core Minimal
API quirk: `Results.Ok(null)` serializes to an empty body, not the JSON
literal `null`, which is unambiguous as a status code where a null body is
one more shape every caller would have to specifically handle).

`SearchBiodataQuery` excludes the caller and every inactive profile, is
capped at 50 results (matching member-family-service's own member-search
default), and
takes an `ExcludeOwnGautra` flag — Gautra is always shown, never
auto-excluded by default; the platform does not hard-code a rule about who
may or may not marry whom, it only offers the filter to members who want
gotra-exogamy honoured.

## Events published

| Event | Topic | Raised by |
|---|---|---|
| `BiodataProfileCreatedDomainEvent` | `matrimony.biodata.created.v1` | `BiodataProfile.OptIn` / `.Rejoin` |
| `MatrimonyConsentWithdrawnDomainEvent` | `matrimony.consent.withdrawn.v1` | `BiodataProfile.WithdrawConsent` |
| `ProposalSentDomainEvent` | `matrimony.proposal.sent.v1` | `Proposal.Send` |
| `ProposalRespondedDomainEvent` | `matrimony.proposal.responded.v1` | `Proposal.Respond` |
| `ProposalWithdrawnDomainEvent` | `matrimony.proposal.withdrawn.v1` | `Proposal.Withdraw` |

All five have `audit-notification-service` `KnownEvents.cs` descriptors.
The two biodata events are self-actions and carry no distinct actor,
matching `family.created.v1`'s own shape. All three proposal events name
`ProposalId` as the entity and whichever party actually acted
(`FromMemberId` for send/withdraw, `ToMemberId` for respond) as the actor —
the sender and recipient are two different people, so "who acted" is never
the same as "which row this is about".

`BiodataProfile.Erase` raises no event — it is the *result* of an
already-announced erasure (`identity.user.erased.v1`), not a new fact worth
its own audit row.

## Who may hold a biodata

**Only a member whose My Profile says Unmarried or Divorced (2026-10-24).**
Marital status lives in member-family-service; this service keeps
`MatrimonyEligibility`, a yes/no per member fed by
`members.marital-status.changed.v1`, which carries the eligibility and never
the status itself - matrimony has no use for "Married" beyond "no", and a copy
of the status here would be a second place to erase it from. Opting in and
updating answer 409 `Matrimony.NotEligible` without it; `GetBiodataQuery`
answers 404 for a member who is not eligible, and `SearchBiodataQuery` leaves
them out, so a member who marries drops out of search the moment My Profile
says so. Their own biodata stays readable to them, unedited and unshown.

**No row is not eligible.** A member who has never set a marital status is
`Unknown`, and Unknown is refused like NotEligible - so every biodata created
before this rule is hidden until its owner sets their status. That is
deliberate: the alternative is showing a married member's biodata because the
platform had not asked.

**It is a projection, not a call** - the "no synchronous call to
member-family-service" rule below holds. Kafka can deliver late or twice;
`MatrimonyEligibility.Apply` ignores a change older than the one held, so a
redelivered Married cannot undo a later Divorced. Erasure removes the row with
the biodata.

## Events consumed

**`identity.user.erased.v1`** — a member's full account erasure takes their
biodata and proposals with it, the same obligation member-family-service
already meets for its own data. `EraseMatrimonyDataCommand`, raised by
`IntegrationEventConsumer`, calls `BiodataProfile.Erase` (destructive,
unlike a member-initiated withdrawal) and `Proposal.DeIdentify` on every
proposal the erased member was part of — the row is kept so the *other*
party's own sent/received history is not silently broken, but the message
is stripped and a still-`Pending` proposal is closed out as `Withdrawn`
rather than left dangling forever. Idempotent against Kafka's at-least-once
delivery: nothing found returns success rather than an error, matching
`CreateProfileForNewUserCommandHandler`'s own "unusable payload → log a
warning and succeed, never retry to exhaustion" precedent.

**`members.marital-status.changed.v1`** — `RecordMaritalStatusCommand`
records whether the member may hold a biodata; see "Who may hold a biodata"
above. The consumer is subscribed to exactly these two topics and sends
each to its own command.

No `[InternalRequest]`-gated synchronous endpoint exists anywhere in this
service — the only cross-service data flow is member-portal's client-
supplied opt-in payload, plus these two Kafka topics.

## API endpoints

| Method | Path | Roles |
|---|---|---|
| GET | `/v1/matrimony/consent-notice` | `Members.Read` |
| GET | `/v1/matrimony/biodata/me` | `Members.Read` |
| GET | `/v1/matrimony/eligibility/me` | `Members.Read` |
| POST | `/v1/matrimony/biodata/me/opt-in` | role list above |
| PATCH | `/v1/matrimony/biodata/me` | role list above |
| DELETE | `/v1/matrimony/biodata/me/consent` | role list above |
| GET | `/v1/matrimony/biodata/search` | `Members.Read` |
| GET | `/v1/matrimony/biodata/{memberId}` | `Members.Read` |
| POST | `/v1/matrimony/proposals` | role list above |
| GET | `/v1/matrimony/proposals/sent` | `Members.Read` |
| GET | `/v1/matrimony/proposals/received` | `Members.Read` |
| POST | `/v1/matrimony/proposals/{id}/respond` | role list above, recipient only |
| POST | `/v1/matrimony/proposals/{id}/withdraw` | role list above, sender only |
| GET | `/health` | anonymous |

## Decisions worth knowing before you change this service

**No admin moderation, on purpose.** A member's own biodata goes live the
moment they opt in, the same as any other profile edit. There is no
oversight screen and no dashboard tile in admin-portal for this module —
adding one is a deliberate follow-up, not an oversight in this pass.

**Withdrawal and erasure answer different questions, and conflating them
would break a shared record.** See "Consent" above. Any change here should
ask, for every new state transition: does this affect only the acting
member's own data, or does it also affect a `Proposal` the other party has
a stake in?

**No cross-service HTTP call exists between this service and
member-family-service, in either direction.** The opt-in payload arrives
already assembled from the client, which is the established pattern
admin-portal's Pathshala placement queue already uses for child names.
Resist adding a synchronous call here even if a future field seems to need
one — reach for the client-composition pattern first, and only reconsider
if that genuinely cannot work.

**Domain-assigned keys are `ValueGeneratedNever`.** Both `BiodataProfile.Id`
(the member's own id) and `Proposal.Id` (a fresh `Guid.NewGuid()` from the
factory). This repository has hit the EF default three times already in
other services; see their own `CLAUDE.md`s.

## Dependencies

- **Postgres** `samaajconnect_matrimony` — `ConnectionStrings__Default`
- **Kafka** — `Kafka__BootstrapServers`, producer and consumer
  (`identity.user.erased.v1`, `members.marital-status.changed.v1`)
- **Jwt** — `Jwt__SigningKey`, validation only. This service never mints
  tokens.
- No Redis dependency.

## Testing

- `Sangam.Matrimony.UnitTests` — `BiodataProfile` (consent required, age
  gate, withdrawal deactivates rather than erasing, rejoin, erase) and
  `Proposal` (state transitions, sender/pending/recipient guards,
  de-identification).
- `Sangam.Matrimony.IntegrationTests` — Testcontainers Postgres, no Kafka
  broker (bootstrap address points at an unreachable address on purpose, so
  outbox rows and the erasure consumer can be asserted without racing a real
  one). Covers opt-in round-tripping every field, the consent-required and
  under-18 refusals, search's filters and exclusions, tenant isolation,
  duplicate-pending-proposal refusal, the full accept/decline/withdraw flow,
  and the `identity.user.erased.v1` consumer erasing biodata while
  de-identifying proposals and leaving the other party's own history intact.

```
dotnet test services/matrimony-service/Sangam.Matrimony.sln
```

`scripts/smoke-through-gateway.sh` covers this service through the gateway:
the module toggled on, a consent-notice read, opt-in with and without
consent, search finding an opted-in candidate while excluding the caller, a
proposal sent/received/accepted, a duplicate-pending refusal, withdrawal
taking a profile out of search while its proposal history survives, and the
module toggled off again with every route answering 404 while the
underlying data survives.
