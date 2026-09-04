# Security Checklist

Derived from the cross-cutting requirements in both requirement docs.
Treat this as a PR review checklist for anything touching a new
endpoint, not just a one-time setup task.

> **Status as of 2026-09-03. The mechanical half of this page is no longer a
> walk with a date on it.** `scripts/security-invariants.sh` re-checks it from
> the source in a second, and **CI runs it**: every request type carries one of
> the four authorization attributes; the anonymous and internal sets are exactly
> the ones listed under "Authorization" below; no route reaches an internal
> command; every `DbContext` applies the tenant query filter by reflection;
> every service calls `TenantWriteGuard`; and the eleven files that are meant to
> be identical across all ten services are identical.
>
> That last check is the reason the others are worth having. A property proven
> by hand across ten copies of a file is a property that stops being true
> quietly — which is what this repository has now watched happen to the
> accessibility audit, the isolation probe and the backup drill in turn. A
> ticked box below is something a test, a smoke check or one of the scripts
> actually asserts; an unticked one says plainly what is missing and where it is
> tracked. Items that cannot apply yet — file uploads, for instance — say so
> rather than being ticked by default.
>
> **What the 2026-09-03 re-pass found.** Three things.
>
> `KafkaProducer` had drifted: eight of the ten shipped a default `ClientId` of
> `"member-family-service"`, copied and never changed. It is the producer-side
> twin of the consumer-group bug found in September — six services sharing the
> group id `timeline-service` — and that fix looked at consumers only. Every
> service overrides it in its own `appsettings.json`, so nothing was ever
> misattributed at the broker; what was there was the trap, and the default is
> now empty and falls back to the running assembly, so an unconfigured service
> names itself rather than another one.
>
> This page cited `PipelineBehaviorTests`, which does not exist in this
> repository and appears never to have. The pipeline order is checked by
> `scripts/pipeline-order.sh`, which does.
>
> And it claimed the isolation probe makes 36 attempts, from a cycle when it
> did; it makes 65 and reports its own coverage. The number is gone rather than
> corrected — see "Tenant isolation".
>
> **What the 2026-09-01 pass found**, kept because both are still the reason
> two items below read as they do: a step-up password check that no lockout
> counted (fixed, below), and six services holding member ids that never
> subscribed to the erasure event (closed since, see "Data privacy").

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
      attempt every cross-tenant read and write it can reach against Samaaj A's
      ids, through the gateway, across all ten services. Every one is refused
      with **404** — not 403, which would confirm the id is real. The
      administrator half is the one that matters most: a Super Admin whose
      override scopes them to B is exactly the caller the query filter would let
      through if a handler forgot its own check.

      **The count is deliberately not written here.** This paragraph said "36"
      for several cycles after the probe had grown to 65 attempts, which is the
      page going stale about the one script written so it could not. The probe
      reports its own coverage on every run — how many of the platform's
      id-taking endpoints it probed, and by name the ones it did not — so the
      run is the number and this page does not hold a second copy of it.

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
      the pipeline (see root `CLAUDE.md` §4.4) so an unauthorized caller
      never learns anything about validation rules for data they can't
      access. Registered in that order in **all ten services**, numbered in the
      source, and checked by `scripts/pipeline-order.sh`, which CI runs and
      which reads the expected order out of `CLAUDE.md` rather than carrying a
      second copy of it.

### The two lists below are the ones worth reading twice

Everything else on this page is a property that holds or does not. These two are
*sets*, and a set grows by one line in a pull request nobody reads twice. They
are written here rather than in the script that checks them, so this page stays
the source of truth and `scripts/security-invariants.sh` fails whichever side
moves — the arrangement §4.4 already uses.

**Adding a row here is the deliberate act.** The script does not warn about an
unlisted request type; it fails. That is the point: an endpoint becoming
anonymous should cost somebody a paragraph explaining why.

#### Requests reachable without authentication

All ten are in `identity-tenant-service`, and that is itself worth keeping true
— no other service has any business answering an unauthenticated caller.

| Request | Why it cannot require a token |
|---|---|
| `LoginCommand` | Issuing the token is what it does |
| `RefreshSessionCommand` | The refresh token *is* the credential presented |
| `SignOutCommand` | Ends a session that may already be unusable |
| `RegisterMemberCommand` | No account exists yet. Takes a **slug**, resolved server-side, never a tenant id |
| `ActivateAccountCommand` | Redeeming a first password: the person has no way to sign in yet |
| `GetConsentNoticeQuery` | DPDP s.5 requires the notice at or before the point of consent, which is registration |
| `ListRegisterableTenantsQuery` | The registration form asks people to pick their Samaaj before they have an account. Deliberately not the Super Admin's `ListTenantsQuery`, which carries status and contact details |
| `GetTenantBySlugQuery` | The gateway resolves the Samaaj before any token has been validated |
| `GetTenantByIdQuery` | Same, by id. Returns only what the gateway needs to route |
| `GetTenantLogoQuery` | The registration form draws the Samaaj directory, and a logo that needed a token could not appear on it. **The only image on the platform outside per-request authorization** — see the file-handling section |

#### Requests no HTTP route may reach

`[InternalRequest]` says a command is raised by a Kafka consumer or another
in-process caller and is not routed. That is a claim about the *absence* of a
route, which nothing in the type system can hold — so the script asserts it, by
checking no endpoint file mentions the type.

| Request | Service | Raised by |
|---|---|---|
| `RecordIntegrationEventCommand` | audit-notification | Its consumer, for every event the platform publishes |
| `ErasePersonalDataCommand` | audit-notification | `identity.user.erased.v1` |
| `ConsumeIntegrationEventCommand` | pathshala, social-issues, timeline | Each service's own consumer |
| `CreateProfileForNewUserCommand` | member-family | `identity.user.registered.v1` |
| `EraseMemberDataCommand` | member-family | `identity.user.erased.v1` |
| `CompleteChildConversionCommand` | member-family | The account-created event that follows an approved conversion |
| `CreateAccountForConvertedChildCommand` | identity-tenant | An approved adult-child conversion |

## Permission key naming convention

Use `{Module}.{Action}`, matching the style already used in your
existing platform (`Pathshala.Attendance.Write`, `Issue.Approve`):

| Key | Grants |
|---|---|
| `Tenant.Manage` | Create/activate/deactivate tenants |
| `AdminUsers.Manage` | Create/invite admins, assign roles |
| `Roles.Manage` | Edit the role/permission matrix for a Samaaj. A Samaaj administrator cannot have this one taken away — that is the lock-out floor, enforced in the service and drawn as a fixed tick rather than a checkbox in the admin panel |
| `Members.Read` / `Members.Write` | Directory search / profile correction |
| `Family.Write` | Family/child management |
| `Family.ApproveConversion` | Approve adult-child conversion |
| `Timeline.Post` / `Timeline.Moderate` | Post / moderate content |
| `VolunteerGroups.Manage` | Create groups, decide applications |
| `VolunteerGroups.Lead` | Run a group you are president of: decide its join requests. Granted to `Member`, deliberately — see "A permission held only by an ungranted role" below, and `volunteer-groups-service/CLAUDE.md` for why the permission is necessary and not sufficient |
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
      served back to any user. **Two of the three, since 2026-09-04.**

      Member and child photos are hosted by the platform now, so this item is
      live rather than not applicable. **Size**: 2 MB, enforced in the domain,
      again at the endpoint against a bounded read rather than a declared
      `Content-Length`, and once more in each portal so a phone is not asked to
      send two megabytes to be refused. **Type**: read out of the bytes and
      never taken from the upload's header, which is a string the uploader
      chose — JPEG, PNG and WebP only. SVG is excluded and the exclusion is
      load-bearing: an SVG is a document that can carry script, and these are
      served from the platform's own origin.

      **Virus scanning is not done, and nothing here pretends otherwise.**
      Sniffing proves the bytes begin like an image; it says nothing about what
      a decoder does with the rest of them. Closing it needs a scanner in the
      deployment rather than a check in a domain type, and it is the only reason
      this box is still unticked. Tracked in `DEVELOPMENT_PLAN.md`.

      Post media and social-issue evidence are still not uploadable at all —
      there is no endpoint for either.

      **A Samaaj's logo is hosted too, since 2026-09-04**, with the same size cap
      and the same sniffing. `LogoUrl` turned out to be a field nothing could
      ever set — no command took one, so it was null on every row the platform
      has had, beside an "Upload Logo" control the admin wireframe drew with
      nothing behind it. The tracking problem it was documented as having was
      therefore theoretical, which is worse than harmless: a security note about
      something that cannot happen dilutes the ones that matter.
- [x] File storage access is authorization-checked per request, not
      just obscured by a random URL. **This is why the bytes are served by the
      service that owns the profile** rather than by a media service or a
      bucket. Who may see a member's photo is who may see the member — same
      Samaaj, `Members.Read` — and that rule already lived there; a child's
      photo is the household's, so `Members.Write` does not open it, matching
      the line `DecideJoinRequestCommand` already draws. A separate store would
      have had to be told those rules, asked about them, or handed a signed URL.

      `Cache-Control: private` on every response, because a shared cache holding
      an image would hand it to a caller who never passed the check that
      produced it. Both portals fetch photos through `HttpClient` rather than an
      `<img src>` — see `AuthedImageDirective` in `libs/shared` — precisely so
      the token is attached and the check happens; an `<img src>` is fetched by
      the browser with no `Authorization` header at all, which is the shape of
      request that makes people reach for unguessable URLs.

      **What replaced the tracking hole.** A photo used to be a URL the client
      supplied, so every member who opened the directory fetched it from
      whatever host it named — and on a `ChildProfile` that is exactly the
      third-party tracking of children DPDP s.9(3) prohibits. Nothing outside
      the Samaaj is asked for anything now.

      **One exception, and it is deliberate: a Samaaj's logo is served to
      anyone.** The registration form asks somebody to pick their Samaaj before
      they have an account, so `ListRegisterableTenantsQuery` is anonymous by
      necessity and a logo needing a token could not appear beside the name it
      already publishes. It is an organisation's public mark — the one on its
      letterhead — and reveals nothing about a person, which is the difference
      from a member's photo that makes this acceptable where that would not be.
      It is `Cache-Control: public` for the same reason, which makes logos
      cheaper to serve than photos rather than more expensive. Ticking this box
      does **not** cover that endpoint, and `GetTenantLogoQuery` is on the
      anonymous list above with the same reasoning.

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
