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
| `UserRole` | built | A null `TenantScope` is the platform-wide grant, i.e. Super Admin. `LocalityId` is set only on an `AreaAdmin` grant - see "Area Admin" below. |
| `ConsentRecord` | built | Append-only. One row per decision, per purpose. |
| `RefreshToken` | built | A session step. Hashed, single-use, rotating. Deliberately not tenant-scoped. |
| `TenantImage` | built | A Samaaj's own hosted image - its logo or its banner (`TenantLogo` until 2026-10-25). Not tenant-scoped, for the reason `Tenant` is not. |
| `TenantSecurityPolicy` | built | A Samaaj's own overrides to the platform's password/OTP/lockout/session constants. Sparse — no row means no overrides, not zero. |
| `LoginChallenge` | built | Owned by `User`: a sign-in held open between a correct password and its code. Hashed, and as short-lived as the code. |
| `TrustedDevice` | built | A browser excused the sign-in code for 30 days. Hashed token, revoked with the account's sessions. Not tenant-scoped, like `RefreshToken`. |
| `CaptchaChallenge` | built | One sign-in CAPTCHA: its answer hashed with its id, single use, five minutes. Not tenant-scoped - it is answered before anyone has said who they are. |

`Tenant` deliberately does not implement `ITenantScopedEntity`. Applying the
global query filter to it would make the gateway's own lookups impossible,
since those happen *before* a tenant is established.

## Commands

| Command | Policy | Status |
|---|---|---|
| `CreateTenantCommand` | `SuperAdmin` + `Tenant.Manage` | built |
| `RegisterMemberCommand` | anonymous | built |
| `LoginCommand` | anonymous | built. Step one: CAPTCHA and password; a code follows on an untrusted device |
| `IssueCaptchaCommand` | anonymous | built |
| `VerifySignInCodeCommand` | anonymous (the challenge is the credential) | built |
| `ResendSignInCodeCommand` | anonymous (same) | built |
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
| `SetUserSuspensionCommand` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | built |
| `InviteAdminCommand` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | built |
| `SetTenantModulesCommand` | `SuperAdmin` + `Tenant.Manage` | built |
| `UploadTenantLogoCommand` | `SuperAdmin`, `SamaajAdmin` + `Tenant.Manage` | built |
| `RemoveTenantLogoCommand` | `SuperAdmin`, `SamaajAdmin` + `Tenant.Manage` | built |
| `UploadTenantBannerCommand` | `SuperAdmin`, `SamaajAdmin` + `Tenant.Manage` | built |
| `RemoveTenantBannerCommand` | `SuperAdmin`, `SamaajAdmin` + `Tenant.Manage` | built |
| `SetRolePermissionCommand` | `Roles.Manage` | built |
| `ChangePasswordCommand` | any authenticated role, self only | built |
| `RequestPasswordResetCommand` | anonymous | built |
| `RedeemPasswordResetCommand` | anonymous | built |
| `SetSecurityPolicyCommand` | `SuperAdmin`, `SamaajAdmin` + `Tenant.ManageSecurityPolicy` | built |

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
| `GetTenantLogoQuery` | **anonymous** — see below | built |
| `GetTenantBannerQuery` | `Member`, `SamaajAdmin`, `AreaAdmin`, `SuperAdmin` - and only the Samaaj's own | built |
| `GetSecurityPolicyQuery` | `SuperAdmin`, `SamaajAdmin` + `Tenant.ManageSecurityPolicy` | built |

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
| `UserStatusChangedDomainEvent` | `identity.user.status-changed.v1` | `User.Suspend` and `.Reinstate` |
| `UserRoleRevokedDomainEvent` | `identity.user.role-revoked.v1` | `User.RevokeRole` |
| `SessionRevokedDomainEvent` | `identity.session.revoked.v1` | `User.RecordSessionRevoked` |
| `PasswordChangedDomainEvent` | `identity.user.password-changed.v1` | `User.ChangePassword` |
| `LoginOtpRequestedDomainEvent` | `identity.login-otp.requested.v1` | `User.BeginSecondFactor` and `.ResendSecondFactor` - the sign-in code |
| `PasswordResetRequestedDomainEvent` | `identity.password-reset.requested.v1` | `User.RequestPasswordReset` |
| `PasswordResetDomainEvent` | `identity.password-reset.completed.v1` | `User.ResetPassword` |
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
| POST | `/v1/identity/captcha` | anonymous |
| POST | `/v1/identity/login` | anonymous |
| POST | `/v1/identity/login/verify` | anonymous |
| POST | `/v1/identity/login/resend` | anonymous |
| POST | `/v1/identity/token/refresh` | anonymous |
| POST | `/v1/identity/logout` | anonymous |
| GET | `/v1/identity/me` | any authenticated role |
| POST | `/v1/identity/me/password` | any authenticated role |
| POST | `/v1/identity/password-reset/request` | anonymous |
| POST | `/v1/identity/password-reset/redeem` | anonymous |
| GET | `/v1/identity/activations/pending` | `SamaajAdmin` + `AdminUsers.Manage` |
| POST | `/v1/identity/activations/{userId}/code` | `SamaajAdmin` + `AdminUsers.Manage` |
| POST | `/v1/identity/activations/redeem` | anonymous |
| GET | `/v1/identity/consent-notice` | anonymous |
| POST | `/v1/identity/me/consents/{purpose}/withdraw` | any authenticated role |
| GET | `/v1/identity/roles` | any authenticated role |
| PUT | `/v1/identity/roles/{roleId}/permissions/{permissionKey}` | `Roles.Manage` |
| GET | `/v1/identity/admins` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` |
| POST | `/v1/identity/admins` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` |
| PUT | `/v1/identity/admins/{userId}/roles/{role}` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` |
| PUT | `/v1/identity/admins/{userId}/status` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` |
| GET | `/v1/identity/me/data-export` | any authenticated role |
| POST | `/v1/identity/me/erase` | any authenticated role |
| PUT | `/v1/identity/tenants/{id}/grievance-contact` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` |
| POST | `/v1/identity/tenants/{id}/logo` | `SuperAdmin`, `SamaajAdmin` + `Tenant.Manage` |
| GET | `/v1/identity/tenants/{id}/logo` | anonymous |
| DELETE | `/v1/identity/tenants/{id}/logo` | `SuperAdmin`, `SamaajAdmin` + `Tenant.Manage` |
| POST | `/v1/identity/tenants/{id}/banner` | `SuperAdmin`, `SamaajAdmin` + `Tenant.Manage` |
| GET | `/v1/identity/tenants/{id}/banner` | that Samaaj's members and administrators, and Super Admins |
| DELETE | `/v1/identity/tenants/{id}/banner` | `SuperAdmin`, `SamaajAdmin` + `Tenant.Manage` |
| GET | `/v1/identity/tenants/{id}/security-policy` | `SuperAdmin`, `SamaajAdmin` + `Tenant.ManageSecurityPolicy` |
| PUT | `/v1/identity/tenants/{id}/security-policy` | `SuperAdmin`, `SamaajAdmin` + `Tenant.ManageSecurityPolicy` |
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

**A Samaaj's logo is the one image on the platform served to anyone**, and that
is a decision rather than an omission. Everywhere else — a member's photo, a
child's — the bytes are served only to a caller who passed a check, because they
are photographs of people. A logo is an organisation's public mark, the one on
its letterhead, and the screen that most needs it is the registration form:
somebody signing up picks their Samaaj before they have an account, so
`ListRegisterableTenantsQuery` and `GetTenantBySlugQuery` are anonymous by
necessity and a logo needing a token could not appear beside the name they
already publish.

`SECURITY-CHECKLIST.md` says so explicitly rather than letting the member-photo
tick imply it covers this, and `GetTenantLogoQuery` is on that page's anonymous
list. `Cache-Control` is `public` for the same reason it is `private` on a
photo, which makes logos cheaper to serve rather than more expensive.

**A Samaaj has a banner as well as a logo (2026-10-25), and it is the logo's
opposite on who may see it.** The photo across the top of its members' Home:
set by the same people (a Super Admin, or the Samaaj's own administrator),
stored the same way - which is why `TenantLogo` became `TenantImage`, one table
holding both, the rename written as a real rename so no logo was lost - and
served only to that Samaaj's own members and administrators, and Super Admins.
`Cache-Control: private`, never public. The reason is what a banner usually is:
a photograph of a gathering, a temple or a procession, with the Samaaj's members
in it - a member's photo's rule, not a letterhead's. The anonymous Samaaj lookup
still names its *path*, `bannerUrl`, because knowing one exists says nothing
the picture would; the path carries a version so a replaced banner is a new URL.
Another Samaaj's member asking for it is told there is none. `TenantBannerTests`
holds all of it.

**Reading a logo is anonymous; setting one is not, and they share a path.**
`Tenant.Manage`, and a Samaaj Admin may set their own — the same reasoning as
the grievance contact, since routing every change through the platform operator
would make it stale by design. There are tests for both halves, and they are
worth keeping: an `AllowAnonymous` on the wrong verb of one route is an easy
mistake and an invisible one.

**Nothing could set a logo until 2026-09-04.** `LogoUrl` had been on the record
since the first migration and no command ever took one, so the column was null
on every row the platform has ever had, while the admin wireframe's Create
Samaaj screen drew an "Upload Logo" control with nothing behind it. The
third-party tracking problem the field was documented as having was therefore
entirely theoretical — which is worse than harmless, because a security note
about something that cannot happen dilutes the ones that matter. Worth
generalising: **a field the API can read and nothing can write is a gap of the
same family as an endpoint with no caller**, and this repository has now found
several of the latter and exactly one of the former.

**`ImageContent` is copied verbatim from member-family-service**, namespace
aside, and `scripts/security-invariants.sh` holds the two identical. Sniffing an
image is the same problem in both services and a second copy that drifts is how
one of them quietly starts accepting something the other refuses. The copy is
deliberate rather than a shared package, matching how the pipeline behaviors and
the outbox are handled here.

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

## Signing in: a CAPTCHA, a password, and a code

Since 2026-09-27, confirmed with the user: two factors for **everyone**, a
trusted device excused the second for 30 days, and a self-hosted CAPTCHA on
the password step.

```
POST /captcha            -> { captchaId, imagePng }
POST /login              { identifier, password, captchaId, captchaAnswer, deviceToken? }
   trusted device        -> { codeRequired: false, session }
   anywhere else         -> { codeRequired: true, challengeToken, codeSentTo }   (a code is sent)
POST /login/verify       { challengeToken, code, trustDevice }
                         -> { session, deviceToken? }
POST /login/resend       { challengeToken }   at most every 30 seconds
```

**The CAPTCHA is checked first, before any account is read**, so a script that
cannot read the picture learns nothing, not even a lookup's timing. It is this
service's own (`CaptchaRenderer` draws a PNG with a bitmap font - no graphics
library, no third party learning who signs in), single use, and **spent
whether the answer was right or not, on its own connection**
(`ICaptchaStore.TryRedeemAsync`, one conditional UPDATE): the
`IFailedLoginRecorder` shape again, because a wrong password rolls the
request back, and a CAPTCHA handed back by that rollback could be replayed
for unlimited password guesses. `SignInSecurityTests` proves the replay fails.

**The code is a second factor, not a way around the first.** A correct
password on an untrusted device issues a `LoginChallenge` (256 bits, hashed)
and a `LoginOtp`, and sends the code through
`identity.login-otp.requested.v1`, the pipeline passwordless sign-in used.
`VerifySignInCodeCommand` takes the challenge and the code and no identifier,
so it cannot be reached without the password step. A wrong code counts against
the same login lockout a wrong password does (SECURITY-CHECKLIST.md: "any OTP
endpoint" is "any endpoint that checks a credential"), and an unknown challenge,
an expired one and a wrong code are one answer. Succeeding verifies the contact
address, which is still the only way `IsContactVerified` becomes true for a
registered member.

**Passwordless code sign-in was retired the same day** - its two commands and
the `/otp/request` and `/otp/login` routes are gone (confirmed with the user). With two factors required of everybody, a code alone was the one way in
that skipped the password.

**A trusted device skips the code, never the password.** `TrustedDevice` is a
256-bit token, hashed like a refresh token, that the browser keeps in
`localStorage` (libs/shared's `DeviceTrustStore`) - longer-lived than the
session on purpose, and safe to be because it opens nothing without the
password. `ISessionService.EndAllForUserAsync` revokes every device with the
sessions, so changing or resetting a password, signing out everywhere and
erasure all make every browser ask for a code again.

**`SignInSecurityOptions` can switch either guard off**, for the integration
tests that sign in to exercise something else (`GuardedIdentityTenantApiFactory`
turns both back on for the tests about them); the host warns at start-up when
either is off. `RevealSecretsInLogs` writes each CAPTCHA answer and code to
this service's log - set only in docker-compose, which has no SMS or email
provider and whose smoke run signs in by reading them (`scripts/lib/sign-in.sh`).

**The same flag now also puts the code on the response itself, as `DevCode`
- since 2026-09-27.** `LoginCommand`'s `SignInResponse`,
`ResendSignInCodeCommand`'s response and `RequestPasswordResetCommand`'s all
carry it, null unless `RevealSecretsInLogs` is on. It reuses the one flag
rather than adding a second, because both are the same fact about the
environment: no real provider exists to read the code from, so reveal it
somewhere a developer can. `libs/shared`'s `AuthService` is the one reader,
and prints it to the browser console - with no gate of its own, so this flag
is the whole of it; see that library's `CLAUDE.md` for why. `RequestPasswordResetResponse`'s `DevCode` is the one case worth
naming: present or absent now says whether the identifier belongs to a real,
active account, which the response is otherwise built never to reveal - only
acceptable because the log this flag already writes says the same thing the
same way, on the same local-only stack.

**The one typed value that meets `HashDeterministic` is the CAPTCHA answer,
knowingly** - see `CaptchaChallenge`'s remarks: hashed with its own id, good
for five minutes and one try, it is worth nothing to reverse.

**The code is the one thing this service ever puts in a Kafka payload that
is not already meant to be read by anyone who can reach the topic.** Every
other domain event either names an id or, where it carries something
sensitive, deliberately does not (`UserErasedDomainEvent` carries two ids and
nothing else). `LoginOtpRequestedDomainEvent` carries the plaintext code
because there is no admin standing between minting it and delivering it the
way there is for an activation code - the notification pipeline is the only
route the code has to the member at all. Accepted for the same reason the
rest of this service's inter-service traffic is: every consumer is inside the
same trust boundary, and a real provider replacing `LoggingNotificationChannel`
does not change what travels through Kafka to get there.
`PasswordResetRequestedDomainEvent` (the wireframe's separate `#forgot`/`#otp`
reset flow) carries a code the same way, for the same reason.

**Forgot password redeems into a new password, not a session - no token in
the response.** `RedeemPasswordResetCommand` mirrors
`ActivateAccountCommand`'s own choice: proving you hold a contact address a
code was sent to is weaker proof than a real password, so the next step is
signing in normally, not skipping it. It also needs no `ITenantRepository` at
all - it is not issuing a session, only checking a code exists and matches,
so there is no Samaaj to be a member of yet to check.

**`ResetPassword` and `ChangePassword` are two events, not one with a
flag.** Both replace `PasswordHash`; the difference is what proved the
request was legitimate - a password the member already knew, or a code sent
to an address they hold. The audit trail should be able to tell "the member
changed their own password" from "someone completed a forgot-password flow"
without inferring it from which endpoint happened to be hit, which is
exactly the kind of question `UserActivatedFromChildDomainEvent` existing
separately from `UserRegisteredDomainEvent` already answers for a different
pair of near-identical facts.

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

**"Treat it as an incident" had nowhere to be treated, until 2026-09-04.**
`SessionRevokedDomainEvent` existed, carried exactly this reasoning in its own
doc comment, and was raised nowhere:
`SessionService.RevokeSessionOutOfBandAsync` revoked the token chain against a
raw `DbContext` scope with nothing tracked, so the Outbox had nothing to drain,
and the signal reached `ILogger.LogWarning` and no further - invisible to the
audit trail an administrator can actually search. `User.RecordSessionRevoked`
is now called in that same scope before it saves, so the event drains through
the Outbox in the same transaction as the revocation. Given no reason to notify
the member (revoking already does that, by ending their session), and given the
audit trail is the intended reader, it carries no `Notification` spec -
`audit-notification-service`'s `KnownEvents` gives it its own descriptor
instead, so the audit row names the session as the entity and the account as
the actor rather than falling to the derived default, which would have left
both blank.

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

**No account could ever replace its own password, until `ChangePasswordCommand`.**
`User.PasswordHash` had exactly two writers: construction, and `Activate()`,
which only runs once, moving a `PendingActivation` account to `Active` for the
first time. An account that already had a working password - every member,
every Samaaj Admin, the bootstrap Super Admin - had no way to set a new one.
`SessionEndReason.PasswordChanged` had even anticipated this: it sat declared
in the first migration, its own doc comment saying "the member changed their
password", raised by nothing, for exactly as long as there was nothing that
could change one.

The current password is checked inline rather than through
`IStepUpAuthentication`, though the shape - `GetSelfAsync`, the lockout check,
`IPasswordHasher.Verify`, the same `IFailedLoginRecorder` a wrong login counts
against - is identical. What is not reused is the message: `ConfirmAsync`'s
failure is written for an irreversible action ("...cannot be undone, so we ask
for it first"), and that is false for a password, which can always be changed
back. A security message about something that cannot happen dilutes the ones
that matter, the same reasoning the tenant-logo tracking note above already
follows.

Changing a password ends every other session for the account
(`ISessionService.EndAllForUserAsync`, `SessionEndReason.PasswordChanged`) -
the request's own access token is exempt, since it cannot be withdrawn and
outlives this by fifteen minutes at most, but every refresh token is revoked
immediately. A stolen refresh token is worth nothing the moment its owner
changes their password; leaving it live would mean the whole point of changing
one only bites once that token's own fourteen-day life runs out.

**Five numbers this section names as fixed - the password floor, both OTP
lifetimes, the lockout threshold and its duration, and the refresh token's
fourteen days - are now a Samaaj's own choice to tighten or loosen, not just
the platform's.** `TenantSecurityPolicy` is sparse and override-only, the
same shape `RolePermissionOverride` already uses for the role matrix: a
Samaaj that has changed nothing has no row and behaves exactly as it did
before this existed, and clearing a field (sending it back to `null`) resumes
tracking the platform default rather than freezing whatever value happened to
be there.

`ISecurityPolicyResolver` is the one place "a Samaaj's override, or the
platform default" is decided - every call site that used to read
`User.MaxFailedAttempts`, `LoginOtp.Lifetime`, `PasswordResetCode.Lifetime` or
`RefreshToken.Lifetime` directly now resolves a policy and reads a concrete
value off it instead, and those constants stay exactly where they were,
now serving as the resolver's own fallback rather than being read by
five separate call sites each with their own idea of the default.

**The password floor moves in one direction only.** A Samaaj may ask for
passwords longer than the platform's ten-character minimum; the resolver
takes `Math.Max(10, override)`, so nothing can ask for shorter ones even if a
bad row somehow got past `SetSecurityPolicyCommandValidator`'s own 10–64
range check. This is enforced as a handler-level check
(`ResolvedSecurityPolicy.ValidatePasswordLength`) in `RegisterMemberCommand`,
`ActivateAccountCommand`, `ChangePasswordCommand` and
`RedeemPasswordResetCommand` alike, not as a change to any of their four
existing `MinimumLength(10)` FluentValidation rules - two of those four are
anonymous requests with no resolved tenant at validation time, so a
tenant-aware validator was never an option for all four uniformly. The
platform floor those validators already enforce has to hold regardless of
Samaaj, so it stays exactly as it was; a stricter Samaaj asks for more on top
of it, checked once the handler has loaded the account and can resolve which
Samaaj it belongs to.

**Setting it is `SuperAdmin`/`SamaajAdmin` self-tenant-only, the same shape
the grievance contact and the tenant logo already use** - a new permission,
`Tenant.ManageSecurityPolicy`, rather than reusing `Tenant.Manage` (reserved
platform-level by its own doc comment: "which modules exist is a
platform-level decision") or `AdminUsers.Manage` (about administrator
accounts, not security policy).

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

**The role matrix is editable per Samaaj**, which it was not until the three
things `ListRolesQuery` named as preconditions existed: per-tenant definitions,
an audit trail, and a floor of permissions no edit may remove.

`AuthorizationCatalog` stays the platform default for every Samaaj.
`RolePermissionOverride` records only where one has decided differently, as a
row saying "for this Samaaj, this role does - or does not - carry this
permission". A Samaaj that has changed nothing has no rows and behaves exactly
as it did before the feature existed, and one that sets a permission back to its
default has its override deleted rather than stored, so it resumes tracking that
default as it changes.

What a command *requires* is still compiled in. This decides who *carries* a
permission, not what a command *needs* - the two halves of role-based access
control, and only one of them is a runtime decision. That is why the objection
the read-only version raised does not apply: the answer is not split between
source and a table, because the two answer different questions.

The floor is in `MatrixEditing`, and it is two rules. **SuperAdmin cannot be
edited by a Samaaj** - it is platform administration, and the role that has to
stay able to repair a Samaaj that has locked itself out. **A Samaaj Admin cannot
lose `Roles.Manage`** - the one revocation a Samaaj could not undo for itself,
since without it the screen that edits the matrix refuses the administrator who
just used it. It is a floor rather than a warning because a warning is something
somebody clicks past at the end of a long afternoon.

It is gated on `Roles.Manage`, a key of its own rather than `AdminUsers.Manage`.
Inviting an administrator hands somebody an existing bundle of permissions; this
redefines the bundle, for everyone who holds the role and everyone who ever
will. Every change raises `identity.role-matrix.changed.v1` carrying who made
it and what it was before.

`editable` on the response means **this caller, in this scope, may edit** rather
than "this matrix is editable". Reading the matrix stays open to everybody -
seeing why you were refused something is a good thing - and a screen told the
second would offer an ordinary member controls the server refuses.

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

**Suspending an account was fully built and unreachable, until 2026-09-05.**
`UserStatus.Suspended` has existed since the first migration.
`LoginCommandHandler` has always refused it. `SessionService.ContinueAsync`
has always re-read status on every refresh and force-revoked the whole chain
the moment it found anything but `Active` - which is what makes suspending
someone bite within one access token's lifetime rather than at their next
sign-in, exactly as this file's own Sessions section and
`SECURITY-CHECKLIST.md` already claimed. What none of that had was a door in:
`User.Suspend()` was called from nowhere but a unit test's own setup, and
`Reinstate()` from nowhere at all. A Samaaj administrator had no way to act on
a problem account short of asking the platform operator to archive the whole
Samaaj.

`SetUserSuspensionCommand` is that door, one command for both directions like
`AssignRoleCommand`'s own `Granted` boolean. Suspending needs the caller's own
password - the same asymmetry `ChangeTenantStatusCommand` draws between taking
something out of service and restoring it - and refuses two targets outright:
an erased account, and the caller's own. Suspending yourself ends your own
session and, unlike reinstating, cannot be undone by the account it happened
to; the self-revoke guard above is the same reasoning one level further.

`UserStatusChangedDomainEvent` is new alongside it, named and shaped after
`TenantStatusChangedDomainEvent` on purpose - the same kind of fact, one
account-wide and one Samaaj-wide, with no reason for their audit rows to look
different. `audit-notification-service`'s `KnownEvents` names the *account* as
the actor's target and the *administrator* as the actor, which the derived
default would have gotten backwards for the one question an audit row like this
exists to answer.

**Inviting creates the account and issues the code in one command.** An
invitation that created the account and then failed to issue a code would leave
an account nobody can reach and no obvious way to tell that is what happened.
The invited roles are granted up front: they cannot be used until the account
can be signed into, and granting them at invitation time means the decision has
one audit trail rather than a grant someone must remember to make later. The
account is `PendingActivation` with no password, exactly like a converted
child's, because inviting someone establishes they are entitled to an account
and nobody has yet proved they are the person asking for it.

**`Activate()` used to raise one event regardless of which flow redeemed the
code, and an invited admin ended up Active with no profile at all, until
2026-09-06.** A converted child and an invited admin both redeem an
`ActivationCode` through the same `ActivateAccountCommand`, and `Activate()`
raised `UserActivatedFromChildDomainEvent` unconditionally - correct for a
conversion, since member-family-service's consumer uses it to find the
waiting `ChildProfile` and link it. An invited admin has no such record, so
the event carried an empty `ChildProfileId`, which that consumer treats as an
unusable payload and silently skips (logging a warning and committing the
offset regardless, per its own doc comment about not stalling the partition).
The admin's account was `Active` and could sign in; member-family-service had
simply never heard of them, and the member portal's "still being set up" copy
was the only visible symptom. `Activate()` now branches on
`ConvertedFromChildProfileId`: present, raise the child-conversion event as
before; absent, raise `UserRegisteredDomainEvent` instead - the same event an
ordinary registration raises, and exactly what `AdminInvitedDomainEvent`'s own
doc comment already promised would happen "when the invitation is redeemed".

**Area Admin, added 2026-09-26: an administrator restricted to one locality.**
The restriction is `UserRole.LocalityId`, a second scope beside `TenantScope`
on the grant itself rather than a flag on the account, so revoking the role
takes the restriction with it. `InviteAdminCommand` and `AssignRoleCommand`
both take an optional `LocalityId`, required exactly when the role is
`AreaAdmin` and refused otherwise, and an `AreaAdmin` invitation may name no
other role - a locality beside two roles would leave it unclear which grant it
belongs to. `GetAuthorizationAsync` reads it back and every token minted for
the account carries it as the `locality_id` claim (`PlatformClaimTypes`, in
all sixteen copies); `/me` returns it as `areaLocalityId` for display.

This service only *carries* the restriction; each service holding locality-
scoped data enforces it, from the token, the same way it enforces `tenant_id`
- never from a header, which a caller could set. `AreaAdmin` holds exactly the
permissions of the services that do: `Members.Read`/`Members.Write`
(member-family-service), `VolunteerGroups.Manage`/`Lead`, `Events.Publish`,
`Pathshala.Manage` and `SocialIssues.Approve`. A permission granted here
without its service's locality check would make an Area Admin an
unrestricted admin of that module, so the two land together.

In every one of those services the locality bounds the **administrator**,
never the member: an Area Admin browses, registers, applies and votes across
the whole Samaaj like anybody else, and only the admin powers stop at their
locality.

**A Pathshala teacher need not be a member of the Samaaj (2026-09-27).** An
invitation for the `PathshalaTeacher` role and nothing else creates an
account with no `Member` role - somebody from outside the Samaaj who signs in
only to teach. Every other invitation is an administrator, who is a member of
the Samaaj they run, and keeps `Member`. Two things follow. The teacher role
carries `Pathshala.Teach` instead of `Members.Read`, so such an account reads
nothing member-facing anywhere on the platform (a member who teaches still has
`Members.Read`, from `Member`). And activating it raises no
`identity.user.registered.v1`: member-family-service answers that event by
creating a member profile, which would put a non-member into the member
directory. `User.IsMember` decides, which is why `FindPendingActivationAsync`
now loads the role grants. Existing teacher accounts were left as they were:
both on the development stack had member profiles.

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

**Editing the role matrix reached the platform without any of that.** It has
eleven integration tests in `RoleMatrixEditingTests`, and until 2026-09-04 it
had never been called through the gateway — while also being absent from this
file's Commands and endpoint tables and from `API-CONTRACTS.md`, which still
described `/roles` as "Read-only". Three hand-written lists missed the same
endpoint, and it is the one that decides what a role may do for everybody who
holds it. `scripts/service-docs.sh` is what found it. The smoke run now grants a
permission a role never had, reads it back on that role's own row, checks the
two floors (403 on SuperAdmin, 409 on taking `Roles.Manage` from SamaajAdmin),
and puts the grant back.
