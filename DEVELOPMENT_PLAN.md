# Development Plan — samaajconnect

Living document — check items off as they're completed, and keep
**Current Status** below up to date. This is the actionable/trackable
version of the plan; the reasoning behind the ordering lives in
`docs/product/ROADMAP.md` and shouldn't be duplicated here.

## Current Status

- **Stage:** every module has a service and member screens; Phase 5 hardening next
- **Last updated:** 2026-09-01 - the member portal's Boli screens, closing the
  last module. `boli-service` shipped in the same run as the tenth and final
  service: its guarantee is one *highest* bid rather than one vote each, which
  needs a row lock as well as a unique index, and fifteen simultaneous
  identical bids through the gateway leave exactly one. Driving Celebrity
  Voting against the running stack earlier in the run found a gateway bug that
  had been breaking every module-gated screen in the app: a gated route
  answered 404 rather than 401 when the caller's access token had expired, so
  the portals' silent renew-and-retry never fired and the screen printed "No
  such endpoint." from fifteen minutes after sign-in onwards. 1,087 tests green
  (814 backend, 273 frontend) plus 240 smoke checks against a stack built from
  empty volumes, re-runnable against a dirty one.
- **Blocking item:** none. **Every module the platform has now has both a
  service and member screens.** What is left is Phase 5 hardening, an editable
  role matrix, and the two DPDP obligations — breach notification and the right
  to nominate — that need a notification channel first. The vote endpoint's throughput load test is
  carried to Phase 5, since it needs a deployed environment; its correctness
  half is done. The five questions in `docs/product/DPDP-COMPLIANCE.md` still
  need counsel before any of this ships to real users.

*(Update these three lines at the start/end of every work session —
they're what a coding agent or a teammate should read first to know
where things stand.)*

---

## Stage 0 — Walking Skeleton

Do this before starting any other service. The goal is one full
vertical slice working end-to-end, not broad partial progress.

- [x] Shared infra running locally (`docker-compose up`: Postgres,
      Redis, Kafka) - one logical database per service created on first
      start by `infra/postgres/init-databases.sh`
- [x] `identity-tenant-service` scaffolded (`.claude/skills/new-microservice`)
  - [x] `Tenant` aggregate + `CreateTenantCommand` + `GetTenantBySlugQuery`
  - [x] `User` aggregate + `RegisterMemberCommand` + `LoginCommand`, plus
        seeded roles/permissions, `/me`, tenant activation and a configurable
        Super Admin bootstrap
  - [x] `dotnet build` / `dotnet test` pass (101 unit, 34 integration)
- [x] `audit-notification-service` scaffolded - regex-subscribed consumer
      recording every platform event into an append-only audit log, plus
      member notifications. Verified with Testcontainers Kafka, not a fake.
- [x] Gateway: subdomain → tenant resolution + JWT validation wired
      for `/v1/identity/**`, `/v1/audit/**` and `/v1/notifications/**`,
      with the module feature-flag gate and inbound tenant-header stripping
- [x] `apps/member-portal` shell created with the shared interceptors in
      `libs/shared` (npm workspace; Angular workspace root is the repo root)
- [x] Login / Register / Home screens ported from
      `docs/product/wireframes/member-portal-wireframes.html`
      (`.claude/skills/wireframe-to-angular`), calling real endpoints
- [x] **End-to-end proof:** register → select a Samaaj → log in → Home
      renders with that Samaaj's name, the member's name, the welcome
      notification the Kafka consumer raised, and only the modules that
      Samaaj has enabled. Verified in a browser against the compose stack.
      The subdomain redirect is implemented but exercised only by unit test
      locally, since `localhost` has no subdomains.
- [x] CI running build + test on every push (`.github/workflows/ci.yml`):
      per-service .NET build and test, the two frontend suites, a check that
      no EF model has drifted from its migrations, and the gateway smoke test
      against a stack built from scratch

> The through-the-gateway coverage CLAUDE.md §9 requires is
> `scripts/smoke-through-gateway.sh`, run against the compose stack and in CI.

**Exit criteria:** the Platform Foundation acceptance criteria in
`docs/product/requirements/samaajconnect-product-requirements.docx`
§11 are demonstrably true against running code, not just individually
unit tested.

## Phase 1 — Platform Foundation (remainder)

- [x] `member-family-service` scaffolded - profile created from the
      `identity.user.registered.v1` event, no synchronous call between the
      two services
  - [x] Profile update flow, with per-field privacy levels
  - [x] Family create / join-request / decide
  - [x] Child profile create
  - [x] Adult-child conversion request + approval flow, admin-approved. The
        member-family half is done and announces
        `members.child-conversion.approved.v1`; nothing consumes it yet, so
        the login is not created - see below.
- [x] identity-tenant-service consumes `members.child-conversion.approved.v1`
      and creates the login. With no notification channel, a Samaaj admin
      issues a one-time activation code (shown once, stored as a hash) and
      hands it over in person; redeeming it sets the first password and closes
      the loop back to member-family-service
- [x] DPDP Act: the compliance mapping (`docs/product/DPDP-COMPLIANCE.md`),
      versioned consent notice, per-purpose append-only consent records
      captured at registration, withdrawal, and per-service data export
- [x] DPDP Act: parental consent required to create a `ChildProfile` (s.9),
      data exports from all three services (s.11), and a published grievance
      contact per Samaaj (s.13)
- [x] DPDP Act: the right to erasure (s.8(7), s.12) - `POST /v1/identity/me/erase`,
      password-gated and with no admin in the way, fanning out over
      `identity.user.erased.v1` to clear the profile, the children held on that
      member's parental consent and the household link, delete their
      notifications, and de-identify rather than delete their audit rows
- [ ] DPDP Act, remaining: breach notification (s.8(6)) and the right to
      nominate (s.14), both of which need a notification channel first
- [x] DPDP Act: a member-portal surface for consent withdrawal, data export and
      erasure, at `/privacy`. No wireframe covers it — the prototype's
      `#profile` screen is per-field directory privacy, which is a different
      thing — so the screen was designed against the Act rather than translated.
      Withdrawing is one click with no confirmation, because s.6(4) requires it
      to be as easy as giving and giving was a tick; erasing asks for the
      password, because it cannot be undone. What erasure keeps is printed
      beside what it erased. The copy is assembled in the browser from the three
      services that hold it, since the platform deliberately has no single
      export endpoint. Driven end to end against the running stack, including a
      real erasure of a throwaway account
- [x] Admin backend: Super Admin tenant list (`GET /v1/identity/tenants`), a
      closed `ModuleCatalog` with runtime toggles, the read-only role and
      permission matrix, listing administrators, inviting one with a one-time
      activation code, and granting/revoking a role
- [x] Admin portal: the Angular SPA itself — sign-in, the Samaaj list with
      status and module toggles, Create Samaaj, administrators with role
      assignment, Invite Admin with its one-time code, the read-only role
      matrix, the adult-child conversion queue, and the audit log. Screens with
      no service appear in the nav, disabled, saying why
- [ ] Admin: an editable role and permission matrix. `GET /v1/identity/roles`
      reports what the backend enforces and says `editable: false`; making it
      editable needs per-tenant role definitions, an audit trail of matrix
      changes, and a floor of permissions no edit may remove. See
      "The admin surface" in `services/identity-tenant-service/CLAUDE.md`
- [x] `docs/product/SECURITY-CHECKLIST.md` pass on both Stage-0 +
      Phase-1 services. Every box walked against the code; the file now records
      what is asserted, what is missing and where each gap is tracked. Added on
      the way: a `TenantWriteGuard` that refuses a cross-tenant write at
      `SaveChanges` whatever the handler did, per-source rate limits at the
      gateway, before-state on corrections and status changes, audit events for
      data exports, and validation of the photo links that were previously only
      length-checked
- [x] Session revocation: refresh tokens stored as hashes, single-use and
      rotating, with reuse treated as theft and the whole chain revoked;
      `POST /v1/identity/logout` ending one session or all of them; erasure
      ending every session; and the access token down from 60 minutes to 15.
      Refreshing re-reads the account, its Samaaj and its roles, so a
      suspension or a revoked role bites within one token lifetime. Both
      portals renew silently rather than sending members to the login screen
- [x] Step-up authentication on deactivating a Samaaj — and on archiving one,
      which is the only status change that cannot be undone. Shared with
      erasure through `IStepUpAuthentication`. Two things came out of doing it:
      a failed step-up must answer 403 rather than 401, because the portals'
      interceptor renews the token on a 401 and *retries the original request*,
      which on these endpoints would resubmit the destructive command after a
      typo — erasure had this defect and now does not; and the step-up has to
      read the account past the tenant query filter, since a Super Admin's own
      account sits at `PlatformTenantId` while they act on a Samaaj

## Phase 2 — Social & Community Engagement

- [x] `timeline-service` (feed + moderation queue) — posting with the member/announcement split, the moderation queue that reported posts rejoin, comments, reactions and reporting. The platform's first module-gated route: switching `community` off makes the whole area answer 404
- [x] `volunteer-groups-service` — groups, the join-application flow, and the president's review queue. Introduced `VolunteerGroups.Lead`, a permission every member holds, because gating a president's own group on an admin permission made those endpoints unreachable
- [x] `events-service` (with capacity/waitlist) — draft/publish/cancel, RSVP and a waitlist that actually moves: giving up a place promotes whoever waited longest, and a promoted member keeps their queue position
- [x] `social-issues-service` (full Draft → Published workflow) — eight states declared as a transition table, with publishing reachable only from Approved, and an append-only history that answers "why was mine rejected?". The first service on its own module key
- [x] **Member portal: Timeline** (wireframe `#timeline`) — the feed with its
      three real states, composing, reactions, comments and reporting. The
      first member-facing screen for any service beyond sign-in, and the one
      that establishes the pattern the rest follow: a feature folder with its
      own `*.api.ts` and `*.models.ts`, wire types mirroring the service's
      responses, and per-item errors kept off the page-level error
- [x] **Member portal: Events** (wireframes `#events` and `#eventdetail`) - the
      list with the two states the wireframe drew and four it did not
      (cancelled, already going, already waiting, no capacity limit), and the
      detail screen with the real capacity bar. RSVP and joining the waitlist
      are one button and one call, because which of the two a member gets
      depends on a count the portal cannot see the current value of. Verified
      against the stack: RSVP, join a queue, and watch a place move down it
- [x] **Member portal: Volunteer Groups** (wireframes `#groups` and
      `#groupdetail`) - the directory with the reader's standing on each card,
      the detail screen with the apply flow, and **the president's review
      queue**, which no wireframe covers but without which every application a
      member sends sits unanswered forever. The queue is fetched only when the
      group says the reader leads it, because the endpoint answers 404 to
      anyone else. Verified against the stack: applied as a member, accepted
      with a position as the president
- [x] **Both Angular apps are containerised.** Neither had a Dockerfile, so
      neither could be deployed - the portals only ever ran from `ng serve` on
      a developer's machine. member-portal is an SSR Node image on 4200 whose
      server proxies `/v1` to the gateway, and the gateway also serves it at
      the root as the public front door; both are same-origin, which is what
      its production `gatewayUrl: ''` needs. admin-portal is a static build behind
      nginx on its own origin, proxying `/v1` to the gateway - deliberately not
      sharing an origin, because both apps use the same sessionStorage token
      keys and one origin would mean one session. Smoke checks cover the root,
      a deep link and `/health` not being swallowed
- [x] **Member portal: Social Issues** (wireframe `#issues`, plus a detail
      screen no wireframe covers) - the submission form, the published list,
      and "My Submissions" with the wireframe's progress strip. The strip is
      drawn only for the four states on the happy path; Rejected,
      ChangesRequested and Closed are where an issue leaves it, so those say so
      in words rather than showing an issue as partway to a publication it is
      not heading for. Every workflow button comes from the service's
      `availableTransitions`, so the portal holds no copy of the eight-state
      table. The detail screen surfaces the reviewer's reason at the top, which
      is what the append-only history was built to answer
- [x] **Member portal: Members and Family** (wireframes `#members`,
      `#memberdetail`, `#family`, `#children`) - the directory, one member's
      privacy-filtered profile, and the household with its children as one
      screen. Building it found that `GET /v1/members/{id}` had been in
      API-CONTRACTS.md since the contract was written and never implemented, so
      that shipped too, through the same per-field privacy mapper the directory
      uses. Adding a child shows the DPDP notice before the form and sends its
      version with the consent
- [x] **Member portal: Celebrities of Samaaj.** The campaign list and one
      detail screen carrying the wireframe's ballot and its results table,
      because they are the same campaign at two points in its life and a member
      arriving after publication wants the result where the ballot was. Every
      control reads `acceptsNominations`/`acceptsVotes` rather than the status,
      so a window that has closed cannot still offer a button. Driving it
      against the running stack found a **gateway** bug, not a portal one: a
      module-gated route answered 404 to a caller whose access token had merely
      expired, so the portals' renew-and-retry never fired and every gated
      screen said "No such endpoint." fifteen minutes after sign-in, for good
- [x] **Member portal: Jain Pathshala.** The directory with a parent's enrol
      request, and one enrolment screen carrying the wireframe's `#myclass`,
      `#attendance`, `#exams` and `#progress` — four views of one enrolment,
      whose own `#progress` already reprints the attendance percentage from
      `#attendance`. Waiting for a place is a first-class state: the screen
      reads `classId` and asks for neither the class nor the exams until there
      is one, because `my-class` answers 409 by design while a child is
      unplaced. The wireframe's "Events: 7 participated" tile is dropped —
      nothing records Pathshala event participation, so it would be a number
      the app made up
- [ ] **Member portal: the rest.** Boli has neither a service nor screens

## Phase 3 — Celebrity Voting

- [x] `celebrity-voting-service` — nominations with an approval step, one vote per member, and a result that is frozen when announced. The double-voting guarantee is a unique index on `(CampaignId, VoterMemberId)`, not the handler's check and not a Redis lock; the vote is written on its own scope so voters are not serialised by the request's transaction
- [ ] Load-test the vote-cast endpoint specifically (highest
      concurrency write path on the platform — see
      `docs/product/ROADMAP.md`). The **correctness** half is done:
      `ConcurrentVotingTests` proves twenty racing requests from one
      member leave exactly one vote, and `VoteIndexTests` proves the
      index is what refuses the second. What remains is throughput
      under sustained load, which needs a deployed environment rather
      than a Testcontainer — carried to Phase 5 hardening

## Phase 4 — Jain Pathshala

- [x] `pathshala-service` — the school, its sessions and classes, a two-step enrolment, the register and exams. Enrolment is a parent's request and a Pathshala's placement, because the Pathshala picks the class and because placing is the only check this service can make that a child is the caller's. Attendance is held to one mark per child per class day by a unique index; the register is written on one connection outside the request transaction, after two mistakes documented in the service's own `CLAUDE.md`
- [x] Teacher and Student "My…" views built in parallel (shared
      underlying data) — the backend for both. My Class, My Attendance,
      My Exams and My Progress are one set of queries over the same
      tables, gated on `Members.Read` and decided against the enrolment
      rather than on the `PathshalaStudent` role, which nothing grants.
      Progress is computed rather than stored, so a corrected mark
      cannot leave it quietly wrong
- [ ] The Angular screens for both, from the wireframes' `#myclass`,
      `#attendance`, `#exams` and `#progress`

## Phase 5 — Boli + Hardening

- [x] `boli-service` — occasions, Boli types, bidding, and a result that
      is recorded before it is announced. The correctness requirement here
      is not "one each" as in celebrity voting but **one highest**: a row
      lock on the Boli serialises bidders (deliberately the opposite of the
      vote path, which avoids serialising them — here it is the point),
      and a unique index on `(BoliId, Amount)` is what holds if a future
      code path forgets the lock or the service runs on two instances.
      Being outbid answers 200 with `accepted: false` and the amount now
      needed. Amounts are integer paise. The bid history never names who
      bid, and a recorded result names nobody until it is published
- [x] Member portal: the Boli screens - the hub, one Boli with its bid form
      and history, and an occasion screen the wireframe's "View Occasion"
      button had nothing behind it. Money is integer paise converted in one
      place; being outbid is an info notice with the new minimum already in the
      field, not a red error; and "You are leading" is only said while bidding
      is open, because on a closed-but-unpublished Boli it would announce the
      winner before the Samaaj did
- [x] Full `SECURITY-CHECKLIST.md` pass across every service. The previous pass
      covered three; six of the seven that shipped after it had never been
      re-checked. The mechanical properties held everywhere — all ten apply the
      tenant query filter by reflection, call `TenantWriteGuard` at
      `SaveChanges`, register the five behaviors in order, and fail closed on an
      unannotated request. Two findings, one fixed and one tracked below
- [x] **Fixed: the step-up was an unthrottled password oracle.** `/me/erase`
      and the tenant-status endpoint check a password and counted nothing, so
      anyone holding a borrowed access token could brute-force the account
      password at full speed without ever tripping the login lockout — turning
      the fifteen-minute window the stateless-token design accepts into a
      permanent credential compromise. `StepUpAuthentication` now shares the
      login lockout, and both paths carry the gateway's `credential-attempts`
      policy
- [ ] **Erasure does not reach six services.** `timeline`, `events`,
      `volunteer-groups`, `social-issues`, `celebrity-voting` and `boli` hold
      member ids and none consumes `identity.user.erased.v1`, though
      `DPDP-COMPLIANCE.md` states as a rule that a new consumer must subscribe
      the day it ships. `timeline` and `social-issues` are the real gap: they
      hold free text an erased member wrote. The other four hold a bare id that
      resolves to nobody once identity and member-family clear — probably
      sufficient, but that is counsel question 6, and for votes and bids the id
      cannot simply be removed anyway
- [ ] Full `SECURITY-CHECKLIST.md` re-pass once the erasure gap is closed
- [ ] HTTPS-only in production: TLS termination, HSTS, secure-cookie policy,
      and `ForwardedHeaders` so the gateway rate limiter partitions on the real
      caller rather than on the proxy
- [ ] Platform-hosted images, replacing the client-supplied `PhotoUrl` and
      `LogoUrl`. Those are now validated as absolute http(s) URLs, which stops
      `javascript:` links, but a photo hosted anywhere still sends every
      viewer's IP to that host - and on a `ChildProfile` that is the
      third-party tracking of children DPDP s.9(3) prohibits. Storage also makes
      the file-handling half of `SECURITY-CHECKLIST.md` live: size and type
      limits, virus scanning, and per-request authorization rather than an
      unguessable URL
- [ ] Tenant-isolation penetration testing (attempt cross-tenant IDOR
      on every write endpoint)
- [ ] Accessibility pass (WCAG 2.1 AA) on both Angular apps
- [ ] Backup/restore drill

---

## Open Decisions

Flagged in the requirements docs as suggestions, not yet resolved.
Resolve each before the phase that depends on it starts — don't let
these sit unresolved into the sprint that needs them.

- [x] **Adult-child conversion: admin-approved.** Decided 2026-08-28. A child
      who turns 18 requests conversion; a Samaaj admin approves it before the
      login is created. Safer default and easy to relax later.
- [ ] **Boli anti-abuse rules:** minimum bid increment + anti-sniping
      auto-extend window. Needed before Phase 5.
- [x] **Single domain, no per-Samaaj subdomain or CNAME.** Decided
      2026-08-28. A member signs in once and the system decides which Samaaj
      they belong to, because a login identifier is unique platform-wide.
      One certificate, no wildcard DNS, and the tenant travels in the token.
      This superseded the subdomain design in `docs/product/ARCHITECTURE.md`
      §3 and §6; root `CLAUDE.md` §6 is now the source of truth. The `Domain`
      column on `Tenant` is retained but unused.
- [x] **Eventing: native Kafka Outbox, no MassTransit.** Settled by
      `CLAUDE.md` §5 and built that way in every service.
- [ ] **DPDP Act, 2023 compliance review — required.** Confirmed 2026-08-28 as
      in scope. The technical capabilities are built: consent records, data
      export, and erasure across all three services. What is still open is not
      engineering - how the notice is worded, what retention periods apply,
      what makes parental consent "verifiable", and whether de-identifying
      audit rows is a defensible reading of the s.8(7) retention exception.
      The five questions at the end of `docs/product/DPDP-COMPLIANCE.md` need
      someone qualified in Indian data protection law, and must be answered
      before Phase 1 ships to real users.
