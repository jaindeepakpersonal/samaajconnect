# Security Checklist

Derived from the cross-cutting requirements in both requirement docs.
Treat this as a PR review checklist for anything touching a new
endpoint, not just a one-time setup task.

> **Status as of 2026-09-01.** Every box below has been walked against **all ten
> services and the gateway** — the previous pass covered three, and six of the
> seven that shipped after it were never re-checked. A ticked box is something a
> test or a smoke check actually asserts, not something believed to be true; an
> unticked one says plainly what is missing and where it is tracked. Items that
> cannot apply yet — file uploads, for instance — say so rather than being
> ticked by default.
>
> **What that pass found.** Two things, both recorded rather than glossed:
> a step-up password check that no lockout counted (fixed, below), and six
> services holding member ids that never subscribed to the erasure event
> (tracked, see "Data privacy"). The mechanical properties held everywhere:
> all ten apply the tenant query filter by reflection, call `TenantWriteGuard`
> at `SaveChanges`, register the five behaviors in the required order, and
> fail closed on a request carrying no authorization attribute.

## Tenant isolation

- [x] Every tenant-owned entity has `TenantId` and an EF Core
      `HasQueryFilter` applying it on every read. Applied by reflection over
      `ITenantScopedEntity` in each `DbContext`, so an entity is filtered by
      implementing the interface rather than by someone remembering.
- [x] Every write handler re-validates that the target entity's
      `TenantId` matches `ITenantContext.TenantId` — do not rely on the
      query filter alone for writes (IDOR protection). Handlers do this and
      say so. It is *also* enforced once, at `SaveChanges`, by
      `TenantWriteGuard`: a handler that forgets the check looks exactly like
      one that does not need it, so the rule is enforced where it cannot be
      skipped. The guard is deliberately silent when no tenant is resolved,
      because consumers legitimately have none.

      **Probed, not assumed.** `scripts/tenant-isolation-probe.sh` builds two
      real Samaaj and has Samaaj B's member *and* Samaaj B's administrator
      attempt 36 reads and writes against Samaaj A's ids, through the gateway,
      across all ten services. Every one is refused with **404** — not 403,
      which would confirm the id is real. The administrator half is the one
      that matters most: a Super Admin whose override scopes them to B is
      exactly the caller the query filter would let through if a handler
      forgot its own check.

      The script asserts its own validity before it believes a pass. A probe
      against a path that does not exist answers 404 and is indistinguishable
      from a refusal; the first run scored three of those, because the
      volunteer-groups routes are under `/v1/volunteer-groups/groups`. So each
      entity is now proven reachable by its *own* Samaaj first, and a missing
      fixture id aborts the run rather than being probed — an empty id turns
      `/groups/{id}` into the list endpoint, which answers 200 to anybody.
- [x] `TenantId` is never read from a client-supplied field in a
      request body. It comes only from the resolved gateway context. The one
      apparent exception is registration, which takes a **slug** and resolves
      it server-side against the tenant table, refusing a request whose
      resolved tenant disagrees.
- [x] Super Admin tenant-override requests are logged with both the
      actor's identity and the overridden `TenantId`, on every request,
      not just at session start. `TenantResolutionMiddleware` logs actor,
      tenant, method and path per request, and refusals are logged too.

## Authorization

- [x] Every endpoint has an explicit `[Authorize(Policy = "...")]` or
      equivalent — no endpoint relies on "nobody will call it directly."
      Every request type carries `[RequiresRoles]`, `[RequiresPermission]`,
      `[AllowAnonymousRequest]` or `[InternalRequest]`, and
      `TenantAuthorizationBehavior` **denies a request carrying none of them**.
      Forgetting to annotate surfaces as a 403 in the first test rather than as
      an unguarded endpoint in production.
- [x] UI hiding of a nav item or button is never the only control —
      confirm the backend rejects the action too. Both portals treat guards and
      role-aware rendering as convenience; the refusals are asserted
      server-side, and `scripts/smoke-through-gateway.sh` checks the negative
      cases through the gateway.
- [x] `TenantAuthorizationBehavior` runs before `ValidationBehavior` in
      the pipeline (see `ARCHITECTURE.md` §3) so an unauthorized caller
      never learns anything about validation rules for data they can't
      access. Registered in that order in all three services, numbered in the
      source, and covered by `PipelineBehaviorTests`.

## Permission key naming convention

Use `{Module}.{Action}`, matching the style already used in your
existing platform (`Pathshala.Attendance.Write`, `Issue.Approve`):

| Key | Grants |
|---|---|
| `Tenant.Manage` | Create/activate/deactivate tenants |
| `AdminUsers.Manage` | Create/invite admins, assign roles |
| `Members.Read` / `Members.Write` | Directory search / profile correction |
| `Family.Write` | Family/child management |
| `Family.ApproveConversion` | Approve adult-child conversion |
| `Timeline.Post` / `Timeline.Moderate` | Post / moderate content |
| `VolunteerGroups.Manage` | Create groups, decide applications |
| `Events.Publish` | Create/publish events |
| `SocialIssues.Approve` | Approve/reject/publish issues |
| `CelebrityVoting.Configure` | Create campaign, close, publish results |
| `Pathshala.Manage` | Super Admin: create Pathshala master record |
| `Pathshala.Attendance.Write` | Teacher: mark attendance |
| `Pathshala.Exams.Write` | Teacher: record results |
| `Boli.Manage` | Occasion/type/open/close |
| `Boli.PublishResults` | Publish (irreversible without correction flow) |
| `Audit.Read` | View audit log |
| `Notifications.Broadcast` | Announce to every member of a Samaaj, and read what has been announced. Its own key rather than folded into an administrative one: managing members and putting a message in front of every one of them are different powers |

`AuthorizationCatalog` in identity-tenant-service is the executable copy of
this table, with stable hand-assigned ids so a grant means the same thing in
every environment. `GET /v1/identity/roles` serves it, so the admin panel shows
what is actually enforced rather than a second copy that can drift.

### A permission held only by an ungranted role is a permission nobody has

Three capabilities have now shipped unreachable because their permission was
carried only by a role that nothing grants:

- `Family.Write` sat on `FamilyHead` — no member could add a child.
- `VolunteerGroups.Manage` sat on `VolunteerGroupPresident` — no president
  could decide their own group's applications.

Both were found by a smoke test through the gateway, and neither could have been
found by a unit test: those build principals by hand and hand them whatever
permission the test needs. `Member`, `SamaajAdmin` and `SuperAdmin` are the
only roles anything currently grants, so when adding a permission, ask which of
*those* carries it.

Where the answer is "the person doing this is an ordinary member who happens to
hold a position", the pattern is a permission every member holds plus a data
check in the handler — `Family.Write` with "are you this family's head?", and
`VolunteerGroups.Lead` with "are you this group's president?". The permission
is the outer gate and grants nothing on its own.

## Audit logging

- [x] Every state-changing command that matches an item in the "Audit
      Logs" section of the admin requirements doc publishes a domain
      event that `audit-notification-service` consumes. That service also
      subscribes by regex to *every* versioned topic, so an event nobody has
      described yet still produces a row rather than a hole.
- [x] Audit rows are immutable (no update/delete endpoint exists for
      `AuditLog`, ever). One narrowly-scoped exception exists for erasure, and
      it cannot touch the action, entity, topic or timestamps — see "Erasure vs.
      the audit log" in `docs/product/DPDP-COMPLIANCE.md`.
- [x] Before/after JSON snapshots are captured for corrections and
      status changes, not just creation events. Status and module changes carry
      their previous value into `AuditLog.BeforeState`. A profile correction
      records **which fields changed, never their values**: the payload is
      stored verbatim in an append-only table, and a member's previous mobile
      number would then be somewhere the platform deliberately makes hard to
      redact. That is a knowing deviation from the literal "JSON snapshot", and
      it answers the audit question — what did an administrator change, and
      when — without turning the audit log into a second copy of the data.

## Session & auth

- [x] Sessions/JWTs are tenant-scoped and expire; refresh tokens are
      revocable server-side (e.g. on suspicious activity or admin
      forced logout). Access tokens carry `tenant_id` and now last **15
      minutes**, not an hour. Refresh tokens are rows: stored as hashes,
      single-use, rotated on every use, and revocable. `POST
      /v1/identity/logout` ends the session — or every session for the account
      with `everywhere` — and erasing an account ends all of them. Refreshing
      re-reads the account, its Samaaj and its roles, so suspending an account,
      deactivating a Samaaj or revoking a role all take effect within one access
      token's lifetime instead of at the next sign-in.

      **Reuse is treated as theft.** A refresh token presented twice means two
      parties hold it; there is no way to tell which is the member, so the whole
      session chain is revoked and both must sign in again. That revocation is
      written on its own connection, because the request that detects it returns
      a failure and `TransactionBehavior` would otherwise roll the revocation
      back with it.

      **What remains open, precisely.** An access token cannot be withdrawn —
      that is what makes it stateless, and what lets every service authorize
      without calling identity-tenant-service. So a stolen access token, or one
      belonging to an account erased a moment ago, keeps working for up to
      fifteen minutes. Closing that last window means a revocation check on
      every request in every service, which trades away the property the design
      was chosen for. Fifteen minutes is the deliberate price; if it is ever
      judged too long, lower `Jwt:AccessTokenMinutes` before reaching for the
      per-request lookup.
- [x] Rate limiting and brute-force lockout on `/login` and any OTP
      endpoint. Both halves. Per-account lockout after five wrong passwords,
      and five wrong activation guesses kill the code; per-source rate limits
      at the gateway on `/login`, `/activations/redeem`, `/register`,
      `/me/erase` and `/tenants/{id}/status`. The limits are deliberately loose
      because Indian mobile carriers put many subscribers behind one address —
      see `gateway/.../RateLimiting.cs`. There is no OTP endpoint yet; when one
      lands it belongs on the credential policy.

      **Read "any OTP endpoint" as "any endpoint that checks a credential".**
      The step-up endpoints shipped outside both halves: they verify a password
      and recorded nothing, so `/me/erase` was an unthrottled password oracle.
      Anyone holding a borrowed access token — the fifteen-minute window the
      stateless-token design knowingly accepts — could guess at full speed and
      never trip the login lockout, turning a session compromise into a
      permanent credential one. On the shared and family devices this platform
      is built for, that is somebody walking up to an unlocked tab. Found by
      the 2026-09-01 pass and fixed: `StepUpAuthentication` now refuses when
      the account is locked out and records each failure through the same
      `IFailedLoginRecorder` login uses, so the two share one budget rather
      than offering two oracles. `ErasureEndpointTests` proves the lockout
      engages and that it also blocks the *correct* password afterwards.
- [ ] All production traffic is HTTPS-only; no mixed content. **A deployment
      concern, not a code one, and nothing here enforces it.** Compose serves
      plain HTTP for local work. Needs TLS termination, HSTS, and secure-cookie
      policy decided with the hosting; tracked in `DEVELOPMENT_PLAN.md` Phase 5.
- [x] Sensitive admin actions (publish Boli result, publish voting
      results, deactivate a tenant) are candidates for step-up
      authentication (re-enter password / OTP). **Erasing an account,
      deactivating a Samaaj and archiving one all re-ask for the caller's own
      password**, through the shared `IStepUpAuthentication` in
      identity-tenant-service. Activating a Samaaj deliberately does not: a
      step-up on a harmless, reversible direction only teaches people to type
      their password without reading the screen. Publishing a voting result is
      reversible in the sense that matters — it refuses to publish twice, so a
      mis-click cannot change an announced result.

      **Publishing a Boli result deliberately does not step up either**, on the
      same reasoning, now that boli-service exists. `PublishResultCommand` is
      idempotent and a published result cannot be changed through the API at
      all, so a mis-click cannot announce the wrong winner — it can only
      announce the right one slightly early. What it does carry is its own
      permission, `Boli.PublishResults`, separate from `Boli.Manage`: the
      control on "this cannot be taken back" is who may do it, not how many
      times they are asked. A Samaaj that wants a second pair of eyes grants one
      without the other.

      **A failed step-up answers 403, never 401**, and this is a rule rather
      than a preference. The portals' HTTP interceptor treats a 401 as an
      expired access token: it renews the token and *retries the original
      request*. On a step-up endpoint that means a destructive command is
      submitted a second time because somebody mistyped their password. 403 is
      also the truer answer, since the caller is authenticated and simply has
      not proven enough. Any future step-up must follow the same rule; there
      are tests asserting the status code specifically.

      The step-up reads the account with `GetSelfAsync`, past the tenant query
      filter. A Super Admin's own account lives at `PlatformTenantId` while
      they act on a Samaaj, so a tenant-filtered read finds nothing and the
      step-up fails for the one role that most needs it.

## File handling

- [ ] Uploaded files (post media, social issue evidence, profile
      photos) are size/type restricted and virus-scanned before being
      served back to any user. **Nothing is uploaded to this platform yet** —
      there is no file storage and no upload endpoint. What exists is a *link*:
      `PhotoUrl` on a member or child, and `LogoUrl` on a Samaaj, supplied by
      the client. Those are now validated as absolute `http(s)` URLs, which
      closes the `javascript:`/`data:` stored-scripting hole.
      **It does not close the tracking one**: a photo hosted anywhere sends
      every viewer's IP address to that host, and on a `ChildProfile` that is
      exactly the third-party tracking of children DPDP s.9(3) prohibits. The
      real fix is the platform hosting its own images, at which point this item
      becomes live in full. Tracked in `DEVELOPMENT_PLAN.md`.
- [ ] File storage access is authorization-checked per request, not
      just obscured by a random URL. Not applicable until there is storage;
      when there is, it must not be a public bucket with unguessable keys.

## Data privacy

- [x] Member directory respects `PrivacyLevel` per profile field, not
      just an all-or-nothing visibility toggle. Built in
      `member-family-service`; a hidden field is null, never masked.
- [x] Exports of member data are logged as audit events and restricted
      to roles with an explicit export permission. Logged: each export
      publishes `identity.member-data.exported.v1`, carrying ids and a
      timestamp and never the contents — recording what was in the export
      would make the record of the copy a second copy.
      **The permission half is deliberately not implemented as written.** The
      exports that exist are a member reading their *own* data, which is a
      right under DPDP s.11; gating that behind a permission an administrator
      grants would defeat it. The checklist item is about an administrator
      exporting *other people's* data in bulk, and no such endpoint exists. If
      one is ever added it needs its own permission key, and this item should
      be re-read then.

- [x] Erasure reaches every service that holds **free text** a member wrote.
      Six services did not subscribe to `identity.user.erased.v1` although
      `DPDP-COMPLIANCE.md` states that rule plainly. `timeline` and
      `social-issues` were the two where it mattered — a post and an issue carry
      words their author wrote, which identify them whatever happens to the id
      beside them — and both now consume it, verified end to end through Kafka
      against the running stack.

      Building those two consumers found that **six services shared the Kafka
      consumer group `timeline-service`**, scaffolded from it and never changed.
      Nothing had broken only because one of them ran a consumer; adding a
      second would have had erasure events delivered to a service that ignores
      them and committed away, intermittently. The group id now lives only in
      each service's `ConsumerOptions`.
- [ ] Decide whether a bare `MemberId` left in a service that holds no name or
      contact is still personal data after erasure. Four services are in that
      position — `events`, `volunteer-groups`, `celebrity-voting`, `boli` —
      and two of them cannot simply drop the id: the voter id **is** the
      double-voting guarantee, and a bid is a financial record. Counsel question
      6 in `DPDP-COMPLIANCE.md`.

> **DPDP Act, 2023.** The obligations this platform carries under India's data
> protection law, what is built for them, and what still needs counsel, are in
> `docs/product/DPDP-COMPLIANCE.md`. The children's-data provisions (section 9)
> are the largest exposure here, because `ChildProfile` exists by design.
