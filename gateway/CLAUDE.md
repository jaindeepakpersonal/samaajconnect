# gateway

YARP reverse proxy. Single public entry point for both Angular apps and for
anything else talking to the platform.

Read the root `CLAUDE.md` §6 and `docs/product/ARCHITECTURE.md` §6 first — this
file covers how the gateway is built, not why the platform is shaped this way.

## What it does

1. **Tenant resolution.** The platform runs on **one domain**. A member signs
   in and the token they get names their Samaaj, so the gateway reads the
   `tenant_id` claim, confirms the Samaaj is still active (through Redis,
   falling back to `identity-tenant-service`), and injects `X-Tenant-Id` plus
   `X-Tenant-Slug` for the service behind it. Anonymous requests carry no
   tenant. There is no subdomain — see root `CLAUDE.md` §6.
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
    "Metadata": { "module": "pathshala" }
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

**Never put a `"//"` comment key inside `Routes` or `Clusters`.** The
convention works at the top level of `appsettings.json`, where nothing binds
the value. Inside those two objects every key is a route or cluster name, and
YARP refuses to start on a route with no `Path` - so the gateway crash-loops
with `Route '//x' requires Hosts or Path specified`. Put the explanation in
this file instead.

**The key must be one of the keys in `ModuleCatalog`** in
identity-tenant-service's domain. That is the closed list a Samaaj's enabled
modules are validated against, so a route whose metadata names a key no Samaaj
can ever have enabled answers 404 to everybody, forever, with nothing logged.
Comparison is case-insensitive, but write the key in the catalogue's own
lowercase spelling. Adding a module means adding it in both places.

Services host their endpoints at the absolute path (`/v1/identity/...`), not at
a gateway-relative one, so no path rewriting is configured and the same URL
works whether you curl a service directly or go through here.

## Pipeline order — load-bearing

```
UseAuthentication            → the override check needs to know if the caller is a Super Admin
UseRateLimiter               → before tenant resolution: a rejected request should not cost a Samaaj lookup
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
unconditionally, first, on every request** — including anonymous ones that never
get a tenant. Downstream services treat these headers as gateway-issued facts.
If a client could supply its own, it would pick its own Samaaj.

**A token naming a missing or inactive Samaaj returns 403, not 404.** The
caller holds a valid token, so this is "your Samaaj is not available", not "no
such address". Both cases answer the same way, so a deactivation is not
distinguishable from a deletion.

**A disabled module returns 404, not 403.** Same reasoning: a Samaaj that runs
no Pathshala should be indistinguishable from a platform with no Pathshala
feature.

**"We could not check" is a 502, never a 403.** If `identity-tenant-service` is
unreachable, reporting "your Samaaj is unavailable" would turn one service being
down into every member on the platform being locked out.

**Redis is a cache, never the source of truth.** A miss, a timeout, or Redis
being absent entirely degrades to a call to the identity service.
`NullTenantCache` is a working implementation, so no call site has to handle
"there is no cache" as a special case. Negative results are cached too, briefly,
or a token naming a deleted Samaaj becomes an unthrottled stream of lookups.

**The SuperAdmin role is the whole gate on an override.** With one domain there
is no admin hostname to check as well, which makes the audit log the only
record of who acted on whose Samaaj — hence logging it on every overridden
request rather than once per session.

**`MapInboundClaims` is off.** Left on, `JwtSecurityTokenHandler` rewrites
`role` to the long WS-Federation URI and every role check here silently stops
matching. That is not hypothetical: the override check passed its unit tests
and failed against a real token, because the tests built the principal by hand.

**Rate limiting is per source address and deliberately loose.** `/login`,
`/activations/redeem` and `/register` carry policies; everything else is
unlimited. The limits are set well above any plausible human volume because
Indian mobile carriers put very large numbers of subscribers behind one address
- they exist to make scripted attacks expensive, not to police individuals, and
the per-account lockout in identity-tenant-service is what protects a specific
person. Put a proxy in front of this and you must configure
`ForwardedHeaders`, or every request partitions into one bucket and the limit
becomes a global cap.

Those three routes are declared above the identity catch-all with a lower
`Order` purely so they can carry a policy; they proxy to the same cluster.

## Configuration

| Key | Purpose |
|---|---|
| `Gateway__IdentityServiceUrl` | Where tenant ids are resolved |
| `Gateway__TenantCacheSeconds` | Cache TTL; also how long deactivating a Samaaj takes to bite |
| `Redis__ConnectionString` | Optional. Absent means no caching, not a failure |
| `Jwt__SigningKey` | Must match the key `identity-tenant-service` signs with |
| `RateLimiting__Enabled` | Set false only in a host that must make hundreds of credential attempts on purpose |
| `RateLimiting__CredentialAttemptsPerWindow` | Sign-in and activation attempts per source per window |
| `RateLimiting__RegistrationsPerWindow` | Registrations per source per window |
| `RateLimiting__WindowSeconds` | Window length, default 60 |

## Testing

`dotnet test gateway/Sangam.Gateway.sln` covers cache behaviour, the middleware
pipeline against a terminal endpoint that echoes what a downstream service would
receive, and the module gate.

The through-the-gateway coverage CLAUDE.md §9 asks for lives in
`scripts/smoke-through-gateway.sh`, which drives the real compose stack:

```bash
bash scripts/smoke-through-gateway.sh
```

One domain means no `Host:` juggling: it signs in and lets the token decide the
Samaaj, exactly as a browser would. Run it after adding any route — a service
that works when curled directly but was never wired in here is the failure this
catches.
