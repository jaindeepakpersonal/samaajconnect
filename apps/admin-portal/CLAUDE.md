# admin-portal

The Angular app Samaaj and platform administrators use. Read the root
`CLAUDE.md` §7 first for the conventions this follows, and
`.claude/skills/wireframe-to-angular/SKILL.md` before translating another
screen. The spec is `docs/product/wireframes/admin-panel-wireframes.html`.

## Screens

| Screen | Route | Wireframe section | Status |
|---|---|---|---|
| Sign in | `/login` | — (the wireframe has none) | built |
| Dashboard | `/dashboard` | `#dashboard` | built, partial — see below |
| Samaaj / Tenants | `/tenants` | `#tenants` | built |
| Create Samaaj | `/tenants/new` | `#createtenant` | built |
| Admin Users & Roles | `/admins` | `#admins` | built |
| Invite Admin | `/admins/invite` | `#inviteadmin` | built |
| Role & Permission Matrix | `/roles` | `#rolematrix` | built, editable per Samaaj |
| Adult Child Conversion Queue | `/conversions` | `#conversionqueue` | built |
| Audit Logs | `/audit` | `#audit` | built |
| Members, Timeline, Groups, Events, Issues, Celebrity, Pathshala, Boli, Notifications, Reports, Settings | — | various | not built — no service exists |

The unbuilt screens appear in the nav, disabled, each saying why. That is
deliberate: the wireframe promised them, and an administrator who cannot see
that Pathshala is coming has been told less than the wireframe did.

## Where things live

```
apps/admin-portal/src/app/
├── app.config.ts            providers, interceptor order
├── app.routes.ts            lazy routes; everything but /login is behind the shell
├── shell/                   the nav, top bar and Samaaj scope selector
├── core/
│   ├── admin.models.ts      wire shapes, mirroring the services' responses
│   ├── admin-api.ts         every call this app makes
│   ├── admin-scope.ts       which Samaaj is being acted on, and its interceptor
│   └── current-user.guard.ts
└── features/{feature}/      one folder per screen group
```

## Running it

**In Docker, with everything else.** The panel is a container now: a static
build served by nginx, which also proxies `/v1` to the gateway so the app is
same-origin with its own API.

```bash
docker compose up -d --build
```

Then open <http://localhost:4300>.

**Its own origin is deliberate.** It is not behind the gateway like the member
portal, because both apps share `libs/shared`'s `TokenStore` and its
sessionStorage keys `samaajconnect.token` / `samaajconnect.refresh`.
sessionStorage is scoped to an origin, so on one origin an admin signing in
would overwrite a member's session in the same tab - and the panel would then
call every endpoint with a member's token and 403 on all of them. Two origins,
two sessions. See `apps/admin-portal/nginx.conf`.

**For frontend work**, the dev server is still the fast path:

```bash
docker compose up -d --build
```

```bash
npm run start:admin
```

The dev server also uses 4300, so it and the container cannot both hold it.
Stop the container first:

```bash
docker compose stop admin-portal
```

The rest of the stack keeps running, and the dev server's `proxy.conf.json`
sends `/v1` to the gateway on 8080 — the same same-origin arrangement nginx
provides in the container.

`npm test` runs all three suites: `test:libs`, `test:app` (member-portal) and
`test:admin`.

## Decisions worth knowing before you change this

**The Samaaj scope is a real privilege, shown at all times.** A Samaaj Admin's
token names one Samaaj and the selector is not offered to them. A Super Admin's
token names none, so choosing one sends `X-Tenant-Override-Id` and every
service behind the gateway scopes to it. The gateway refuses that header from
anyone without the SuperAdmin role and audits every request carrying one — on a
single domain there is no admin hostname to gate it by, so the role is the whole
gate and the audit log is the only record of who acted on whose Samaaj (root
`CLAUDE.md` §6). The banner naming the Samaaj is the least the panel can do
about that.

**Changing the scope reloads the page.** Screens read their data once on init
and the router reuses a component when the URL has not changed, so an in-app
navigation would leave the previous Samaaj's rows on screen under the new
Samaaj's name — the most misleading thing this panel could do. A reload also
guarantees nothing cached from the previous scope survives. The selection is in
`sessionStorage`, so it comes back.

**`currentUserGuard` holds the panel until roles are known.** Roles and
permissions come from `/v1/identity/me`, not from the token, so a screen that
branches on them reads an empty list if it initialises first. That is not
cosmetic: it made a Super Admin's tenant tile blank and made Samaaj-scoped
tiles show a confident `0` for a Samaaj nobody had selected. Both look exactly
like permission bugs.

**The role matrix edits now, and the screen still decides nothing.** The
wireframe always said it should edit; it took per-tenant overrides, an audit
trail and a lock-out floor before that was safe. What the screen renders is
still entirely the backend's answer: the response says whether this caller may
edit, each role says whether it may be edited, and a cell is a checkbox only
when both are true. SuperAdmin comes back not editable.

The one rule duplicated from the backend is that a Samaaj administrator cannot
lose `Roles.Manage`, drawn as a fixed tick rather than a checkbox. That is a
deliberate copy: offering a click that always answers 409 is offering a choice
that was never there.

**A refused change re-reads the matrix.** A checkbox has already flipped itself
in the DOM by the time the request fails, and leaving it showing a change the
server refused is the one thing this screen must never do.

**The one-time activation code is shown once and then removed from the DOM.**
Only its hash is stored, so it cannot be looked up again. "Invite another"
clears it before the next form appears — leaving the previous code on screen
while a second invitation is typed is how one gets handed to the wrong person.

**Wireframe columns with no cheap answer are gone, not faked.** The tenant
list drops **Subdomain** (there are no subdomains any more) and **Members**
(that count lives in member-family-service, and one cross-service call per row
for a column nobody acts on is exactly the synchronous reach across a service
boundary this repo avoids). The conversion queue drops **Family** and **Age**
for the same reason. The dashboard shows a tile only when something can answer
it, and names the rest as still to come.

**Validation rules are duplicated from the services on purpose, and must stay
in step.** The invite form applies identity-tenant-service's identifier rule
character for character. A rule this form is stricter about is a login nobody
can create here; one it is looser about is a wasted round trip.

**Models live in this app, not in `libs/shared`.** Only the admin panel calls
these endpoints. A type moves to the shared library when both apps need it, not
in anticipation.

## Accessibility

Same standard as member-portal (WCAG 2.1 AA). In place: labelled controls
including visually-hidden labels on the per-row checkboxes and note fields,
`aria-invalid` on failed fields, `role="alert"` on errors and `role="status"`
on progress, a restyled rather than removed focus ring, and wide tables
scrolling inside their own container rather than the page.

One trap worth remembering: the disabled nav items originally carried their
reason in a `title` attribute, which becomes the element's accessible name and
*replaces* the label — a screen reader announced "The events service does not
exist yet" with no way to tell which item that was. The reason is now a
visually-hidden span after the label.

**The audit was done on 2026-09-01**, alongside member-portal's — see that app's
`CLAUDE.md` for what was checked and what the palette measured. Two things
changed here.

**The shell now has a skip link**, and it matters more in this app than in the
member portal: the whole left rail is navigation, so a keyboard user without one
tabs through every section link on every page before reaching the content.
`<main>` gained `id="main-content"` and `tabindex="-1"` so the link has
somewhere focusable to land.

**Sign-in has a `<main>` and deliberately no skip link.** It sits outside the
shell, so it has no navigation to skip past, and a skip link that jumps nowhere
is noise in the tab order rather than a service.

Not yet done: a pass with a real screen reader, and a keyboard-only walkthrough
of the role matrix, which is the widest table in either app.
