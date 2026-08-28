# Development Plan — samaajconnect

Living document — check items off as they're completed, and keep
**Current Status** below up to date. This is the actionable/trackable
version of the plan; the reasoning behind the ordering lives in
`docs/product/ROADMAP.md` and shouldn't be duplicated here.

## Current Status

- **Stage:** Stage 0 complete; starting Phase 1 - Platform Foundation
- **Last updated:** 2026-08-28 - CI wired up. Register, sign in, land on your
  Samaaj Home, all through the gateway. 258 tests green (214 backend, 44
  frontend) plus 14 smoke checks against a stack built from scratch.
- **Blocking item:** none. Stage 0 is complete. Next up is Phase 1:
  `member-family-service` (profile, family, children).

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

- [ ] `member-family-service` scaffolded
  - [ ] Profile update flow
  - [ ] Family create / join-request
  - [ ] Child profile create
  - [ ] Adult-child conversion request + approval flow (see Open
        Decisions below — default assumption is admin-approved)
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

- [ ] **Adult-child conversion:** admin-approved vs. self-service.
      Recommended: admin-approved initially (safer default, easy to
      relax later). Needed before Phase 1 Children work.
- [ ] **Boli anti-abuse rules:** minimum bid increment + anti-sniping
      auto-extend window. Needed before Phase 5.
- [ ] **Custom domain (CNAME) per tenant:** needed before the SSL/cert
      strategy is finalized — decide early even though it's low
      priority, since it's expensive to retrofit.
- [ ] **Eventing library:** confirm native Kafka Outbox (current
      convention, see `CLAUDE.md` §5) vs. MassTransit and make sure
      the skill and this repo agree — don't let them drift.
- [ ] **DPDP Act, 2023 compliance review** (India's data protection
      law) — confirm with someone qualified how consent records and
      data export/erasure requests should work before Phase 1 ships
      to real users.
