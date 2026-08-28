# Architecture

This describes how samaajconnect is put together technically. For *what*
each service does, see `SERVICES.md`. For entities, see `DATA-MODEL.md`.

## 1. High-level shape

```
        *.samaajconnect.com                admin.samaajconnect.com
       (Angular member portal, SSR)         (Angular admin app, SPA)
                    |                                  |
                    v                                  v
        --------------------------------------------------------
                    YARP API Gateway (.NET 8/9)
        - subdomain -> TenantId resolution (Redis-cached)
        - JWT validation
        - per-tenant module feature-flag gate
        - routes /v1/{resource}/** to the owning service
        --------------------------------------------------------
                    |         |         |          |
                    v         v         v          v
             identity  member-family  timeline  volunteer-groups  ...
             (each: Postgres DB, Clean Architecture, CQRS/MediatR)
                    \_________|_________|__________/
                              |
                        Kafka event bus
                              |
                              v
                 audit-notification-service
              (consumes every domain event; writes
               AuditLog rows; emits Notifications)
```

## 2. Clean Architecture layers (per service)

Each service has four projects, dependencies pointing inward:

- **Domain** — aggregates, domain events, no framework dependencies.
- **Application** — commands, queries, MediatR handlers, validators,
  the five pipeline behaviors, and the `ITenantContext`/`Result<T>`
  abstractions.
- **Infrastructure** — EF Core `DbContext` with the tenant global query
  filter, repositories, the Outbox dispatcher, Kafka producer.
- **Api** — thin Minimal API endpoints: bind request → build command →
  `sender.Send()` → map `Result<T>` to `IResult`.

See the `new-microservice` skill for the exact folder tree and starter
code — this doc explains the *why*, that skill generates the *what*.

## 3. CQRS + MediatR pipeline

Every command/query flows through five pipeline behaviors, in this
fixed order:

1. **LoggingBehavior** — structured log of request type + correlation ID
2. **TenantAuthorizationBehavior** — checks the caller's role/permission
   against the policy required for this request; runs *before*
   validation so unauthorized callers never trigger business-rule
   validation against data they shouldn't see
3. **ValidationBehavior** — FluentValidation-style input validation
4. **TransactionBehavior** — opens a DB transaction, but only for
   `ICommand<TResponse>` (queries don't mutate, so they skip this)
5. **UnhandledExceptionBehavior** — catches anything unhandled, logs it,
   converts to a generic `Result.Failure`

Commands and queries both return `Result<T>` — never throw for expected
business outcomes (e.g. "issue already published"). Reserve exceptions
for truly unexpected failures.

## 4. Eventing: Outbox → Kafka

A service that changes state writes its domain event to an
`OutboxMessages` table **in the same transaction** as the state change
(via `TransactionBehavior` + `AggregateRoot.Raise`). A background
`OutboxDispatcher` polls unsent rows and publishes them to Kafka, then
marks them sent. This guarantees "the event fires if and only if the
transaction committed" without a distributed transaction.

Downstream services — chiefly `audit-notification-service`, but also
any service that needs another's data locally — subscribe via Kafka
consumers in their own `EventHandlers/` folder.

> **Correction to the uploaded architecture doc:** the original System
> Architecture Design names RabbitMQ/Kafka *via MassTransit*. Your
> existing `new-microservice` skill scaffolds a native
> `OutboxDispatcher` + `KafkaProducer` with no MassTransit dependency.
> This doc follows the skill (i.e., your actual working convention) as
> the source of truth. If MassTransit is genuinely still the intended
> abstraction going forward, that's a one-line change to the skill and
> to this doc — just don't let the two drift, since the next engineer
> to scaffold a service will follow whichever one they read first.

## 5. Multi-tenancy vs. service decomposition — two different axes

These are easy to conflate; keep them separate:

- **Tenant isolation** (how one Samaaj is kept apart from another) is a
  **shared-database, discriminator-column** pattern *within* each
  service: every tenant-owned table has `TenantId`, and EF Core's
  `HasQueryFilter` applies it automatically to every read. Writes are
  additionally checked against the request's tenant context.
- **Service decomposition** (how the platform is split into
  microservices) is **database-per-service**: each of the ten services
  in `SERVICES.md` owns its own Postgres logical database
  (`samaajconnect_{name}`) and nobody else queries it directly.

A single Samaaj's data is therefore spread across ten databases (one
row per relevant service, all sharing that Samaaj's `TenantId`), not
consolidated into one "tenant database."

## 6. Gateway responsibilities

- **Tenant resolution:** extract subdomain slug → look up `TenantId` in
  Redis (backed by `identity-tenant-service`) → inject `X-Tenant-Id`
  header downstream. Reject unknown/inactive tenants at the gateway,
  before any service sees the request.
- **Auth:** validate the JWT signature and expiry; downstream services
  still re-check role/permission claims themselves (defense in depth —
  the gateway is not the only authorization boundary).
- **Super Admin tenant override:** `admin.samaajconnect.com` requests
  from a Super Admin may carry `X-Tenant-Override-Id`; the gateway logs
  this explicitly so it's auditable, and every downstream service
  treats it exactly like a normal `X-Tenant-Id` — there is no separate
  "admin bypass" code path in the services themselves.
- **Module feature-flag gate:** each tenant has an `EnabledModules`
  list (e.g. a Samaaj with no Pathshala program disables that module).
  The gateway rejects routes for disabled modules with a 404 rather
  than a 403, so a disabled module isn't distinguishable from a
  route that doesn't exist.

## 7. Frontend architecture

- **Monorepo:** `/apps/member-portal` (SSR), `/apps/admin-portal`
  (SPA), `/libs` for shared UI + HTTP interceptors.
- **Tenant interceptor:** reads the resolved subdomain and attaches it
  to outgoing requests (used for local dev against non-subdomain URLs
  too, via an explicit header override).
- **Role-aware rendering:** route guards + structural directives hide
  navigation/actions based on JWT claims — but this is a UX
  convenience, never the actual authorization boundary (see
  `SECURITY-CHECKLIST.md`).

## 8. Suggested additions beyond the original architecture doc

These aren't in the uploaded docs; flagging them here because they
tend to be expensive to retrofit later:

- **Observability:** OpenTelemetry tracing across gateway → service →
  Kafka consumer, correlated by the `LoggingBehavior`'s correlation ID.
  Without this, a slow request spanning 3 services is very hard to
  debug post-launch.
- **Resilience:** Polly retry/circuit-breaker policies on any
  synchronous inter-service call and on the gateway → service hop.
- **API versioning:** the `/v1/` prefix is already in the route tables
  in `SERVICES.md` — keep it from day one, not after the first breaking
  change forces it.
- **Testing:** each service's `IntegrationTests` project should use
  Testcontainers against a real Postgres (per the skill's checklist),
  not mocks, for anything touching the Outbox or the tenant query
  filter — those are exactly the two things most likely to have a
  subtle bug that only shows up against a real database.
