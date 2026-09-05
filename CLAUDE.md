# CLAUDE.md — samaajconnect

This is the root convention reference for the samaajconnect repo. Read
§4–6 before writing any backend code, and §7 before touching either
Angular app — this file *defines* the pattern; `.claude/skills/`
*generates* it. If a skill and this file ever disagree, this file wins
and the skill is out of date.

## 1. What this repo is

samaajconnect is a multi-tenant platform serving multiple Jain Samaaj
(community) organizations from one shared codebase. Ten backend
services, one YARP gateway, and two Angular apps (member portal, admin
panel). Business requirements and rationale live in `docs/product/` —
start with `docs/product/README.md`. Current build status lives in
`DEVELOPMENT_PLAN.md` at repo root.

## 2. Repo layout

```
samaajconnect/
├── CLAUDE.md                     (this file)
├── DEVELOPMENT_PLAN.md           (living status tracker)
├── docker-compose.yml
├── docs/
│   └── product/                  (architecture, data model, dev reference — see docs/product/README.md)
│       ├── wireframes/           (clickable HTML screen references)
│       └── requirements/         (narrative requirement docs, .docx)
├── gateway/
│   ├── CLAUDE.md
│   └── ...
├── services/
│   └── {name}-service/
│       ├── CLAUDE.md             (Purpose → Entities → Commands → Queries → Events → Endpoints → Dependencies → Testing)
│       ├── Dockerfile
│       ├── src/
│       │   ├── Sangam.{PascalName}.Api/
│       │   ├── Sangam.{PascalName}.Application/
│       │   ├── Sangam.{PascalName}.Domain/
│       │   └── Sangam.{PascalName}.Infrastructure/
│       └── tests/
├── apps/
│   ├── member-portal/            (Angular, SSR; Dockerfile; served by the gateway)
│   └── admin-portal/             (Angular, SPA; Dockerfile + nginx.conf; own origin)
├── libs/
│   └── shared/                   (CLAUDE.md; code both apps use - interceptors, tokens, money, module keys)
└── .claude/
    └── skills/
        ├── new-microservice/     (scaffold a new bounded-context service)
        ├── add-service-feature/  (add a command/query to an existing service)
        └── wireframe-to-angular/ (translate a wireframe screen into a real component)
```

The ten services are listed with their owned entities and endpoints in
`docs/product/SERVICES.md` — don't duplicate that list here; it drifts.

A service's own `CLAUDE.md` is what §10 sends a developer to before they touch
it, so its **Commands and Queries sections are a claimed complete list**, not a
sample. `scripts/service-docs.sh` holds them to that, and CI runs it — this is
§9's lesson about hand-written lists applied one level down.

## 3. Tech stack

Angular v18+ (standalone components, Signals) · YARP gateway (.NET
8/9) · .NET 8/9 Minimal API backend services · PostgreSQL (one logical
database per service) · Redis (voting locks, tenant cache) · Kafka via
the Outbox pattern (see §5 — no MassTransit). Full rationale in
`docs/product/ARCHITECTURE.md`.

## 4. Backend service conventions (CQRS/MediatR)

### 4.1 Record types & `Result<T>`

Commands and queries are C# records. Handlers return `Result<T>` —
never throw for expected business outcomes (e.g. "issue already
published"). Reserve exceptions for truly unexpected failures, which
`UnhandledExceptionBehavior` (§4.4) converts to a generic failure
result at the boundary.

### 4.2 Marker interfaces

```csharp
public interface ICommand<TResponse> : IRequest<Result<TResponse>> { }
public interface IQuery<TResponse> : IRequest<Result<TResponse>> { }
```

This is how `TransactionBehavior` tells commands and queries apart
without reflecting over naming conventions. Every command/query
implements one of these — don't implement `IRequest<T>` directly.

### 4.3 Validators

One FluentValidation validator per command/query, named
`{FeatureName}Validator`. `ValidationBehavior` collects all failures
into a single `Result.Failure` — validation never throws.

### 4.4 Pipeline behavior order — fixed, load-bearing

1. `LoggingBehavior` — structured log + correlation ID
2. `TenantAuthorizationBehavior` — role/permission check against the
   policy this request requires
3. `ValidationBehavior`
4. `TransactionBehavior` — commands only (`TRequest : ICommand<>`)
5. `UnhandledExceptionBehavior`

Tenant authorization runs before validation so an unauthorized caller
never learns anything about validation rules for data they can't
access. Validation runs before the transaction opens so an invalid
request never holds a DB transaction open. Do not reorder these
without updating this file and every service's `Program.cs` together.

**`scripts/pipeline-order.sh` checks that, and CI runs it.** The list
above is what it reads as the expected order — so this section is the
single source of truth, and the check fails whichever side moves. Until
2026-09-02 the rule was stated here and enforced by nothing, across ten
copies of one file.

### 4.5 Domain aggregates

```csharp
public sealed class {Aggregate} : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    private {Aggregate}() { }   // EF Core

    public static {Aggregate} Create(Guid tenantId, /* ... */)
    {
        var entity = new {Aggregate} { Id = Guid.NewGuid(), TenantId = tenantId /* ... */ };
        entity.Raise(new {Aggregate}CreatedDomainEvent(entity.Id, tenantId));
        return entity;
    }
}
```

State changes go through methods on the aggregate that call `Raise()`,
never through a handler setting properties directly.

### 4.6 Minimal API endpoints

Thin mapping only: bind request → build command/query → `sender.Send()`
→ `result.ToApiResult()`. One file per aggregate:
`Api/Endpoints/{Aggregate}Endpoints.cs`. No business logic in the
endpoint layer — if you're tempted to add an `if` beyond input
binding, it belongs in the handler or the domain.

## 5. Domain events & the Outbox

`AggregateRoot.Raise(IDomainEvent)` appends to an internal list.
`Infrastructure/Persistence` reads that list at `SaveChanges` time and
writes `OutboxMessages` rows **in the same transaction** as the state
change. A background `OutboxDispatcher` polls unsent rows, publishes
to Kafka via `KafkaProducer`, and marks them sent.

No MassTransit — this is a native Outbox + Kafka producer, copied
verbatim from any existing service. (`docs/product/ARCHITECTURE.md`
§4 has the full rationale for why this repo doesn't use MassTransit
despite an earlier requirements draft naming it.)

**A new domain event needs an entry in
`audit-notification-service/.../KnownEvents.cs`, not just a `Topic`
string.** Every event reaches the audit trail either way — an
undescribed topic still gets a row, with an action and entity name
derived from the topic string — but the derived default's
`EntityIdProperty` is `null`, explicitly, so that row carries no entity
id and no actor. Five cycles running found the same shape of gap behind
that line: a domain event whose own doc comment promised the audit
trail would answer "who did this?", silently unkept because nothing
fails when a descriptor is missing. **`scripts/audit-descriptor-coverage.sh`
checks that every topic has one, and CI runs it** — every one of the
platform's 50 topics does as of 2026-09-05, and this is what keeps a new
one from quietly joining the derived default instead.

## 6. Multi-tenancy (global query filter)

Every tenant-owned entity implements `ITenantScopedEntity { Guid
TenantId; }`. Each service's `DbContext` applies `HasQueryFilter` to
every such entity type in `OnModelCreating` (via reflection over
`ITenantScopedEntity` implementers — write this once per service, not
per entity). `ITenantContext.TenantId` is request-scoped, populated
from the `X-Tenant-Id` header the gateway injects.

**The platform runs on a single domain.** There is no Samaaj subdomain.
A member signs in once and the system decides which Samaaj they belong
to, because a login identifier is unique platform-wide and therefore
names exactly one Samaaj. The gateway reads the `tenant_id` claim off
the validated token and injects `X-Tenant-Id` from it; an anonymous
request (login, registration, the Samaaj directory) simply carries no
tenant. Registration is the one place a Samaaj is chosen by hand, from
the directory, and it is resolved server-side by slug.

> This supersedes the subdomain-per-Samaaj design in
> `docs/product/ARCHITECTURE.md` §3 and §6, which came from the original
> uploaded architecture doc. Anywhere those still say "subdomain", this
> file wins.

**Writes independently re-validate** the target entity's `TenantId`
against `ITenantContext.TenantId` in the handler — never rely on the
query filter alone for a write path. This is the IDOR guard; treat
skipping it as a blocking code review comment, not a style nitpick.

Super Admin tenant-override (`X-Tenant-Override-Id`) populates the
same `ITenantContext` — there is no separate "admin bypass" code path
in any service. The override is logged at the gateway on every request
that carries it.

On a single domain there is no admin hostname to gate the override by,
so the SuperAdmin role on the validated token is the whole gate. That
makes the audit log the only record of who acted on whose Samaaj, which
is why it is written on every overridden request rather than once per
session.

## 7. Frontend conventions (Angular)

- Standalone components, Signals for local state, `HttpClient` through
  the shared tenant interceptor in `libs/`.
- One feature folder per business module in each app, named to match
  the owning service where practical (e.g. a `family` feature folder
  pairs with `member-family-service`).
- **When building a screen, the matching screen in
  `docs/product/wireframes/*.html` is the spec** — translate its
  markup, copy, and flow directly rather than redesigning from
  scratch. See `.claude/skills/wireframe-to-angular/SKILL.md`.
- Role-aware rendering (guards/structural directives) is a UX
  convenience only. The backend check in §4.4/§6 is the actual
  authorization boundary — never let a guard be the only thing
  standing between a user and an action they shouldn't have.

## 8. Environment variables & docker-compose convention

- `ConnectionStrings__Default` per service, pointing at its own
  logical database: `samaajconnect_{name}`.
- `Kafka__BootstrapServers`, `Redis__ConnectionString` shared across
  all services via compose environment defaults.
- Each service's compose block `depends_on: postgres, kafka`.
- Gateway route + cluster config lives in `gateway/` per that folder's
  own `CLAUDE.md` — one route block per service, matching
  `/v1/{resource-prefix}/**`, plus the tenant module-flag key that
  route requires.

**The two Angular apps are containers too**, and they are not symmetrical.

- **member-portal** is server-side rendered (Node) and reachable two
  ways, both same-origin — which is what its production `ApiConfig`
  (`gatewayUrl: ''`) needs. On `http://localhost:4200` the SSR server
  proxies `/v1` to the gateway itself; on `http://localhost:8080` the
  gateway serves the portal at the root, as the public front door. That
  route is a catch-all with `Order: 1000`, so every `/v1/**` route and
  the gateway's own `/health` win over it.
- **admin-portal** is a static SPA served by nginx, which proxies
  `/v1` to the gateway. It is on its own origin
  (`http://localhost:4300`) **deliberately**: both apps use the same
  `TokenStore`, whose `sessionStorage` keys are
  `samaajconnect.token` and `samaajconnect.refresh`. Share an origin and
  an admin signing in overwrites a member's session in the same tab.

Neither uses `*service-defaults` — they need no database and no broker.

## 9. Testing conventions

- `{Service}.UnitTests` — handler logic, no I/O, no Testcontainers.
- `{Service}.IntegrationTests` — Testcontainers against a **real**
  Postgres, not mocks, for anything touching the Outbox or the tenant
  query filter. Those two are exactly where a subtle bug tends to only
  show up against a real database.
- At least one test per service curls its endpoint **through the
  gateway**, not only directly against the service — a route that
  works in isolation but isn't actually wired into the gateway is a
  common and easy-to-miss failure mode.

  **`scripts/service-coverage.sh` checks that, and CI runs it.** It reads the
  gateway's own route table to map each `/v1/{prefix}` to the service its
  cluster names, so a new service that nobody added a smoke section for fails
  on the day it lands. It also checks the two lists in `ci.yml` — the
  build/test matrix and the migrations job — because "is this service tested at
  all" and "is it tested through the gateway" are the same question asked twice.

  Until 2026-09-03 all three were stated in prose and enforced by nothing, and
  boli-service was missing from all three at once: no gateway coverage for
  eleven cycles while its module key was toggled on and off around it, and no
  CI entry, so 49 tests — including the ones holding "a Boli has exactly one
  highest bid" — ran nowhere but a developer's machine.

  The general lesson is worth more than the instance. **A hand-written list of
  the ten services is a list something will fall off**, and it will not fail
  when it does; it will simply be shorter. Every such list should be derived
  from the directories, or checked against them.

## 10. Where to look for what

| Need | Location |
|---|---|
| Business requirements & rationale | `docs/product/README.md` and the linked docs |
| DPDP Act obligations, what is built, what needs counsel | `docs/product/DPDP-COMPLIANCE.md` |
| Data model / entity fields | `docs/product/DATA-MODEL.md` |
| Per-service ownership, commands/queries/events | `docs/product/SERVICES.md` |
| REST endpoint list | `docs/product/API-CONTRACTS.md` |
| Security/authorization checklist, permission-key naming | `docs/product/SECURITY-CHECKLIST.md` |
| Phased plan (why) / live tracker (what's done) | `docs/product/ROADMAP.md` / `DEVELOPMENT_PLAN.md` |
| UI reference | `docs/product/wireframes/*.html` |
| Member-facing app conventions | `apps/member-portal/CLAUDE.md` |
| Code both apps share, and why it is shared | `libs/shared/CLAUDE.md` |
| Admin panel conventions | `apps/admin-portal/CLAUDE.md` |
| Module-gated service, worked example | `services/timeline-service/CLAUDE.md` |
| A correctness guarantee that is a database index | `services/celebrity-voting-service/CLAUDE.md` |
| An adapter seam with nothing real behind it yet | `services/audit-notification-service/CLAUDE.md` |
| State that cannot live on the row it describes | `services/audit-notification-service/CLAUDE.md` §"Read state" |
| A permission that is necessary but not sufficient | `services/pathshala-service/CLAUDE.md` |
| Permission that needs a data check beside it | `services/volunteer-groups-service/CLAUDE.md` |
| Capacity and a waitlist that moves | `services/events-service/CLAUDE.md` |
| A multi-state workflow as a transition table | `services/social-issues-service/CLAUDE.md` |
| Which endpoints no screen can reach | `scripts/unreachable-endpoints.sh` |
| Which fields nothing can write | `scripts/unwritable-fields.sh` |
| Which API-client methods no screen calls | `scripts/uncalled-api-methods.sh` |
| Which free text reaches a handler unvalidated | `scripts/validator-coverage.sh` |
| Whether every service still agrees with §4.4 | `scripts/pipeline-order.sh` |
| Whether the security checklist is still true | `scripts/security-invariants.sh` |
| Whether any service is quietly left out | `scripts/service-coverage.sh` |
| Whether a service documents everything it can be asked to do | `scripts/service-docs.sh` |
| Whether the module keys still agree in all three places | `scripts/module-keys.sh` |
| Which domain events fall through to audit-notification-service's derived default | `scripts/audit-descriptor-coverage.sh` |
| Scaffold a new bounded-context service | `.claude/skills/new-microservice/SKILL.md` |
| Add a command/query to an existing service | `.claude/skills/add-service-feature/SKILL.md` |
| Turn a wireframe screen into a real component | `.claude/skills/wireframe-to-angular/SKILL.md` |
| Domain vocabulary | `docs/product/GLOSSARY.md` |
