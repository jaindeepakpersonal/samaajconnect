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
| Timeline | `/timeline` | `#timeline` | built |
| Forgot password / OTP | — | `#forgot`, `#otp` | not built — no endpoint exists |
| Profile, Members, Family, Events, Groups, Issues, Celebrity, Pathshala, Boli, Notifications | — | various | not built — the services exist, the screens do not |

## Where things live

```
apps/member-portal/src/app/
├── app.config.ts          providers, interceptor order
├── app.routes.ts          lazy routes; /home is behind authGuard
└── features/{feature}/    one folder per business module

libs/shared/src/           imported as @samaajconnect/shared
├── api/                   API_CONFIG token, problem-details mapping
├── auth/                  token store, interceptor, service, guard, models
└── tenant/                the URL interceptor
```

Anything both apps will need goes in `libs/shared`; anything only this app
needs does not. `admin-portal` exists now, and the boundary held: it shares the
API config, the problem-details mapping, the token store, the auth service,
guard and interceptors, and keeps its own models and its Samaaj-scope
interceptor to itself.

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

**The client says nothing about the Samaaj.** The platform runs on one domain
and a member's Samaaj travels in their token, so there is nothing to attach and
nothing to configure. `proxy.conf.json` only points `/v1` at the gateway's port.
To work as a different Samaaj, sign in as a member of it.

**A token is scoped to one Samaaj, so the interceptor drops it on
`403 Tenant.Mismatch` as well as on `401`.** Less likely now that the tenant
comes from the token rather than the host, but still the right behaviour: the
services refuse a mismatched token before checking whether the request needed
authentication at all, so even the anonymous registration directory would fail
until it is cleared.

**The wireframe's numbers are not in the shipped Home.** It showed 1,248
members, 4 family, 6 events. Those services do not exist, and the skill is
explicit that prototype values must not be hardcoded, so a tile carries a count
only once something can supply one.

**Home hides modules the Samaaj has switched off.** The gateway already answers
404 for those routes, so offering the tile would be offering a door to a 404.
The "no modules enabled" notice is keyed off whether any *optional* module is
on, not off an empty tile list, because Members and Family are not module-gated
and are always present.

**A module key is `ModuleKeys.X`, never a string literal.** Home filtered its
Events and Volunteer tiles on `Events` and `VolunteerGroups`, neither of which
is a key the platform has ever had - both features are behind `community`. The
filter did not fail; it simply never matched, so both tiles were invisible to
every Samaaj, forever, with nothing logged anywhere. `libs/shared`'s
`ModuleKeys` now mirrors `ModuleCatalog` and `ModuleTile.moduleKey` is typed to
it, which makes the same mistake a compile error.

**Three bugs in this app have had one shape: the portal inventing a value the
platform does not have.** The module keys above; a post status of `Pending`
where the service serialises `PendingReview`; and reactions called `Like` and
`Pray` where `ReactionType` is a closed enum of `Appreciate`, `Support` and
`Celebrate`. None of the three is a type error - they are strings compared
against strings on the wire - so none failed loudly. The module keys were caught
by reading the catalogue, and the other two only by opening the screen against a
running stack. **When a screen compares against a name the backend produced,
check the enum, and open the page.**

**Screens with no endpoint are disabled and say why.** The OTP tab and the
forgot-password link are both present, because they are in the signed-off
wireframe, and both explain that the feature is not available yet. Neither
calls anything. When the backend lands, wire them; do not fake them sooner.

**After signing in the portal simply navigates to Home.** The wireframe sent a
member to their Samaaj's subdomain; there is no subdomain now, so there is
nowhere else to go.

**Login and Register are client-rendered, not prerendered.** They are forms
whose server-rendered output would be an empty shell, and Home is behind the
guard with a token that only exists in the browser. Prerendering them at build
time also means calling the gateway from the build, which is why the generated
default of `RenderMode.Prerender` fails. The SSR setup is kept for the public
screens still to come.

**An expired access token no longer means the login screen.** Access tokens
last fifteen minutes; the interceptor spends a refresh token, retries the
original request, and the member notices nothing. It ends the session only
when there is no refresh token, or the refresh is itself refused. Signing out
now calls the server as well as clearing storage - clearing alone left the
refresh token live for a fortnight, which is not signing out.

**`sessionStorage`, not `localStorage`, for the tokens.** A shared or family
device is common in this platform's audience, and a session that survives
closing the tab is longer than a community app needs. That goes double for the refresh token,
which can mint access tokens for a fortnight. Not a cookie either: a cookie
is sent automatically on every request, which is what makes cookies useful
and what makes them need CSRF protection - and this platform has none.

## Accessibility

The requirements call for WCAG 2.1 AA. What is in place so far: labelled form
controls, `aria-invalid` on failed fields, `role="alert"` on errors and
`role="status"` on progress and confirmations, a visible focus ring that is
restyled rather than removed, and disabled controls carrying a `title`
explaining why. A full audit has not been done; that is a Phase 5 item in
`DEVELOPMENT_PLAN.md`.
