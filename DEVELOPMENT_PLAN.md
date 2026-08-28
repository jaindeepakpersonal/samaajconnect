# Development Plan — samaajconnect

Living document — check items off as they're completed, and keep
**Current Status** below up to date. This is the actionable/trackable
version of the plan; the reasoning behind the ordering lives in
`docs/product/ROADMAP.md` and shouldn't be duplicated here.

## Current Status

- **Stage:** Stage 0 - Walking Skeleton, in progress
- **Last updated:** 2026-08-28 - `identity-tenant-service` complete for Stage 0:
  Tenant + User + roles/permissions, register, common login, JWT issuance,
  Super Admin bootstrap. 135 tests green.
- **Blocking item:** none. Next up is `audit-notification-service` (one Kafka
  consumer end to end), then the gateway.

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
- [ ] `audit-notification-service` scaffolded (shell is fine — one
      event consumer wired end-to-end is the point, not full coverage)
- [ ] Gateway: subdomain → tenant resolution + JWT validation wired
      for `/v1/identity/**` and `/v1/audit/**` only
- [ ] `apps/member-portal` shell created with the tenant interceptor
- [ ] Login / Register / Home screens ported from
      `docs/product/wireframes/member-portal-wireframes.html`
      (`.claude/skills/wireframe-to-angular`), calling real endpoints
- [ ] **End-to-end proof:** register → select a Samaaj → log in →
      redirected to that Samaaj's subdomain → Home renders
- [ ] CI running build + test on every push

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
