# Roadmap

Refined from the PRD's MVP plan, mapped to the services in `SERVICES.md`
so each phase has a concrete scaffolding order.

## Phase 1 — Platform foundation

Nothing else works without this phase.

- Scaffold `identity-tenant-service` (Tenant, User, Role, Permission)
- Scaffold `audit-notification-service` early, not last — every
  subsequent service needs an Outbox consumer to publish to from day
  one; retrofitting audit logging after five services exist is
  expensive and error-prone.
- Gateway: subdomain resolution, JWT validation, tenant-override
  handling for Super Admin
- Scaffold `member-family-service` (Profile, Family, Children —
  including the adult-child conversion flow)
- Admin: tenant CRUD, admin user + role assignment
- **Exit criteria:** a member can register into Samaaj A, log in, land
  on Samaaj A's subdomain, and cannot see Samaaj B's member directory.

## Phase 2 — Social & community engagement

- Scaffold `timeline-service` (feed, moderation queue)
- Scaffold `volunteer-groups-service`
- Scaffold `events-service`
- Scaffold `social-issues-service` (full Draft → Published workflow)
- **Exit criteria:** a member can post (pending moderation), apply to a
  volunteer group, RSVP to an event, and submit a social issue that
  requires admin approval before it's publicly visible.

## Phase 3 — Celebrity voting

- Scaffold `celebrity-voting-service`
- Load-test the vote-casting endpoint specifically — this is the
  highest-concurrency write path in the whole platform (a campaign
  close typically produces a burst of last-minute votes)
- **Exit criteria:** duplicate votes are rejected under concurrent load,
  and Top 10 publication is a one-way, audited action.

## Phase 4 — Jain Pathshala

- Scaffold `pathshala-service`
- Build out Teacher and Student "My..." views in parallel since they
  share the same underlying enrollment/attendance/exam data
- **Exit criteria:** Super Admin creates a Pathshala, a parent enrolls
  an eligible child, a teacher marks attendance and records an exam
  result, and the student/parent see it reflected in "My Progress."

## Phase 5 — Auctions / Boli + hardening

- Scaffold `boli-service`
- Security hardening pass: full run through `SECURITY-CHECKLIST.md`
  across every service, not just the new one
- Tenant-isolation penetration testing (attempt cross-tenant IDOR on
  every write endpoint)
- Accessibility pass on both Angular apps (see requirements doc NFRs)
- Backup/restore drill, monitoring/alerting wired to the observability
  additions in `ARCHITECTURE.md` §8
- **Exit criteria:** Boli results are recorded, reviewed, and published
  only through the authorized manager action, and are locked afterward
  except through a distinct correction workflow.

## Ordering rationale

Phase order follows dependency, not just PRD numbering: Pathshala (§6.13
in the PRD) depends on Family/Children (Phase 1) for enrollment, so it
can't move earlier even though it's a headline feature. Boli has no
dependency on Pathshala or Celebrity Voting and could in principle be
pulled forward if it's a higher business priority than voting — flag
that tradeoff explicitly if the phase order needs to change, since the
services are largely independent once Phase 1 exists.
