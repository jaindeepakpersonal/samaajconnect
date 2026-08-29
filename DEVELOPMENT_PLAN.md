# Development Plan — samaajconnect

Living document — check items off as they're completed, and keep
**Current Status** below up to date. This is the actionable/trackable
version of the plan; the reasoning behind the ordering lives in
`docs/product/ROADMAP.md` and shouldn't be duplicated here.

## Current Status

- **Stage:** Phase 3 service built; Phase 1 has four tracked leftovers
- **Last updated:** 2026-08-29 - celebrity-voting-service, the platform's eighth
  service and the first Phase 3 context. 770 tests green (705 backend, 65
  frontend) plus 195 smoke checks against a stack built from empty volumes,
  re-runnable against a dirty one.
- **Blocking item:** none. Next is the Phase 4 `pathshala-service`, or the four
  Phase 1 leftovers, which are smaller: step-up auth on deactivating a Samaaj, a
  member-portal surface for the DPDP rights, an editable role matrix, and the
  two DPDP obligations that need a notification channel first. The vote
  endpoint's throughput load test is carried to Phase 5, since it needs a
  deployed environment; its correctness half is done. The five questions in
  `docs/product/DPDP-COMPLIANCE.md` still need counsel before any of this ships
  to real users.

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
- [ ] DPDP Act: a member-portal surface for consent withdrawal, data export and
      erasure. The endpoints exist and are exercised end to end, but a right
      only reachable with curl is not one a member has. No wireframe covers an
      account/privacy screen, so this needs one first
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
- [ ] Step-up authentication on deactivating a Samaaj. Erasing an account
      already re-asks for the password; deactivating a whole Samaaj is at least
      as consequential and does not

## Phase 2 — Social & Community Engagement

- [x] `timeline-service` (feed + moderation queue) — posting with the member/announcement split, the moderation queue that reported posts rejoin, comments, reactions and reporting. The platform's first module-gated route: switching `community` off makes the whole area answer 404
- [x] `volunteer-groups-service` — groups, the join-application flow, and the president's review queue. Introduced `VolunteerGroups.Lead`, a permission every member holds, because gating a president's own group on an admin permission made those endpoints unreachable
- [x] `events-service` (with capacity/waitlist) — draft/publish/cancel, RSVP and a waitlist that actually moves: giving up a place promotes whoever waited longest, and a promoted member keeps their queue position
- [x] `social-issues-service` (full Draft → Published workflow) — eight states declared as a transition table, with publishing reachable only from Approved, and an append-only history that answers "why was mine rejected?". The first service on its own module key

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

- [ ] `pathshala-service`
- [ ] Teacher and Student "My…" views built in parallel (shared
      underlying data)

## Phase 5 — Boli + Hardening

- [ ] `boli-service`
- [ ] Full `SECURITY-CHECKLIST.md` pass across every service
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
