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
| `RefreshToken` | built | A session step. Hashed, single-use, rotating. Deliberately not tenant-scoped. |

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
| `RefreshSessionCommand` | anonymous (the refresh token is the credential) | built |
| `SignOutCommand` | anonymous (same) | built |
| `AssignRoleCommand` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | built |
| `InviteAdminCommand` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | built |
| `SetTenantModulesCommand` | `SuperAdmin` + `Tenant.Manage` | built |

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
| `ListTenantsQuery` | `SuperAdmin` + `Tenant.Manage` | built |
| `ListAdminUsersQuery` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | built |
| `ListRolesQuery` | any authenticated role | built |

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
| `TenantModulesChangedDomainEvent` | `identity.tenant.modules-changed.v1` | `Tenant.SetEnabledModules` |
| `AdminInvitedDomainEvent` | `identity.admin.invited.v1` | `User.Invite` |
| `UserRoleGrantedDomainEvent` | `identity.user.role-granted.v1` | `User.GrantRole` |
| `UserRoleRevokedDomainEvent` | `identity.user.role-revoked.v1` | `User.RevokeRole` |
| (no aggregate) | `identity.member-data.exported.v1` | `DataExportRecorder` |

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
| GET | `/v1/identity/tenants` | `SuperAdmin` + `Tenant.Manage` |
| GET | `/v1/identity/tenants/modules` | anonymous |
| PUT | `/v1/identity/tenants/{id}/modules` | `SuperAdmin` + `Tenant.Manage` |
| POST | `/v1/identity/register` | anonymous |
| POST | `/v1/identity/login` | anonymous |
| POST | `/v1/identity/token/refresh` | anonymous |
| POST | `/v1/identity/logout` | anonymous |
| GET | `/v1/identity/me` | any authenticated role |
| GET | `/v1/identity/activations/pending` | `SamaajAdmin` + `AdminUsers.Manage` |
| POST | `/v1/identity/activations/{userId}/code` | `SamaajAdmin` + `AdminUsers.Manage` |
| POST | `/v1/identity/activations/redeem` | anonymous |
| GET | `/v1/identity/consent-notice` | anonymous |
| POST | `/v1/identity/me/consents/{purpose}/withdraw` | any authenticated role |
| GET | `/v1/identity/roles` | any authenticated role |
| GET | `/v1/identity/admins` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` |
| POST | `/v1/identity/admins` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` |
| PUT | `/v1/identity/admins/{userId}/roles/{role}` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` |
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


## Sessions

A sign-in produces two things, and they are opposites on purpose.

The **access token** is a JWT. Every service validates it without calling here,
which is what makes the platform's authorization cheap - and what makes the
token impossible to withdraw. It lasts **15 minutes**, and that number *is* the
security property: it is exactly how long a revoked role, a suspended account or
a stolen token keeps working.

The **refresh token** is a row in `refresh_tokens`. Rows can be revoked. It is
stored as a hash, lasts 14 days, and is what decides whether a session
continues.

**Refresh tokens are single-use and rotate.** Redeeming one issues a
replacement in the same `SessionId` chain and marks the old one used. That is
what makes theft detectable rather than merely possible: a token that never
changed would work forever and look exactly like the real one.

**A reused token is treated as theft, bluntly.** Presenting an already-redeemed
token means two parties hold it and one is not the member. Nothing can tell
which, so the entire chain is revoked and both are made to sign in again. A
member who hits this is inconvenienced; an attacker loses access. A run of
`ReuseDetected` on one account is the closest thing this platform has to an
intrusion signal - treat it as an incident.

**That revocation is written on its own connection.** Refreshing is a command,
so `TransactionBehavior` rolls it back when the handler returns a failure - and
detecting a stolen token *is* a failure. Revoking on the ambient context would
be undone by the very request that found the theft, leaving the attacker live
and a log line claiming otherwise. `SessionService.RevokeSessionOutOfBandAsync`
is the same shape, and exists for the same reason, as `IFailedLoginRecorder`.

**Refreshing re-reads the account, its Samaaj and its roles.** They are not
carried through the session. This is what makes suspending an account,
deactivating a Samaaj or revoking a role bite within one access token's lifetime
rather than at the next sign-in - and it is why the refresh path, not the login
path, is where those checks matter most.

**Every refusal answers the same way.** "No such token", "already used",
"expired" and "your Samaaj was deactivated" are one 401 saying *please sign in
again*. The reason is logged. Telling a caller which of those applied tells
whoever holds a stolen token what to try next.

**Signing out is anonymous, and that is deliberate.** The refresh token is the
credential. Requiring a valid access token would mean the moment you most want
to end a session - an expired token, a device you want to abandon - is the
moment you cannot. Presenting a token only ever destroys the presenter's own
session, and signing out twice looks exactly like signing out once, because a
count that distinguished them would say which tokens exist.

**`RefreshToken` is not an `ITenantScopedEntity`** despite carrying a tenant id.
A caller redeeming one has no access token and so no resolved tenant; a query
filter would compare against `Guid.Empty` and turn every refresh into a
sign-out. Nothing is lost: the only way to find a row is to present its 256-bit
secret.

**The hash is deterministic, unlike a password's.** The lookup *is* by hash, so
a per-value salt would make it impossible. Dropping the salt is safe here and
would not be for a password: a salt defends against precomputation over a
guessable space, and the input is 256 bits of randomness. That is also why it is
fast - slowness buys nothing against an input nobody can enumerate. Never pass
anything a human typed to `HashDeterministic`.

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

**Step-up is shared, and a failed one is 403.** `IStepUpAuthentication` is the
one place that re-asks for a password, used by erasure and by deactivating or
archiving a Samaaj. Two things about it are load-bearing.

It reads the account with `GetSelfAsync`, past the tenant query filter, because
a Super Admin's own account lives at `PlatformTenantId` while they act on a
Samaaj — a tenant-filtered read finds nothing and fails the step-up for the one
role that most needs it. This is the same trap that made `/me` answer 404 for an
overriding Super Admin.

And it fails with `ErrorType.Forbidden`, never `Unauthorized`. Both portals'
HTTP interceptor treats a 401 as an expired access token: it renews the token
and *retries the original request*. On a step-up endpoint that means the
destructive command is submitted a second time because somebody mistyped. 403 is
also the truer answer — the caller is authenticated, they simply have not proven
enough — and carries no `WWW-Authenticate` obligation, which a 401 does and this
never had. Erasure returned 401 until this was noticed.

**Activating a Samaaj deliberately does not ask.** The requirement is decided by
the target status: `Inactive` and `Archived` need the password, `Active` does
not. Deactivating signs out every member at their next refresh and archiving
cannot be undone at all; activating restores service and is undone by the very
call that undid it. A step-up on the harmless direction only teaches people to
type their password without reading the screen. The rule reads the target status
alone rather than whether the change would actually do anything, so the same call
never sometimes needs a password and sometimes not.

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


## The admin surface

What the admin portal's Samaaj/Tenants, Admin Users & Roles, and Role Matrix
screens call. `docs/product/wireframes/admin-panel-wireframes.html` is the spec.

**Module keys are a closed list, not free text.** `ModuleCatalog` in the domain
is the only set of values `Tenant.EnabledModules` may hold, and both the create
and the set-modules validators refuse anything else. The reason is what an
unrecognised value does: the gateway gates a route by looking for its module key
in that collection and answers 404 when it is absent, so a Samaaj created with
"pathshaala" would have its Pathshala routes disappear with nothing logged
anywhere - and a typo and a deliberate switch-off would look identical. The
catalogue also carries the label the admin portal shows, so the two cannot drift
into disagreeing about what a key means.

Keys are canonicalised rather than rejected on casing. The gateway compares them
case-insensitively, so refusing "Pathshala" would refuse a request that would
have worked, while storing it verbatim would leave two spellings of one module
for the next comparison to disagree about.

**Nothing consumes `identity.tenant.modules-changed.v1`.** The gateway re-reads
a Samaaj when its own 60-second cache expires, so a module change takes effect
within a minute with no consumer to keep in step. The event exists because
switching a module off makes a whole area of the platform answer 404 for
everyone in that Samaaj, which is a decision worth having in the audit log.

**The role matrix is read-only, and that is a decision rather than an
omission.** The wireframe's Role & Permission Matrix screen says "this screen
edits it, not just displays it". Every command and query on this platform
declares the roles and permissions it requires as a compiled-in attribute, so a
runtime-editable matrix would split the answer to "who may approve a
conversion?" between source control and a table someone changed on a Tuesday,
with neither half reviewable against the other. It is also platform-wide: a
Samaaj Admin editing it would be editing what a Samaaj Admin means everywhere.

Making it editable is a real requirement and its own piece of work - it needs
per-tenant role definitions, an audit trail of matrix changes, and a floor of
permissions no edit may remove or the platform locks itself out. Until then
`GET /v1/identity/roles` reports what the pipeline actually enforces, reading
straight from `AuthorizationCatalog` rather than the database so it cannot
report a matrix that has drifted, and says `editable: false` so a screen does
not have to assume.

**Which modules a Samaaj runs is a Super Admin decision; who administers it is
the Samaaj Admin's.** Modules are what the gateway routes on, and switching one
off makes a whole area of the platform answer 404 for everybody in that Samaaj.
A Samaaj Admin manages people and content inside the modules they have.

**Not every role can be handed out.** `AuthorizationCatalog.AdminAssignableRoleIds`
is the list, and both the matrix and the assign-role command read it, so a role
can never be assignable in one and not the other. `SuperAdmin` is missing
because its only route is the bootstrap on an empty database, so granting it can
never be one compromised admin account away. `Member`, `FamilyHead` and
`PathshalaStudent` are missing because they are earned - by registering, by
creating a household, by enrolling - and granting one from a screen would write
a grant with no account behind it.

**An admin cannot remove their own Samaaj Admin role.** In a Samaaj with one
administrator that locks everybody out of the screen they are standing on.
Another admin can still do it, which makes it take two people rather than one
mis-click.

**Inviting creates the account and issues the code in one command.** An
invitation that created the account and then failed to issue a code would leave
an account nobody can reach and no obvious way to tell that is what happened.
The invited roles are granted up front: they cannot be used until the account
can be signed into, and granting them at invitation time means the decision has
one audit trail rather than a grant someone must remember to make later. The
account is `PendingActivation` with no password, exactly like a converted
child's, because inviting someone establishes they are entitled to an account
and nobody has yet proved they are the person asking for it.

**An identifier already on the platform is refused without saying where.**
`MobileOrEmail` is unique platform-wide, so the existing account may be in
another Samaaj; naming it would confirm an identifier is on the platform to
anyone holding an admin account anywhere. Adding a role to an existing account
is what `AssignRoleCommand` is for.

**`GetByIdAsync` includes the role grants, and that Include is load-bearing.**
Every write path reaching a `User` through it reasons about roles - granting one
checks for a duplicate, revoking one looks for the grant, erasure clears them
all. With the collection unloaded each of those operates on an empty list and
reports success having done nothing; erasure was leaving live role rows behind
until this was fixed. There is a regression test.

**`UserRole.Id` is `ValueGeneratedNever`.** The aggregate assigns it. Left as
EF's default, a grant added to a *tracked* `User` comes back Modified rather
than Added and the save fails against a row that was never there. This is the
same trap member-family-service's `CLAUDE.md` records for `Family` and
`FamilyMember`; it stayed hidden here until `GrantRole` started adding a role to
a `User` that was already loaded. Apply it to any domain-assigned key.

**A cross-tenant write is refused at `SaveChanges`, not only by the
handler.** `TenantWriteGuard` compares every added or modified
`ITenantScopedEntity` against `ITenantContext.TenantId` and throws when they
disagree. Handlers still do their own check - a 404 saying no such row exists in
this Samaaj is a far better answer than an exception - but a handler that
forgets one looks exactly like one that does not need it, so the rule is also
enforced where it cannot be skipped. The guard is silent when no tenant is
resolved, because consumers legitimately have none.

**A data export is announced, and the announcement carries no data.** An export
hands out a complete copy of a person's data, which makes it more worth
recording than most of what already is (SECURITY-CHECKLIST.md) - and until this
it was the one operation leaving no trace at all. The event carries ids and a
timestamp only: putting the export's contents into an append-only audit table
would make the record of the copy a second copy.

`DataExportRecorder` writes on its own scope, like `IFailedLoginRecorder`, for
a related reason: the export is a *query*, so no transaction is open and no unit
of work will be committed on its behalf. It also swallows its own failures. A
member's right to a copy of their data does not depend on our bookkeeping
succeeding.

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
