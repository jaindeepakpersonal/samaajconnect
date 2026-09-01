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
| Events | `/events` | `#events` | built |
| Event detail | `/events/:id` | `#eventdetail` | built |
| Volunteer Groups | `/groups` | `#groups` | built |
| Group detail | `/groups/:id` | `#groupdetail` | built, plus the president's review queue the wireframe has no screen for |
| Social Issues | `/issues` | `#issues` | built |
| Issue detail | `/issues/:id` | — | built; no wireframe covers it, and the workflow is unusable without one |
| Members | `/members` | `#members` | built |
| Member detail | `/members/:id` | `#memberdetail` | built |
| My Family | `/family` | `#family` + `#children` | built as one screen |
| Celebrities of Samaaj | `/voting` | `#celebrity` | built |
| Campaign detail | `/voting/:id` | `#celebrity` + `#celebrityresults` | built as one screen |
| Your data and privacy | `/privacy` | — | built; no wireframe covers the DPDP rights, and endpoints reachable only with curl are not rights a member has |
| Jain Pathshala | `/pathshala` | `#pathshala` | built |
| Pathshala enrolment | `/pathshala/:id` | `#myclass` + `#attendance` + `#exams` + `#progress` | built as one screen |
| Auctions / Boli | `/boli` | `#boli` | built |
| Boli detail | `/boli/:id` | `#bolidetail` | built |
| Boli occasion | `/boli/occasions/:id` | — | built; the wireframe's "View Occasion" had no screen behind it |
| Pathshala events | — | `#pathevents` | not built — no endpoint exists |
| Forgot password / OTP | — | `#forgot`, `#otp` | not built — no endpoint exists |
| Profile, Notifications | — | various | not built |

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

**In Docker, with everything else.**

```bash
docker compose up -d --build
```

Open either <http://localhost:4200> - the container - or
<http://localhost:8080> - the gateway, which serves the portal at the root as
the platform's public front door. Both are same-origin with `/v1`, which is
what the production `ApiConfig` (`gatewayUrl: ''`) needs: on 8080 the gateway
serves both, and on 4200 the SSR server proxies `/v1` to the gateway itself
(`GATEWAY_URL` in docker-compose.yml, mounted in `src/server.ts`).

The first version of this published no port at all, on the reasoning that the
gateway was the only front door that mattered. That was wrong: every other
container in the stack publishes one, so a portal showing a bare `4000/tcp`
reads as broken no matter what the documentation says.

**For frontend work**, the dev server is still the fast path - rebuilds are
instant and the container is a full `ng build`:

```bash
docker compose up -d --build
```

```bash
npm start
```

That serves on 4200 and `proxy.conf.json` sends `/v1` to the gateway on 8080,
which is the same same-origin arrangement by a different route.

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

**A wireframe promise the platform cannot keep is dropped, not printed.** The
event detail wireframe says "You'll receive a notification reminder 24 hours
before the event". There is no notification channel yet, so the shipped screen
does not say it - a disabled control can explain itself, but a sentence of prose
cannot, and printing it would simply be a lie. There is a test asserting the
words are absent.

**A null field means "not shared", never "not set", and the screen must not
claim otherwise.** member-family-service returns null for a field the viewer
may not see rather than masking it, because a mask like "+91 98xxxxxx10" still
leaks length and shape. From the client the two cases are indistinguishable, so
the only honest label is "Not shared" - never "None" and never a bare dash.

**The directory has no profession filter, and that is a privacy decision.**
Profession carries a per-field privacy level. A server-side filter on it would
let anybody confirm a private value one query at a time - ask for "CA", see who
comes back - which is the same reasoning that already stops the service matching
a search term against a private mobile number. The column stays, because a
member who shared it should be findable by eye.

**Adding a child fetches the DPDP notice before it offers the form.** Section 9
makes parental consent the basis for holding a child's data and section 6(7)
means a consent that cannot say what was shown is worth little - so the notice
arrives first, its version travels back with the consent, and the tick is never
pre-filled. A tick beside a notice that has not loaded is a tick against
nothing.

**A workflow screen renders its buttons from the server, never from the
status.** Social issues have eight states and a transition table the aggregate
enforces; the service returns `availableTransitions` computed from that same
table, so a button that appears is a move the server will accept. Deriving the
buttons here would put a second copy of the table in the portal, and the first
time somebody added a state the screen would be confidently wrong. It also
means one screen serves a member and a reviewer without knowing which it is
talking to.

**A screen never asks for something it has been told it may not have.** The
group detail screen fetches the president's review queue only when the group
says `iAmThePresident`. That endpoint answers 404 to anyone else - deliberately,
so a 403 cannot confirm a group has applications pending - so asking
speculatively would mean a 404 on every ordinary member's visit, and a console
full of them is how a real 404 stops being noticed.

**Names the portal cannot resolve are not invented.** Timeline, Events and
Groups all carry member ids and no names: names live in member-family-service
and resolving them would be a call per row. Each screen says the thing it *can*
know and that actually matters to the reader - "Your post", "You", "The
president", "A member", "A volunteer group" - rather than printing an id or a
placeholder. The wireframes' "President: Rajesh Jain" is prototype data, not a
field the API has.

**Money is integer paise on the wire and converted in exactly one place.**
boli-service holds every amount as a `long` because a Boli is money the Samaaj
announces and collects against. `boli.format.ts` is the only file that divides
or multiplies by 100, and it rounds rather than truncating: `15600.07` parsed as
a float and multiplied by 100 is `1560006.9999999998`, which truncates to a
paisa less than the member typed. It also refuses input `parseFloat` would
accept — `parseFloat('12abc')` is `12`, and bidding a number nobody typed is the
worst possible way to be lenient. Amounts are grouped `en-IN` explicitly, so a
member on a US-locale phone still sees ₹1,50,000 rather than ₹150,000.

**A bid the server refuses as too low is a notice, not an error.** The service
answers 200 with `accepted: false` and the amount now needed, because somebody
outbid while their form was open has done nothing wrong. The screen puts that in
an info notice, refills the field with the new minimum, and leaves the red
`role="alert"` for things the member can actually fix. The minimum itself always
comes from the server — the increment rule belongs to the Boli, and a second
copy here would be the one that drifts.

**"You are leading" is only ever said while bidding is open.** It shipped
appearing beside "Result announced", which is the wrong tense on a race that
finished hours ago — and on a Boli closed *without* a published result it would
have been worse than wrong, telling the reader they had won before the Samaaj
announced it. That is precisely what the service's record-then-publish split
exists to prevent, and a screen is as able to break it as a handler. Once a
result is published the list says "Won by you", from the result rather than from
the leading bid.

**Withdrawing a consent has no confirmation step, and erasing has two.** DPDP
section 6(4) requires withdrawing to be as easy as giving, and giving was a tick
during registration — so an "are you sure?" in front of it would be the app
making a right harder to exercise than the Act allows. Erasure is the opposite:
it cannot be undone, so it asks for the password, which is also the identity
check a Fiduciary needs before acting irreversibly. The required consent is not
a disabled button but a sentence: withdrawing membership is not temporarily
unavailable, it *means* erasing the account, and saying so is more use than a
control that answers 409.

**What erasure keeps is shown beside what it erased.** A member told only "done"
has no way to know a de-identified audit row survives. Section 8(7) permits
retention required by other law, so printing both lists is part of honouring the
right rather than a caveat on it.

**The data export is assembled in the browser, from three services.** The
platform has no single export endpoint on purpose: a member's data sits in
identity, member-family and audit, and having one service reach synchronously
into the others would undo the service boundaries for something used a handful
of times a year. The client is the only party already authenticated to all
three. A service with nothing for that member contributes a note rather than
failing the export — a partial copy delivered beats a complete one refused —
and gathering the data is reported separately from the browser refusing to save
it, because those are different problems and only one of them is the member's to
fix.

**A parent's own children are the one place this app does resolve names.** The
rule below — that names the portal cannot resolve are not invented — is about
ids belonging to people the reader has no particular claim on, where resolving
them would be a call per row. A parent's children are one call to
`/v1/children`, a list they already hold, and "Waiting for a place" against an
opaque id would be useless to the only person the Pathshala screens are for. The
teachers on the same screen stay a count, because those are exactly the ids the
rule is about.

**Waiting for a place is a state, not a failure.** An unplaced enrolment has no
class, so `my-class` answers 409 by design and the exam list comes back empty.
The enrolment screen reads `classId` off the enrolment it already has and asks
for neither until there is one — the same discipline as the group president's
review queue. A parent needs "the Pathshala has not placed them yet" told
plainly; it is the state they will sit in longest, and it is not "declined".

**Two numbers on one screen must be able to be reconciled.** An excused absence
is marked but deliberately not counted against the attendance percentage. The
progress tile first printed "2 of 4 marked" beside a 67% that is 2 of 3, which
reads as an arithmetic error rather than as the policy it is. It now lists the
three counts instead. The same care applies to the exam column: a mark is shown
out of the exam's own maximum, never rebased to 100 without saying so.

**A `<select>` bound to an uninitialised model shows nothing at all.** An
`undefined` matches no option — not even the one whose `value` is the empty
string — so the control renders with `selectedIndex: -1` and reads as an empty
dropdown rather than as its placeholder. The Pathshala child picker seeds each
card's model to `''` when the list loads. Tests that set the model directly
never see this; only opening the page does.

**A window that is open is a fact about the clock, not about the status.** The
voting screens read `acceptsNominations` and `acceptsVotes` for every button,
every sentence and the state pill, and the status only as a fallback for naming
a state that has no window. A campaign sitting at `NominationsOpen` past its
closing date is not taking nominations, and only the server knows the time it is
deciding against. Labelling the pill from the status while everything else read
the clock shipped a card whose pill said "Nominations open" directly above a
line saying "Nominations have closed" — worse than either sentence alone,
because a reader has to work out which half to believe. `stageLabel()` in
`voting.models.ts` is shared by both screens so they cannot drift apart again.

**A null count is not zero, and the flag that says which is not the null.**
A campaign set to `HiddenUntilClose` returns candidates with `votes: null`, and
`tallyVisible: false` beside them. Printing 0 would tell a member nobody had
voted for someone, which is a claim, and the wrong one. The screen needs the
flag rather than inferring from the null because an empty campaign with the
tally visible has genuine zeroes — the two look identical from the null alone.

**Doing something twice is not an error, and the screen should not dress it up
as one.** Nominating an already-nominated member and voting a second time both
come back 200, with `nominated: false` / `accepted: false` and the candidacy or
vote actually held. The unique indexes are what enforce one each; the response
just reports which happened. Both screens say so plainly — "They had already
been put forward" — rather than showing an error for a button someone pressed
twice.

**Shared page primitives live in `src/styles.css`, not in a feature
stylesheet.** The pill colours (`.ok`, `.warn`, `.danger`), the two-column
`.grid2`, the `.progress` bar, `.back` and the table rules are all used by more
than one screen. Timeline shipped marking a pill `.warn` with nothing defining
it: the pill rendered in the wrong colour, which is exactly the kind of thing
nobody notices. Every one of those pill variants sets a text colour as well as a
background, so a state is never carried by hue alone.

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

The requirements call for WCAG 2.1 AA. **The audit was done on 2026-09-01**;
what it checked and what it found are below, so the next pass knows what has
already been looked at rather than starting over.

**Already in place, and confirmed:** labelled form controls — every one of the
57 across both apps, including the checkboxes that are wrapped in a `<label>`
rather than pointing at one by id; `aria-invalid` on failed fields;
`role="alert"` on errors and `role="status"` on progress and confirmations; a
focus ring restyled rather than removed; exactly one `<h1>` on every screen;
wide tables scrolling inside their own container; `lang="en"` on the document.

**The palette passes AA everywhere**, checked rather than assumed — the tightest
pair is muted text on the page background at 4.56:1, and every other
combination the apps use is above 4.9:1. Nothing needed changing, which is
worth writing down: the next person to add a colour has a floor to clear.

Three things it found.

**The app had no `<main>` and no skip link.** `App` was a bare
`<router-outlet />`, so nothing on any screen was inside a landmark and there
was no way to bypass repeated blocks (WCAG 2.4.1, level A). Both apps now have
a visible-on-focus skip link as the first thing in the tab order, and a
`<main id="main-content" tabindex="-1">`. The `tabindex` is what makes the link
work at all: without it the target is not focusable, the browser scrolls but
leaves focus on the link, and the next Tab goes straight back into what the
member was skipping.

**Home's module tiles were buttons that navigated.** They called
`router.navigateByUrl` from a `<button>`, which looks identical on screen and
is wrong in every other way — announced as "button" rather than "link", absent
from the list of links on the page, and impossible to middle-click or
long-press into a second tab. They are anchors now. Each also carries its tile
title in a visually-hidden span, because every visible label is "Open" and a
screen reader listing the page's links otherwise reads "Open, Open, Open".

**Nothing moved focus on navigation.** A full page load resets focus and a
screen reader starts reading; a router navigation does neither, so focus stayed
on a tile that no longer existed and the member was told the page had changed
by sighted layout alone. `App` now focuses the `<main>` after every navigation
except the first, which the browser has already handled.

Also added: a `prefers-reduced-motion` block in both apps. The only motion
today is the skip link sliding in, which is small — but the preference is a
preference rather than a threshold, and the rule belongs there before something
larger is added and nobody remembers to ask.

Not yet done: a pass with a real screen reader, and keyboard-only walkthroughs
of the longer workflows (the Boli bid form, the issue transitions). Those need a
person, not a script.
