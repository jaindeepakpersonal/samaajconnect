---
name: add-service-feature
description: Add a new command or query to an existing samaajconnect backend service — one feature folder under Application/{Feature}/, wired through the same five pipeline behaviors as everything else in that service. Use this instead of new-microservice when the bounded context already exists and you're adding one capability to it (e.g. a new command on volunteer-groups-service). new-microservice explicitly excludes this case; this skill is what it points to instead.
---

# Add a feature to an existing service

Read the target service's own `CLAUDE.md`
(`services/{name}-service/CLAUDE.md`) first — it lists that service's
entities and current commands/queries. Also read the root `/CLAUDE.md`
§4 for the shared conventions this generates against. If anything here
and either of those disagree, they win and this skill is out of date.

## When to use this

- The bounded context already exists — there's already a
  `services/{name}-service/` folder.
- You're adding one command or query against an aggregate that service
  already owns, or a new aggregate that clearly belongs in that same
  bounded context (not a new one).
- Do **not** use this to add a new bounded context — use
  `new-microservice` for that. A sign you actually need
  `new-microservice` instead: the new aggregate has no natural owner
  among existing services, or it needs its own Postgres database
  rather than living alongside the target service's existing tables.

## Inputs to collect before generating anything

1. **Target service** — an existing folder under `services/`.
2. **Command or query?** — determines whether it implements
   `ICommand<TResponse>` or `IQuery<TResponse>` (root `CLAUDE.md` §4.2).
3. **Aggregate it operates on** — existing, or new-but-within-this-service.
4. **Role(s)/permission key that can invoke it** — check
   `docs/product/SECURITY-CHECKLIST.md` for the naming convention
   (`{Module}.{Action}`) and confirm whether the key already exists or
   needs to be added there too. This feeds
   `TenantAuthorizationBehavior` and the endpoint's
   `.RequireAuthorization()` policy.

## Steps

### 1. Locate or create the feature folder

```
src/Sangam.{PascalName}.Application/{Aggregate}/Commands/{FeatureName}/
    {FeatureName}Command.cs
    {FeatureName}CommandHandler.cs
    {FeatureName}CommandValidator.cs
```
(or `Queries/{FeatureName}/` with `Query`/`QueryHandler` instead of
`Command`/`CommandHandler`, and no validator unless the query genuinely
needs input validation). Copy the shape from an existing feature in
the *same* service rather than inventing a new folder layout — this
service already has a working example from its `new-microservice`
scaffold.

### 2. Command/query, handler, validator

Record types, `Result<T>` return, no exceptions for expected outcomes
— per root `CLAUDE.md` §4.1/§4.3. Implements the existing
`ICommand<TResponse>`/`IQuery<TResponse>` marker already defined in
this service's `Application/Common/` — don't redefine it per feature.

### 3. Wire the endpoint

Add a route to the existing
`Api/Endpoints/{Aggregate}Endpoints.cs` if that file already covers
this aggregate, or create it following the same thin-mapping pattern
as every other endpoint file in the repo: bind request → build
command/query → `sender.Send()` → `result.ToApiResult()` (root
`CLAUDE.md` §4.6).

### 4. Domain changes, if any

If the feature needs a new field or a new domain event on an existing
aggregate, add it to `Domain/{Aggregate}/{Aggregate}.cs` and raise the
event via a method on the aggregate itself — never set state directly
from the handler (root `CLAUDE.md` §4.5).

### 5. Migration

`dotnet ef migrations add {FeatureName}` only if the domain change
touched persisted state. Skip this step for pure read-side query
additions that don't change the schema.

### 6. Gateway

Most feature additions reuse an existing route prefix already
registered for this service and need no gateway change. If this is a
genuinely new route, add it in `gateway/` per that folder's own
`CLAUDE.md`.

### 7. Tests

One handler unit test in `{Service}.UnitTests`. Extend the existing
`{Aggregate}EndpointsTests.cs` integration test with this feature's
case rather than creating a parallel test file for one endpoint.

### 8. Update that service's own CLAUDE.md

Add the new command/query (and any new event) to its
Commands/Queries/Events sections. Keep the existing section order —
don't restructure a service's `CLAUDE.md` for the sake of one feature.

## Checklist before calling a feature "done"

- [ ] `dotnet build` / `dotnet test` pass for the whole service, not
      just the new feature's tests
- [ ] New permission key (if any) added to
      `docs/product/SECURITY-CHECKLIST.md`, not just hardcoded into
      the policy with no reference anywhere else
- [ ] Endpoint curl'd through the gateway, not only directly against
      the service
- [ ] Service's own `CLAUDE.md` updated, same section order as before
- [ ] If this feature emits a new domain event, confirm
      `audit-notification-service` has (or explicitly doesn't need) a
      consumer for it — a new event nobody consumes is usually a sign
      the audit trail just got a gap
