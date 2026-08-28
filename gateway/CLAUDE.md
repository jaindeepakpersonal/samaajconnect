# gateway

YARP reverse proxy. Single public entry point for both Angular apps and for
anything else talking to the platform.

Read the root `CLAUDE.md` §6 and `docs/product/ARCHITECTURE.md` §6 first — this
file covers how the gateway is built, not why the platform is shaped this way.

## What it does

1. **Tenant resolution.** The subdomain label becomes a Samaaj: `mahavir-samaj`
   from `mahavir-samaj.samaajconnect.com`. The slug is resolved through Redis,
   falling back to `identity-tenant-service`, and the result is injected as
   `X-Tenant-Id` (plus `X-Tenant-Slug`) for the service behind it.
2. **JWT validation.** Signature, issuer, audience and expiry. Nothing more —
   every service re-checks roles and permissions itself. The gateway is a
   filter, not the authorization boundary.
3. **Super Admin tenant override.** Handled here and logged on every request
   that carries one.
4. **Module feature-flag gate.** A route belonging to a module the Samaaj has
   switched off answers 404.

## Adding a service

Two blocks in `src/Sangam.Gateway/appsettings.json`, and nothing else:

```jsonc
"Routes": {
  "pathshala": {
    "ClusterId": "pathshala",
    "Match": { "Path": "/v1/pathshala/{**catch-all}" },
    // Only for routes a Samaaj can switch off. Omit for platform
    // infrastructure — nobody can disable their own ability to log in.
    "Metadata": { "module": "Pathshala" }
  }
},
"Clusters": {
  "pathshala": {
    "Destinations": { "primary": { "Address": "http://pathshala-service:8080/" } }
  }
}
```

The module key lives in route metadata rather than in a table in this project,
so `ModuleGateMiddleware` needs no change when a service is added. Keep the path
prefix identical to the one in `docs/product/SERVICES.md`.

Services host their endpoints at the absolute path (`/v1/identity/...`), not at
a gateway-relative one, so no path rewriting is configured and the same URL
works whether you curl a service directly or go through here.

## Pipeline order — load-bearing

```
UseAuthentication            → the override check needs to know if the caller is a Super Admin
TenantResolutionMiddleware   → strips forged headers, resolves the Samaaj, injects X-Tenant-Id
MapReverseProxy
  └── ModuleGateMiddleware   → inside the proxy pipeline, where the route metadata exists
```

`ModuleGateMiddleware` **must** stay inside the `MapReverseProxy` pipeline. Run
as ordinary middleware it executes before YARP has selected a route, so
`GetReverseProxyFeature()` returns null and every module route silently stops
being gated.

## Decisions worth knowing before you change this

**Inbound `X-Tenant-Id`, `X-Tenant-Override-Id` and `X-Tenant-Slug` are stripped
unconditionally, first, on every request** — including apex-host requests that
never get a tenant. Downstream services treat these headers as gateway-issued
facts. If a client could supply its own, it would pick its own Samaaj.

**Unknown and inactive Samaaj both return 404.** Not 403, and not a distinct
"inactive" status. Otherwise probing subdomains tells you which Samaaj exist but
are switched off.

**A disabled module returns 404, not 403.** Same reasoning: a Samaaj that runs
no Pathshala should be indistinguishable from a platform with no Pathshala
feature.

**"We could not check" is a 502, never a 404.** If `identity-tenant-service` is
unreachable, reporting "no such Samaaj" would turn one service being down into
every Samaaj appearing to have been deleted.

**Redis is a cache, never the source of truth.** A miss, a timeout, or Redis
being absent entirely degrades to a call to the identity service.
`NullTenantCache` is a working implementation, so no call site has to handle
"there is no cache" as a special case. Negative results are cached too, briefly,
or a probed subdomain becomes an unthrottled stream of lookups.

**Overrides are refused unless the request arrives on the admin host *and* the
caller holds the SuperAdmin role.** Both, not either.

**Only the first label of the host is read, and an IP address is never a slug.**
Otherwise in-cluster health checks against `10.1.2.3` would ask identity to
resolve a Samaaj called `10`.

## Configuration

| Key | Purpose |
|---|---|
| `Gateway__ApexHosts__n` | Hosts that carry no Samaaj — registration and login live here |
| `Gateway__AdminHost` | The only host from which a tenant override is accepted |
| `Gateway__IdentityServiceUrl` | Where slugs are resolved |
| `Gateway__TenantCacheSeconds` | Cache TTL; also how long deactivating a Samaaj takes to bite |
| `Redis__ConnectionString` | Optional. Absent means no caching, not a failure |
| `Jwt__SigningKey` | Must match the key `identity-tenant-service` signs with |

## Testing

`dotnet test gateway/Sangam.Gateway.sln` covers slug extraction, cache
behaviour, the middleware pipeline against a terminal endpoint that echoes what
a downstream service would receive, and the module gate.

The through-the-gateway coverage CLAUDE.md §9 asks for lives in
`scripts/smoke-through-gateway.sh`, which drives the real compose stack:

```bash
bash scripts/smoke-through-gateway.sh
```

It uses explicit `Host:` headers rather than DNS, so it needs no `/etc/hosts`
entries or wildcard record. Run it after adding any route — a service that works
when curled directly but was never wired in here is the failure this catches.
