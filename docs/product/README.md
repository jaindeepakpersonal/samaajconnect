# samaajconnect — Developer Quickstart

samaajconnect is a multi-tenant platform serving multiple Jain Samaaj
(community) organizations from one shared application, with each Samaaj
operating as an isolated tenant on its own subdomain.

This folder is the **dev-ready reference set**: read this first, then
`ARCHITECTURE.md` and `SERVICES.md` before writing any code. Requirements
live in `./requirements/`, and clickable wireframes live in
`./wireframes/`.

> **Convention note:** These docs assume samaajconnect is built the same
> way your existing Sangam platform builds backend services — Clean
> Architecture + CQRS/MediatR, the standard `src/`/`tests/` service
> layout, five pipeline behaviors, Outbox → Kafka for events, and a YARP
> gateway — per your `new-microservice` skill. If samaajconnect is a
> fresh repo rather than a new bounded context inside the existing Sangam
> monorepo, the same shape still applies; just start each service with
> that skill instead of assuming a shared gateway/docker-compose already
> exists.

## 1. What this system does

One Angular application serves member-facing traffic on
`*.samaajconnect.com` (or your production domain), and one Angular admin
app serves `admin.samaajconnect.com`. A member registers into exactly one
Samaaj; after login they're routed to that Samaaj's subdomain. Nine
domain services own the platform's bounded contexts, plus one
cross-cutting Audit & Notification service that consumes events from all
of them.

## 2. Tech stack

| Layer | Technology |
|---|---|
| Frontend (member + admin) | Angular v18+, standalone components, Signals, SSR for the member portal |
| API Gateway | YARP (.NET 8/9) — subdomain → tenant resolution, JWT validation, module feature-flag gate |
| Backend services | .NET 8/9 Minimal API, Clean Architecture, CQRS via MediatR |
| Data | PostgreSQL (one logical database per service), Redis (voting locks, session/tenant cache) |
| Eventing | Kafka, via the Outbox pattern (see `ARCHITECTURE.md` §4) |
| Auth | JWT (tenant-scoped claims), OTP as a suggested addition — see requirements doc |

## 3. Module → service map

| Business module (PRD §6) | Service | Folder |
|---|---|---|
| Tenant admin, register/login, roles | Identity & Tenant | `services/identity-tenant-service/` |
| Profile, Members directory, Family, Children | Member & Family | `services/member-family-service/` |
| Timeline, moderation, announcements | Timeline | `services/timeline-service/` |
| Volunteer Groups | Volunteer Groups | `services/volunteer-groups-service/` |
| Events | Events | `services/events-service/` |
| Social Issues | Social Issues | `services/social-issues-service/` |
| Celebrities of Samaaj / Voting | Celebrity Voting | `services/celebrity-voting-service/` |
| Jain Pathshala | Pathshala | `services/pathshala-service/` |
| Auctions / Boli | Boli | `services/boli-service/` |
| Audit trail, notifications (cross-cutting, consumes events from all above) | Audit & Notification | `services/audit-notification-service/` |

Full entity ownership, commands/queries, and events per service are in
`SERVICES.md`. The full ER model is in `DATA-MODEL.md`.

## 4. Local dev quickstart (per service)

```bash
# scaffold a new service (first time only) — see the new-microservice skill
# then, for day-to-day work on an existing service:
cd services/{name}-service
dotnet build
dotnet test
dotnet run --project src/Sangam.{PascalName}.Api
# Swagger/OpenAPI at http://localhost:{port}/swagger
```

Bring up shared infra (Postgres, Redis, Kafka, gateway) with
`docker-compose up` from the repo root once services are wired in per
`ARCHITECTURE.md` §6.

## 5. Build order (suggested)

Follow `ROADMAP.md` for the phased plan. In short: Identity & Tenant
first (nothing else works without tenant resolution + auth), then
Member & Family, then the Phase 2 community modules, then Celebrity
Voting, then Pathshala, then Boli, with Audit & Notification stood up
early (Phase 1) since every later service needs somewhere to publish
audit events from day one rather than bolting it on retroactively.

## 6. Other docs in this set

- `ARCHITECTURE.md` — Clean Architecture layers, CQRS pattern, pipeline
  behaviors, eventing, gateway routing, cross-cutting concerns.
- `DATA-MODEL.md` — full ER diagram and entity field reference.
- `SERVICES.md` — one reference page per service (entities,
  commands/queries, events, endpoints, roles).
- `API-CONTRACTS.md` — REST endpoint tables per service.
- `SECURITY-CHECKLIST.md` — tenant isolation and authorization rules as
  a dev checklist, plus the permission-key naming convention.
- `ROADMAP.md` — phased delivery plan mapped to services.
- `GLOSSARY.md` — domain terms (Samaaj, Boli, Pathshala, etc.) for
  anyone new to the domain.
