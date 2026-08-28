# Development Plan — samaajconnect

Living document — check items off as they're completed, and keep
**Current Status** below up to date. This is the actionable/trackable
version of the plan; the reasoning behind the ordering lives in
`docs/product/ROADMAP.md` and shouldn't be duplicated here.

## Current Status

- **Stage:** Stage 0 complete; starting Phase 1 - Platform Foundation
- **Last updated:** 2026-08-28 - DPDP: parental consent on child records,
  exports from all three services, and a published grievance contact. 461
  tests green (413 backend, 48 frontend) plus 43 smoke checks against a stack
  built from scratch.
- **Blocking item:** erasure is genuinely blocked on question 1 in
  `docs/product/DPDP-COMPLIANCE.md` - whether de-identifying audit rows counts
  as erasure. The other four questions there need counsel before Phase 1 ships
  to real users but block nothing today. Next unblocked work: the admin portal.

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
- [ ] DPDP Act, remaining: erasure requests, including de-identifying audit
      rows rather than deleting them. **Waiting on counsel** - question 1 in
      `docs/product/DPDP-COMPLIANCE.md` decides the design, and building it
      twice is the waste worth avoiding
- [ ] DPDP Act, remaining: breach notification (s.8(6)) and the right to
      nominate (s.14), both of which need a notification channel first
- [ ] Admin: tenant CRUD screens
- [ ] Admin: admin user + role assignment screens
- [ ] `docs/product/SECURITY-CHECKLIST.md` pass on both Stage-0 +
      Phase-1 services

## Phase 2 — Social & Community Engagement

- [ ] `timeline-service` (feed + moderation queue)
- [ ] `volunteer-groups-service`
- [ ] `events-service` (with capacity/waitlist)
- [ ] `social-issues-service` (full Draft → Published workflow)

## Phase 3 — Celebrity Voting

- [ ] `celebrity-voting-service`
- [ ] Load-test the vote-cast endpoint specifically (highest
      concurrency write path on the platform — see
      `docs/product/ROADMAP.md`)

## Phase 4 — Jain Pathshala

- [ ] `pathshala-service`
- [ ] Teacher and Student "My…" views built in parallel (shared
      underlying data)

## Phase 5 — Boli + Hardening

- [ ] `boli-service`
- [ ] Full `SECURITY-CHECKLIST.md` pass across every service
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
      in scope. The technical capabilities (consent records, data export,
      erasure) are ours to build; how they are worded and what retention
      periods apply still needs someone qualified in Indian data protection
      law. Must land before Phase 1 ships to real users.
