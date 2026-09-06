# master-data-service

## Purpose

Owns the platform's shared reference data: Country, City, Locality,
ProfessionType, Gender, MaritalStatus — and, per Samaaj, its own Gautra list.
Every registration form, member profile, and admin dropdown on the platform
that needs one of these values reads it from here rather than hardcoding it.

Platform infrastructure, like identity-tenant-service and
audit-notification-service — never module-gated. A Samaaj cannot switch off
the Country/Gender/Gautra dropdowns its own registration form depends on.

The eleventh service. See root `CLAUDE.md` §2 and `docs/product/SERVICES.md`
for where it sits among the other ten.

## Entities

| Entity | Notes |
|---|---|
| `LookupItem` | One flat, admin-managed reference list, discriminated by `Category`: `Country`, `ProfessionType`, `Gender`, or `MaritalStatus`. Platform-wide, not tenant-scoped |
| `City` | Scoped to a country (`LookupItem.CountryId`, where `Category == Country`). Own aggregate because it carries a parent id the flat `LookupItem` shape has none for |
| `Locality` | Scoped to a city. The "locations master" — this is the member-portal wireframe's existing locality dropdown (Hiran Magri, Surajpole, Shobhagpura) |
| `Gautra` | The one **tenant-scoped** entity here (`ITenantScopedEntity`). A Samaaj's own Gautra list, per root CLAUDE.md §6's "Samaaj-wise" requirement |

**Why one `LookupItem` table for four categories, not four aggregates.**
Country, ProfessionType, Gender and MaritalStatus are structurally identical —
a code, a label, a sort position, an active flag — and nothing on the platform
reasons about them differently from one to the next. Four separate CRUD
stacks would be four copies of the same file with the words changed — not a
handful of similar lines worth leaving alone, but the same table, four times. `City`, `Locality` and `Gautra` are deliberately kept
out of it: the first two carry a parent id this flat shape has no column for,
and folding a tenant id into it to accommodate `Gautra` would put every read
of every global lookup through the tenant query filter for data that belongs
to no tenant at all.

Every write is a soft delete (`IsActive = false`), never a hard delete. Other
services hold these rows' ids as plain foreign keys across a service boundary
with no referential integrity to enforce it — a hard delete would leave those
references dangling with nothing to say so.

## Commands

| Command | Permission | Notes |
|---|---|---|
| `CreateLookupItemCommand` | `MasterData.Manage` | Refuses a duplicate `(Category, Code)` pair |
| `UpdateLookupItemCommand` | `MasterData.Manage` | Renames/reorders. Code and category are fixed once created |
| `DeactivateLookupItemCommand` | `MasterData.Manage` | Soft delete |
| `CreateCityCommand` | `MasterData.Manage` | Refuses an unknown or non-`Country` `CountryId` |
| `UpdateCityCommand` | `MasterData.Manage` | |
| `DeactivateCityCommand` | `MasterData.Manage` | Soft delete |
| `CreateLocalityCommand` | `MasterData.Manage` | Refuses an unknown `CityId` |
| `UpdateLocalityCommand` | `MasterData.Manage` | |
| `DeactivateLocalityCommand` | `MasterData.Manage` | Soft delete |
| `CreateGautraCommand` | `Gautra.Manage` | Adds to the caller's own Samaaj. Refuses a Super Admin with no Samaaj chosen (`Gautra.NoSamaaj`) rather than throwing |
| `UpdateGautraCommand` | `Gautra.Manage` | Tenant-filtered read, per root CLAUDE.md §6 |
| `DeactivateGautraCommand` | `Gautra.Manage` | Soft delete |

## Queries

| Query | Permission | Notes |
|---|---|---|
| `ListLookupItemsQuery` | anonymous | One category, active-first by sort order — this is exactly the shape a registration-form dropdown or a startup cache warmup wants |
| `GetLookupItemByIdQuery` | anonymous | |
| `ListCitiesQuery` | anonymous | Optionally narrowed to one country |
| `GetCityByIdQuery` | anonymous | |
| `ListLocalitiesQuery` | anonymous | Optionally narrowed to one city |
| `GetLocalityByIdQuery` | anonymous | |
| `ListGautrasQuery` | anonymous | This Samaaj's own list, resolved from `X-Tenant-Id` — see "Anonymous reads" below |
| `GetGautraByIdQuery` | anonymous | |

**Every read here is anonymous, including Gautra's.** A member picks their
Gender, Profession, City, and Gautra during registration — after choosing a
Samaaj from the directory, but before they hold a token. That is the same
"anonymous by necessity" shape identity-tenant-service's own directory and
Samaaj-lookup queries already have, applied to every field a registration
form's dropdowns need. `Gautra` reads still isolate correctly: with no
token, `ITenantContext.TenantId` comes from the gateway's `X-Tenant-Id`
header alone, exactly like every other anonymous, tenant-aware request on the
platform.

## Events published

Twelve topics, three per aggregate (`Created`/`Updated`/`Deactivated`):
`master-data.lookup-item.*.v1`, `master-data.city.*.v1`,
`master-data.locality.*.v1`, `master-data.gautra.*.v1`.

**`IDomainEvent.TenantId` is `Guid.Empty` on every `LookupItem`, `City` and
`Locality` event.** That is the same sentinel `PlatformClaimTypes.TenantId`
already uses for a platform account's token — these three aggregates belong
to the platform, not to any one Samaaj, so there is no real tenant id to put
on their events. Only `Gautra`'s three events carry a real `TenantId`.

All twelve have `audit-notification-service` `KnownEvents.cs` descriptors.
None carries an actor distinct from the row itself: every change here is
maintained by whichever administrator happened to be signed in, not a
decision made *about* somebody else the way `IssueStatusChangedDomainEvent`
or `PostModeratedDomainEvent` are elsewhere on the platform.

## Events consumed

None. This service is the source of truth for master data; it holds no copy
of anything another service owns.

## API endpoints

All under `/v1/master-data/**`. See `docs/product/API-CONTRACTS.md`.

## Dependencies

- PostgreSQL — `samaajconnect_master_data`
- Kafka via the Outbox (`OutboxDispatcher`), publishing only
- No Redis dependency on this service's own side. (Consuming services are
  expected to cache what they read from here in their own Redis — see
  `docs/product/ARCHITECTURE.md` for the cache-warmup-plus-Kafka-invalidation
  pattern that rollout follows.)

## Testing

- `Sangam.MasterData.UnitTests` — the domain rules: a lookup item's event
  carries the platform-wide `Guid.Empty` sentinel, a Gautra's carries a real
  tenant id, deactivating twice is a no-op the second time.
- `Sangam.MasterData.IntegrationTests` — Testcontainers Postgres.
  `GautraTenantIsolationTests` proves the global tenant query filter actually
  isolates one Samaaj's Gautra list from another's, both on reads and on a
  cross-tenant write attempt (refused as 404, indistinguishable from a real
  deletion — the same shape root CLAUDE.md §6's IDOR guard uses everywhere
  else). `LookupItemOutboxTests` proves the Outbox row lands in the same
  transaction as the state change, and that the `(Category, Code)` uniqueness
  constraint is scoped per category rather than global — `"M"` means one thing
  among countries and a different thing among genders.
- `scripts/smoke-through-gateway.sh` exercises all four aggregates through
  YARP: a SuperAdmin manages the platform-wide lookups, a Samaaj Admin manages
  their own Gautra list, a member's anonymous read sees both, and a member
  attempting a write is refused.

```
dotnet test services/master-data-service/Sangam.MasterData.sln
```
