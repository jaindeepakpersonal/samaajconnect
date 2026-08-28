---
name: wireframe-to-angular
description: Translate one screen from the samaajconnect clickable HTML wireframes (docs/product/wireframes/*.html) into a real Angular standalone component in apps/member-portal or apps/admin-portal, wired to real service endpoints instead of the wireframe's demo onclick/alert handlers. Use whenever building a screen that already has a wireframe equivalent — that markup, copy, and flow are the spec, not just a loose reference to redesign from.
---

# Turn a wireframe screen into an Angular component

Read root `/CLAUDE.md` §7 first for the frontend conventions this
generates against (standalone components, Signals, the tenant
interceptor, feature-folder naming).

## Inputs to collect before generating anything

1. **Source screen** — which wireframe file and which `<section
   id="...">` inside it.
2. **Target app + feature folder** — `member-portal` or
   `admin-portal`, and the feature folder it belongs under. Mirror the
   owning service's name where practical (e.g. the wireframe's
   `family` screen belongs under a `family` feature folder, pairing
   with `member-family-service`).
3. **Real endpoint(s) it calls** — look up method/path/roles in
   `docs/product/API-CONTRACTS.md`. A screen with no matching endpoint
   yet is a signal to build the backend feature first (see
   `add-service-feature`), not to fake the data in the component.

## Steps

### 1. Extract the screen

Pull the exact `<section id="{screen}">...</section>` markup from the
wireframe file. This is the layout, copy, and interaction-flow source
of truth — don't redesign it, translate it.

### 2. Convert markup to a standalone component

- `<div class="card">` and similar wireframe containers become real
  components/templates using the project's actual design system —
  the wireframe's inline CSS classes (`.card`, `.pill`, `.btn`) were
  prototype-only and don't ship.
- Static demo data in the wireframe (member names, counts, sample
  posts) becomes signal-bound properties fed by a real API call —
  never hardcode the wireframe's placeholder values into the shipped
  component.
- `onclick="go('x')"` becomes a real `routerLink` or programmatic
  navigation call. `onclick="alert(...)"` becomes a real command
  dispatch to the matching endpoint from `API-CONTRACTS.md`.

### 3. Wire the real endpoint

Call it through the shared `HttpClient` + tenant interceptor (root
`CLAUDE.md` §7). Map loading, error, and empty states explicitly — the
wireframe's static markup never shows these three, but the shipped
component needs all of them.

### 4. Respect the role/permission the endpoint requires

Cross-check the Roles column in `API-CONTRACTS.md` and gate the screen
(or specific actions on it) with a matching guard/conditional. This is
a UX convenience only — confirm separately that the backend actually
enforces the same check, since that's the real boundary (root
`CLAUDE.md` §6).

### 5. Carry over the states the wireframe already modeled

Where the wireframe shows a specific state — e.g. the Events screen's
"Full — Waitlist" pill, or the Boli detail screen's bid-history table
— implement it as a real conditional branch driven by API data, not a
static label. These states were deliberately included in the
wireframe because the underlying requirement needs the UI to handle
them, not because they looked good as a demo.

### 6. Add what the wireframe didn't cover

Wireframes are intentionally low-fidelity: loading skeletons, toast/
error messaging, and form validation feedback generally aren't shown.
Add these per the project's normal frontend conventions — their
absence in the wireframe is a scope limitation of the prototype, not a
requirement that the shipped screen skip them.

## Checklist before calling a screen "done"

- [ ] Component builds and renders against real (or seeded test) API
      data, not hardcoded wireframe demo values
- [ ] Every interactive element from the wireframe screen has a real
      handler — no leftover `alert()`/`console.log` placeholders
- [ ] Loading, empty, and error states are handled even though the
      wireframe didn't show them
- [ ] Role/permission gating matches `API-CONTRACTS.md`
- [ ] Any state the wireframe explicitly modeled (capacity/waitlist,
      approval-status pills, locked/published results, etc.) is a real
      conditional, not a static label left over from the prototype
