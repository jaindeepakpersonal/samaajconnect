# identity-tenant-service

## Purpose

Source of truth for **who a Samaaj is** and **who a user is**. Resolves a
subdomain slug to a `TenantId` for the gateway, and is the only service on
the platform allowed to issue JWTs.

This is also the one service whose primary aggregate is *not* tenant-scoped:
a `Tenant`'s own `Id` **is** the tenant id every other service references.

## Entities

| Entity | Status | Notes |
|---|---|---|
| `Tenant` | built | Platform-level. Not `ITenantScopedEntity` — see below. |
| `User` | not built | Next: registration + login. |
| `Role`, `Permission`, `UserRole`, `RolePermission` | not built | Seeded reference data (DATA-MODEL.md §2). |

`Tenant` deliberately does not implement `ITenantScopedEntity`. Applying the
global query filter to it would make anonymous slug resolution impossible,
since that lookup happens *before* any tenant is known. The reflection-based
filter in `IdentityTenantDbContext` is already written and will pick up `User`
and the rest the moment they land.

## Commands

| Command | Policy | Status |
|---|---|---|
| `CreateTenantCommand` | `SuperAdmin` + `Tenant.Manage` | built |
| `ActivateTenantCommand` | `SuperAdmin` + `Tenant.Manage` | not built |
| `RegisterMemberCommand` | anonymous | not built |
| `LoginCommand` | anonymous | not built |
| `AssignRoleCommand` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | not built |

A new Samaaj is created **Inactive**. Creating the record and letting it serve
traffic are two separately audited decisions, so activation is its own command.

## Queries

| Query | Policy | Status |
|---|---|---|
| `GetTenantBySlugQuery` | anonymous | built |
| `GetCurrentUserQuery` | authenticated | not built |
| `ListTenantsQuery` | `SuperAdmin` | not built |

`GetTenantBySlugQuery` returns `TenantSummaryResponse`, not `TenantResponse`.
The endpoint is reachable without a JWT, so it must not hand an anonymous
caller a harvestable directory of Samaaj contact addresses. An `Archived`
tenant is reported as 404 rather than as a distinct state, for the same reason.

## Events published

| Event | Topic | Raised by |
|---|---|---|
| `TenantCreatedDomainEvent` | `identity.tenant.created.v1` | `Tenant.Create` |

Delivery is at-least-once by design (see `Messaging/OutboxDispatcher.cs`).
Consumers must be idempotent.

## Events consumed

None. This service is the source of tenant truth.

## API endpoints

| Method | Path | Roles |
|---|---|---|
| POST | `/v1/identity/tenants` | `SuperAdmin` + `Tenant.Manage` |
| GET | `/v1/identity/tenants/{slug}` | anonymous |
| GET | `/health` | anonymous |

Paths are absolute, not gateway-relative, so the same URL works whether you
curl the service directly or go through YARP.

## Authorization

`TenantAuthorizationBehavior` reads `[RequiresRoles]`, `[RequiresPermission]`,
and `[AllowAnonymousRequest]` off the request type. **A request carrying none
of them is denied**, not allowed — forgetting to annotate a new command
surfaces as a 403 in its first test, rather than as an unguarded endpoint in
production. Every new command or query needs one of the three.

## Dependencies

- **Postgres** `samaajconnect_identity` — `ConnectionStrings__Default`
- **Kafka** — `Kafka__BootstrapServers`, outbound only
- **Jwt** — `Jwt__SigningKey` (≥32 chars) is validated at startup and the
  service refuses to boot without it. Development supplies one from
  `appsettings.Development.json`; every other environment must supply it
  from a secret store.
- No Redis dependency yet. The gateway, not this service, caches slug lookups.

## Testing

- `Sangam.IdentityTenant.UnitTests` — aggregate rules, the handler, the
  validator, and all five pipeline behaviors. No I/O.
- `Sangam.IdentityTenant.IntegrationTests` — Testcontainers Postgres hosting
  the real `Program.cs`. Only the Kafka producer is faked: the Outbox
  guarantee is transactional, so proving it needs a real database but not a
  real broker.

Run everything:

```
dotnet test services/identity-tenant-service/Sangam.IdentityTenant.sln
```

Still missing: a test that curls this service **through the gateway**
(CLAUDE.md §9). The gateway does not exist yet; add it in the same change
that adds the YARP route.
