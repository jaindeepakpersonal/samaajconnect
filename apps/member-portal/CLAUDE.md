# member-portal

The Angular app members use. Read the root `CLAUDE.md` §7 first for the
conventions this follows, and `.claude/skills/wireframe-to-angular/SKILL.md`
before translating another screen.

## Screens

| Screen | Route | Wireframe section | Status |
|---|---|---|---|
| Login | `/login` | `#login` | built |
| Register | `/register` | `#register` | built |
| Home | `/home` | `#home` | built |
| Forgot password / OTP | — | `#forgot`, `#otp` | not built — no endpoint exists |
| Profile, Members, Family, Timeline, … | — | various | not built |

## Where things live

```
apps/member-portal/src/app/
├── app.config.ts          providers, interceptor order
├── app.routes.ts          lazy routes; /home is behind authGuard
└── features/{feature}/    one folder per business module

libs/shared/src/           imported as @samaajconnect/shared
├── api/                   API_CONFIG token, problem-details mapping
├── auth/                  token store, interceptor, service, guard, models
└── tenant/                slug resolution and the URL interceptor
```

Anything both apps will need goes in `libs/shared`; anything only this app
needs does not. `admin-portal` has not been built yet, so treat that boundary
as a prediction to re-check when it is.

## Running it

The portal talks to the gateway, so bring the stack up first:

```bash
docker compose up -d --build
```

```bash
npm start
```

`npm test` runs both suites: `test:libs` (Vitest, for `libs/shared`) and
`test:app` (the Angular unit-test builder, also Vitest). They are separate
because the Angular builder only collects specs under the application it is
given, so the shared library would otherwise have nowhere to put its tests.

## Decisions worth knowing before you change this

**The dev server supplies the Samaaj, not a header.** ARCHITECTURE.md §7
sketches "an explicit header override" for local development. That was not
built: the gateway strips every inbound tenant header before routing, and that
is the control stopping a client from choosing its own Samaaj — a dev-only
exception to it would be a hole with a friendly name. `proxy.conf.json` sets
the `Host` header the gateway already resolves subdomains from instead. **To
develop against a different Samaaj, change the slug in that file.**

**A token is scoped to one Samaaj, so the interceptor drops it on
`403 Tenant.Mismatch` as well as on `401`.** Holding another Samaaj's token
does not merely fail the pages that need auth — the services refuse a
mismatched token before checking whether the request needed authentication at
all, so even the anonymous registration directory fails until it is cleared.

**The wireframe's numbers are not in the shipped Home.** It showed 1,248
members, 4 family, 6 events. Those services do not exist, and the skill is
explicit that prototype values must not be hardcoded, so a tile carries a count
only once something can supply one.

**Home hides modules the Samaaj has switched off.** The gateway already answers
404 for those routes, so offering the tile would be offering a door to a 404.
The "no modules enabled" notice is keyed off whether any *optional* module is
on, not off an empty tile list, because Members and Family are not module-gated
and are always present.

**Screens with no endpoint are disabled and say why.** The OTP tab and the
forgot-password link are both present, because they are in the signed-off
wireframe, and both explain that the feature is not available yet. Neither
calls anything. When the backend lands, wire them; do not fake them sooner.

**Login and Register are client-rendered, not prerendered.** They are forms
whose server-rendered output would be an empty shell, and Home is behind the
guard with a token that only exists in the browser. Prerendering them at build
time also means calling the gateway from the build, which is why the generated
default of `RenderMode.Prerender` fails. The SSR setup is kept for the public
screens still to come.

**`sessionStorage`, not `localStorage`, for the token.** A shared or family
device is common in this platform's audience, and a session that survives
closing the tab is longer than a community app needs.

## Accessibility

The requirements call for WCAG 2.1 AA. What is in place so far: labelled form
controls, `aria-invalid` on failed fields, `role="alert"` on errors and
`role="status"` on progress and confirmations, a visible focus ring that is
restyled rather than removed, and disabled controls carrying a `title`
explaining why. A full audit has not been done; that is a Phase 5 item in
`DEVELOPMENT_PLAN.md`.
