# identity-tenant-service

## Purpose

Source of truth for **who a Samaaj is** and **who a user is**, and the only
service on the platform allowed to issue JWTs. The `tenant_id` claim it puts in
a token is what every other service scopes by - the platform runs on a single
domain and there is no subdomain resolution step (root `CLAUDE.md` §6).

This is also the one service whose primary aggregate is *not* tenant-scoped:
a `Tenant`'s own `Id` **is** the tenant id every other service references.

## Entities

| Entity | Status | Notes |
|---|---|---|
| `Tenant` | built | Platform-level. Not `ITenantScopedEntity` — see below. |
| `User` | built | Tenant-scoped; the first entity here the query filter actually applies to. |
| `Role`, `Permission`, `RolePermission` | built | Seeded by migration from `AuthorizationCatalog`. |
| `UserRole` | built | A null `TenantScope` is the platform-wide grant, i.e. Super Admin. |
| `ConsentRecord` | built | Append-only. One row per decision, per purpose. |

`Tenant` deliberately does not implement `ITenantScopedEntity`. Applying the
global query filter to it would make the gateway's own lookups impossible,
since those happen *before* a tenant is established.

## Commands

| Command | Policy | Status |
|---|---|---|
| `CreateTenantCommand` | `SuperAdmin` + `Tenant.Manage` | built |
| `RegisterMemberCommand` | anonymous | built |
| `LoginCommand` | anonymous | built |
| `ChangeTenantStatusCommand` | `SuperAdmin` + `Tenant.Manage` | built |
| `CreateAccountForConvertedChildCommand` | `[InternalRequest]` | built |
| `IssueActivationCodeCommand` | `SamaajAdmin` + `AdminUsers.Manage` | built |
| `ActivateAccountCommand` | anonymous | built |
| `WithdrawConsentCommand` | any authenticated role | built |
| `SetGrievanceContactCommand` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | built |
| `EraseMyAccountCommand` | any authenticated role, self only | built |
| `AssignRoleCommand` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | not built |

A new Samaaj is created **Inactive**. Creating the record and letting it serve
traffic are two separately audited decisions, so activation is its own command.

## Queries

| Query | Policy | Status |
|---|---|---|
| `GetTenantBySlugQuery` | anonymous | built |
| `GetCurrentUserQuery` | any authenticated role | built |
| `GetTenantByIdQuery` | anonymous | built |
| `ListRegisterableTenantsQuery` | anonymous | built |
| `ListPendingActivationsQuery` | `SamaajAdmin` + `AdminUsers.Manage` | built |
| `GetConsentNoticeQuery` | anonymous | built |
| `GetMyDataQuery` | any authenticated role | built |
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
| `UserActivatedFromChildDomainEvent` | `identity.child-conversion.completed.v1` | `User.Activate` |
| `ConsentRecordedDomainEvent` | `identity.consent.recorded.v1` | `ConsentRecord.Grant` and `.Withdraw` |
| `UserErasedDomainEvent` | `identity.user.erased.v1` | `User.Erase` |

Delivery is at-least-once by design (see `Messaging/OutboxDispatcher.cs`).
Consumers must be idempotent.

## Events consumed

`members.child-conversion.approved.v1`, to create the account behind an
approved adult-child conversion.

This is the only thing it consumes, and the subscription is an explicit topic
list rather than a pattern. This service publishes far more than it reacts to,
and subscribing to anything it has no handler for would mean quietly committing
offsets for messages it did nothing with.

## API endpoints

| Method | Path | Roles |
|---|---|---|
| POST | `/v1/identity/tenants` | `SuperAdmin` + `Tenant.Manage` |
| PATCH | `/v1/identity/tenants/{id}/status` | `SuperAdmin` + `Tenant.Manage` |
| GET | `/v1/identity/tenants/{slug}` | anonymous |
| GET | `/v1/identity/tenants/by-id/{id}` | anonymous (the gateway calls it) |
| GET | `/v1/identity/tenants/directory` | anonymous |
| POST | `/v1/identity/register` | anonymous |
| POST | `/v1/identity/login` | anonymous |
| GET | `/v1/identity/me` | any authenticated role |
| GET | `/v1/identity/activations/pending` | `SamaajAdmin` + `AdminUsers.Manage` |
| POST | `/v1/identity/activations/{userId}/code` | `SamaajAdmin` + `AdminUsers.Manage` |
| POST | `/v1/identity/activations/redeem` | anonymous |
| GET | `/v1/identity/consent-notice` | anonymous |
| POST | `/v1/identity/me/consents/{purpose}/withdraw` | any authenticated role |
| GET | `/v1/identity/me/data-export` | any authenticated role |
| POST | `/v1/identity/me/erase` | any authenticated role |
| PUT | `/v1/identity/tenants/{id}/grievance-contact` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` |
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
and returns an empty `TenantSlug`, since a platform account belongs to no
Samaaj.

**Registration takes a slug, not a tenant id.** A visitor has no token yet, so
there is no claim to read a Samaaj from; they pick one from the public
directory. The slug is resolved server-side against the tenant table, and a
request arriving with a resolved tenant that disagrees is rejected. This is not
the "client-supplied TenantId" that SECURITY-CHECKLIST.md forbids.

**OTP verification is deferred.** The wireframe shows registration continuing
to "Verify Mobile". No notification channel exists yet, and gating login behind
an OTP nobody can send would make the end-to-end path untestable. Registration
therefore creates an active account with `IsContactVerified = false`, and
publishes `UserRegistered`. Close this when a delivery channel lands.

**A converted child's account is created without a password.** Approving a
conversion establishes that this person is *entitled* to an account; nobody has
yet proved they are the one asking for it. The account sits in
`PendingActivation` - unsignable-into - until an activation code is redeemed.

**Activation codes are stored as a hash and shown once.** With no notification
channel, the plaintext is returned to the issuing admin in the response and
passed on in person, which for a community organisation is realistic and
involves no channel that can be intercepted. Storing the hash means a database
copy is not a set of working credentials. A lost code is re-issued, never
looked up, and re-issuing kills the previous one.

**Every way of failing activation returns one identical response.** "No such
account", "already activated" and "wrong code" are indistinguishable, or someone
with a list of identifiers could sort it into those mid-conversion and those
not. Five wrong guesses kill the code - not the account - and an admin issues a
new one.

**Wrong activation guesses are counted through `IFailedActivationRecorder`.**
Same reason as `IFailedLoginRecorder`: the command returns a failure,
`TransactionBehavior` rolls it back, and a counter on the tracked aggregate
would be rolled back with it - leaving the code guessable without limit.

## Consent and the DPDP Act

Full mapping in `docs/product/DPDP-COMPLIANCE.md`. What matters when changing
this service:

**Consent records are append-only.** Granting and withdrawing each write a row;
nothing is updated in place. Section 6(7) requires a Data Fiduciary to be able
to produce the consent it relied on, which a mutable record cannot do. Current
state is derived from the latest row per purpose, never stored.

**Every record carries the notice version in force when it was made.** Bump
`ConsentNotice.CurrentVersion` whenever the wording changes in substance, so an
old record still says what that person was actually shown.

**Purposes are separate, and the required list is short.** Section 6 wants
consent to be specific, so bundling "run your membership" with "send you news"
into one tick would make neither valid. Consent conditional on service is only
valid where the service genuinely cannot be given without it - hence only
`Membership` is required.

**Withdrawal is one call, with no reason field and no admin in the way**
(section 6(4): as easy as giving). The required purpose is the exception: it
cannot be withdrawn piecemeal, and the error says to ask for erasure instead.

**The data export never contains the password hash.** A credential is data
about a person only in the sense that a lock is about a key; exporting it in the
name of transparency would be a way of handing one out. There is a test.

**The grievance contact is separate from the general contact, and public.**
Section 13 requires a published means of grievance redressal. Reusing
`ContactPerson` would make it impossible to tell whether a Samaaj has actually
named one, and a contact only members can see is not published. A Samaaj Admin
can set their own Samaaj's, because routing every change through the platform
operator would make it stale by design.

**The export is per-service and says so.** A member's data is spread across
three services, and having one reach synchronously into the others would undo
the service boundaries for a feature used a handful of times a year. The
response names what it does not cover.


**Erasure is the member's own call, and the password is the gate.** Section 12
gives the Data Principal a right, not a request for permission, so no admin
approves it - unlike adult-child conversion, where an admin decides whether to
*create* something. The password proves the person at the keyboard is the
account holder, which is the identity check needed before something with no
undo, and it stops a mis-click being enough.

**A Super Admin cannot erase through this route.** Nothing but the bootstrap on
an empty database can recreate one, and there is no second Super Admin to
notice the platform has become unadministrable.

**Erasure frees the identifier.** `MobileOrEmail` is unique platform-wide, so
keeping it would mean someone who left could never come back - a penalty for
exercising a right rather than a consequence of it. It is replaced with a
per-account value at an unroutable domain, which satisfies the uniqueness
constraint without leaving anything to sign in as.

**The event carries two ids and nothing else.** It travels to
audit-notification-service, which records every payload verbatim into an
append-only table, so a name on it would land somewhere deliberately impossible
to redact. `User.Erase` and the outbox row commit together, so an erasure that
succeeds here is always announced to the other two services.

## Dependencies

- **Postgres** `samaajconnect_identity` — `ConnectionStrings__Default`
- **Kafka** — `Kafka__BootstrapServers`, publishing and, since adult-child
  conversion, consuming
- **Jwt** — `Jwt__SigningKey` (≥32 chars) is validated at startup and the
  service refuses to boot without it. Development supplies one from
  `appsettings.Development.json`; every other environment must supply it
  from a secret store.
- No Redis dependency. The gateway, not this service, caches tenant lookups.
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

`scripts/smoke-through-gateway.sh` covers this service through the gateway
(CLAUDE.md §9), including the whole adult-child conversion loop across three
services and two Kafka topics.
