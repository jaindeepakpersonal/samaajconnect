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
| `User` | built | Tenant-scoped; the first entity here the query filter actually applies to. |
| `Role`, `Permission`, `RolePermission` | built | Seeded by migration from `AuthorizationCatalog`. |
| `UserRole` | built | A null `TenantScope` is the platform-wide grant, i.e. Super Admin. |

`Tenant` deliberately does not implement `ITenantScopedEntity`. Applying the
global query filter to it would make anonymous slug resolution impossible,
since that lookup happens *before* any tenant is known. The reflection-based
filter in `IdentityTenantDbContext` is already written and will pick up `User`
and the rest the moment they land.

## Commands

| Command | Policy | Status |
|---|---|---|
| `CreateTenantCommand` | `SuperAdmin` + `Tenant.Manage` | built |
| `ChangeTenantStatusCommand` | `SuperAdmin` + `Tenant.Manage` | built |
| `RegisterMemberCommand` | anonymous | built |
| `LoginCommand` | anonymous | built |
| `AssignRoleCommand` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | not built |

A new Samaaj is created **Inactive**. Creating the record and letting it serve
traffic are two separately audited decisions, so activation is its own command.

## Queries

| Query | Policy | Status |
|---|---|---|
| `GetTenantBySlugQuery` | anonymous | built |
| `GetCurrentUserQuery` | any authenticated role | built |
| `ListTenantsQuery` | `SuperAdmin` | not built |

`GetTenantBySlugQuery` returns `TenantSummaryResponse`, not `TenantResponse`.
The endpoint is reachable without a JWT, so it must not hand an anonymous
caller a harvestable directory of Samaaj contact addresses. An `Archived`
tenant is reported as 404 rather than as a distinct state, for the same reason.

## Events published

| Event | Topic | Raised by |
|---|---|---|
| `TenantCreatedDomainEvent` | `identity.tenant.created.v1` | `Tenant.Create` |
| `TenantStatusChangedDomainEvent` | `identity.tenant.status-changed.v1` | `Tenant.ChangeStatus` |
| `UserRegisteredDomainEvent` | `identity.user.registered.v1` | `User.Register` |
| `UserLoggedInDomainEvent` | `identity.user.logged-in.v1` | `User.RecordSuccessfulLogin` |

Delivery is at-least-once by design (see `Messaging/OutboxDispatcher.cs`).
Consumers must be idempotent.

## Events consumed

None. This service is the source of tenant truth.

## API endpoints

| Method | Path | Roles |
|---|---|---|
| POST | `/v1/identity/tenants` | `SuperAdmin` + `Tenant.Manage` |
| PATCH | `/v1/identity/tenants/{id}/status` | `SuperAdmin` + `Tenant.Manage` |
| GET | `/v1/identity/tenants/{slug}` | anonymous |
| POST | `/v1/identity/register` | anonymous |
| POST | `/v1/identity/login` | anonymous |
| GET | `/v1/identity/me` | any authenticated role |
| GET | `/health` | anonymous |

Paths are absolute, not gateway-relative, so the same URL works whether you
curl the service directly or go through YARP.

## Authorization

`TenantAuthorizationBehavior` reads `[RequiresRoles]`, `[RequiresPermission]`,
and `[AllowAnonymousRequest]` off the request type. **A request carrying none
of them is denied**, not allowed — forgetting to annotate a new command
surfaces as a 403 in its first test, rather than as an unguarded endpoint in
production. Every new command or query needs one of the three.

## Decisions worth knowing before you change this service

Each of these looks arbitrary until you hit the problem behind it.

**`MobileOrEmail` is unique platform-wide, not per tenant.** DATA-MODEL.md
section 2 says per tenant. The member-portal wireframe says a member joins one
Samaaj and that a common login "automatically routes you to your registered
Samaaj". Auto-routing is impossible if one identifier can resolve to two
accounts, so the stronger constraint wins. Per CLAUDE.md section 7 the
wireframe is the spec.

**Failed login attempts are written through `IFailedLoginRecorder`, not the
tracked aggregate.** `LoginCommand` is a command, so `TransactionBehavior`
rolls it back whenever it returns a failure - which is exactly what a wrong
password returns. A counter incremented on the aggregate would be rolled back
with it and the lockout would never fire. The recorder uses its own scope and
connection so the increment survives.

**The tenant comes from the token claim, not the header, for authenticated
requests.** `X-Tenant-Id` is unsigned. A member holding a valid token for one
Samaaj could otherwise point it at another simply by setting a header. When
both are present and disagree, the request is refused rather than resolved
either way.

**A Super Admin has `TenantId == Guid.Empty`** (`User.PlatformTenantId`). It is
a sentinel rather than a nullable column so the tenant query filter needs no
special case. `LoginCommand` skips the Samaaj status check for such an account
and returns an empty `TenantSlug`, since there is no subdomain to redirect to.

**Registration takes a slug, not a tenant id.** Registration happens on the
apex domain where no subdomain has been resolved, so the Samaaj comes from the
form. The slug is resolved server-side against the tenant table exactly as the
gateway would, and a request that arrives with a resolved tenant that disagrees
is rejected. This is not the "client-supplied TenantId" that
SECURITY-CHECKLIST.md forbids.

**OTP verification is deferred.** The wireframe shows registration continuing
to "Verify Mobile". No notification channel exists yet, and gating login behind
an OTP nobody can send would make the Stage 0 end-to-end path untestable.
Registration therefore creates an active account with
`IsContactVerified = false`, and publishes `UserRegistered` for
`audit-notification-service` to pick up. Close this when that service lands.

## Dependencies

- **Postgres** `samaajconnect_identity` — `ConnectionStrings__Default`
- **Kafka** — `Kafka__BootstrapServers`, outbound only
- **Jwt** — `Jwt__SigningKey` (≥32 chars) is validated at startup and the
  service refuses to boot without it. Development supplies one from
  `appsettings.Development.json`; every other environment must supply it
  from a secret store.
- No Redis dependency yet. The gateway, not this service, caches slug lookups.
- **Bootstrap** - `Bootstrap__SuperAdminIdentifier` and
  `Bootstrap__SuperAdminPassword` create the first Super Admin on an empty
  database. Without it a fresh deployment cannot create a Samaaj, because
  doing so needs a Super Admin that nothing can create. It is a no-op once any
  Super Admin exists and never rewrites an existing account, so it cannot be
  used to reset a forgotten password by editing configuration. Leave the
  identifier empty to disable it.

## Testing

- `Sangam.IdentityTenant.UnitTests` — aggregate rules, handlers, validators,
  all five pipeline behaviors, and the password hasher. No I/O.
- `Sangam.IdentityTenant.IntegrationTests` — Testcontainers Postgres hosting
  the real `Program.cs`. Only the Kafka producer is faked: the Outbox
  guarantee is transactional, so proving it needs a real database but not a
  real broker.

Run everything:

```
dotnet test services/identity-tenant-service/Sangam.IdentityTenant.sln
```

`PlatformBootstrapTests` walks the whole Stage 0 backend path with no
hand-minted tokens: bootstrap a Super Admin, sign in, create and activate a
Samaaj, register a member, sign in as them.

Still missing: a test that curls this service **through the gateway**
(CLAUDE.md §9). The gateway does not exist yet; add it in the same change
that adds the YARP route.
