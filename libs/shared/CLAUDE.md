# libs/shared

The code both Angular apps use. Read root `CLAUDE.md` §7 first for the frontend
conventions this sits under.

## What belongs here

Something moves here **when the second app needs it, not in anticipation of
one**. That rule is in root `CLAUDE.md` §7 and it has fired exactly once so far:
`money.ts` lived in the member portal's Boli feature until the admin panel grew
a screen that opens a Boli.

What is here now:

| File | What it is |
|---|---|
| `api/api-config.ts` | `API_CONFIG` — where the gateway is, injected so each app and each test can point somewhere different |
| `api/problem-details.ts` | RFC 9457 problem documents → a sentence a member can read |
| `auth/auth.guard.ts` | Keeps signed-out visitors off member pages, remembering where they were going |
| `auth/auth.interceptor.ts` | Attaches the token; renews an expired one and retries |
| `auth/auth.models.ts` | The shapes identity-tenant-service answers with |
| `auth/auth.service.ts` | Sign in, sign out, and who the current user is |
| `auth/token.store.ts` | The session, in `sessionStorage` |
| `money/money.ts` | Paise ↔ rupees, and how long a Boli has left |
| `tenant/module-keys.ts` | The closed list of module keys |
| `tenant/tenant.interceptor.ts` | Rewrites relative API paths to the gateway |

## Testing

```bash
npm run test:libs
```

**Vitest, not `ng test`, and that is not a preference.** The Angular unit-test
builder only collects specs under the application it is given, so a library that
exists precisely because two applications share it would otherwise have nowhere
to put its tests. The config is `vitest.config.ts` at the repo root.

**The specs here are also compiled by both apps' builds**, because the apps
import this library by path. That means they are type-checked twice, and more
strictly the second time: a `PLATFORM_ID` provider typed `object` satisfied
vitest and failed `ng test member-portal`. If a spec here passes and an app
build then fails, look at the spec's types before anything else.

**Anything needing a full `TestBed` still belongs here if the code does.** The
config comment once said otherwise; `auth.interceptor.spec.ts` uses one, because
testing an interceptor through anything less than a real `HttpClient` tests a
re-implementation of it rather than the interceptor.

## Decisions worth knowing before you change this

**`module-keys.spec.ts` reads the gateway's `appsettings.json`.** It is the only
check in the repository that fails when the client's module keys and the
gateway's route metadata drift apart, and it exists because of what a wrong key
does: nothing at all. A screen filtering on a key the catalogue has never heard
of does not error — the filter simply never matches, so the feature is missing
from the portal for every Samaaj, forever, with nothing logged. Home filtered its
Events and Volunteer tiles on `Events` and `VolunteerGroups`, neither of which is
a module key, and both tiles were invisible to everybody until somebody noticed
by eye.

Adding a module still means three places: `ModuleCatalog` in
identity-tenant-service, the gateway route's `Metadata.module`, and
`ModuleKeys` here. The spec now catches two of the three disagreeing.

**The tenant interceptor attaches no tenant**, and the name is a leftover from
the subdomain design that root `CLAUDE.md` §6 supersedes. It rewrites relative
paths to the gateway and nothing else: a member's Samaaj travels in their token,
and the gateway strips every inbound tenant header precisely so a client cannot
choose its own. Its spec asserts the absence, so somebody "completing" it would
have to delete a test that says why.

**`TokenStore`'s two keys are why the apps are on separate origins.**
`sessionStorage` is scoped to an origin, so sharing one would let an admin
signing in overwrite a member's session in the same tab — and the panel would
then call every endpoint with a member's token. `apps/admin-portal/CLAUDE.md`
records the same decision from the other side.

**`set(token)` and `set(token, null)` mean different things.** Omitting the
refresh token means "I am not talking about it", which is what a plain renewal
sends; passing `null` means there is none. Collapsing the two would sign a member
out at their first token renewal.

**Every storage access is guarded, and the guard is not defensive
programming.** `sessionStorage` throws in private-browsing modes and does not
exist at all during server-side rendering, which the member portal does. Signing
in still works when it throws; the session just does not survive a reload.
