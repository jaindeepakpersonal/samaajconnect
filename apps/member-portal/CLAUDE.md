# member-portal

The Angular app members use. Read the root `CLAUDE.md` §7 first for the
conventions this follows, and `.claude/skills/wireframe-to-angular/SKILL.md`
before translating another screen.

## Screens

| Screen | Route | Wireframe section | Status |
|---|---|---|---|
| Login | `/login` | `#login` | built. A CAPTCHA and the password, then the 6-digit code on a browser the account does not trust (trust it for 30 days) - see below |
| Register | `/register` | `#register` | built |
| Home (Timeline) | `/timeline` | `#timeline` | built - the landing page, labelled "Home" in the sidebar; `/home` redirects here (see below) |
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
| Your data and privacy | `/privacy` | `#profile`'s privacy card, plus none for the DPDP rights | built - carries `#profile`'s per-field directory privacy card, moved here 2026-09-11, ahead of the three DPDP rights (no wireframe covers those, and endpoints reachable only with curl are not rights a member has). Reachable from the top bar's user menu, not the sidebar |
| Jain Pathshala | `/pathshala` | `#pathshala` | built |
| Pathshala enrolment | `/pathshala/:id` | `#myclass` + `#attendance` + `#exams` + `#progress` | built as one screen |
| Auctions / Boli | `/boli` | `#boli` | built |
| Boli detail | `/boli/:id` | `#bolidetail` | built |
| Boli occasion | `/boli/occasions/:id` | — | built; the wireframe's "View Occasion" had no screen behind it |
| Notifications | `/notifications` | `#notifications` | built |
| Businesses directory | `/businesses` | — | built; no wireframe covers a member marketplace |
| Business detail | `/businesses/:id` | — | built. Products, contact info, and "Place an order" - hidden for the business's own owner |
| My businesses | `/businesses/mine` | — | built. The caller's own businesses, plus a create form |
| Manage business | `/businesses/mine/:id` | — | built. Edit fields, activate/deactivate, and product management |
| Orders | `/businesses/orders` | — | built. Placed/Received tabs |
| Competitions | `/competitions` | — | built; no wireframe covers this new module. Browse, register, withdraw |
| Competition detail | `/competitions/:id` | — | built. Rounds and entries, a judging section visible only to an assigned judge, and the published result |
| Samaaj Panch | `/panch` | — | built; no wireframe covers it. Who sits on the Panch, and the elections |
| Panch election | `/panch/:id` | — | built. Nominate somebody by name, cast one final ballot of up to ten, read the result |
| Trusts | `/trust` | — | built; no wireframe covers this new module. Our Samaaj's locality Trusts, read-only, with a "What you owe" card above them when a Boli the member won has fallen due to a Trust |
| Trust detail | `/trust/:id` | — | built. Roster with each trustee's role, and its elections - nominate a fellow trustee, cast a ballot of up to the role's seats, see every winner |
| Pathshala events | — | `#pathevents` | not built — no endpoint exists |
| Set your password | `/activate` | — | built; no wireframe covers redeeming an activation code, and three admin screens told people to do it here |
| Forgot password | `/forgot`, `/reset` | `#forgot`, `#otp` | built — redeems into a new password, not a session |
| My Profile | `/profile` | `#profile` | built — five tabs (Basic, Professional, Educational, Address, Photo) since 2026-09-12, see below |
| Change your password | `/change-password` | — | built; no wireframe covers it. Its own screen now, reachable from the top bar's user menu, not a card on My Profile |

## Where things live

```
apps/member-portal/src/app/
├── app.config.ts          providers, interceptor order
├── app.routes.ts          lazy routes; every signed-in screen is a child of
│                          the shell route, which carries authGuard once
├── shell/                 the sidebar, top bar and breadcrumb every signed-in
│                          screen sits inside
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

**Every signed-in screen sits inside a persistent shell now, and for a long
time none did.** The wireframe's defining piece of chrome is a fixed dark
sidebar, grouped Core/Community/Jain Pathshala/Boli/Account, plus a top bar
naming the current section and carrying the notification bell and the
member's own chip - present on every one of its screens. Nothing here built
it: `App` was a bare `<router-outlet />` wrapping whichever feature loaded,
so the only way from Members to Family was back to Home first, on every
single visit. `shell/shell.component.ts` is that chrome, added as the
parent of every signed-in route; `authGuard` moved to that one parent route
rather than staying on each child, because a route reused across
navigations does not re-run its own guard and does not need to - a session
that actually ends is caught by the interceptor's own redirect the next time
any screen makes a request, which every one of them does on load.

The wireframe's own placement of "Logout / Login" as a sidebar row moved
into the top bar's user chip instead, matching admin-portal's shell rather
than inventing a second placement for the same control this platform already
has one of. And the "Tenant: mahavir-samaj / mahavir-samaj.samaajconnect.com"
block under the brand is gone rather than translated, for the reason
`CLAUDE.md` section 6 already gives everywhere else in this app: there is no
Samaaj subdomain, and the breadcrumb already names the Samaaj on every
screen without it.

Home's own header carried the Samaaj pill, the member's name and Sign out
before the shell existed, because it was the only screen that could. It kept
them afterwards for one cycle too many, which meant Home showed the same
three things twice - once in its own header, once in the shell wrapping it.
They are gone from Home now; the shell is where every screen shows them,
Home included. The twelve list screens' own "Back to home" buttons are gone
for the same reason: reachable directly from the sidebar now, a button
whose only job was returning to the one screen with a way out is not
saving a click, it is clutter the wireframe's own list screens never drew
either.

**The wireframe's numbers are not in the shipped Home.** It showed 1,248
members, 4 family, 6 events. Those services do not exist, and the skill is
explicit that prototype values must not be hardcoded, so a tile carries a count
only once something can supply one.

**Below 700px the sidebar is an off-canvas drawer, not a stack.** The shell's
first mobile rule simply let `.side` fall out of the flex row and sit above
`.content`, so a member on a phone scrolled past every nav group before
reaching a single word of the screen they opened the app for. `navOpen` (a
signal on `ShellComponent`) now gates a fixed-position drawer that transforms
in from the left over a backdrop, triggered by a floating hamburger button
that stays in the DOM at every width rather than being swapped for a close
button - the same "trigger never leaves" rule this app already applies to
confirmation panels. The drawer closes on a backdrop click and on any nav
link click, so choosing a destination doesn't leave the menu covering it.
admin-portal's shell had the identical defect and got the identical fix,
since both shells share this one bug's shape: a sidebar built for desktop
with only a stacking rule for narrow viewports, never a collapsing one.

**The top bar's user chip opens a menu now, as of 2026-09-11 - it used to
navigate straight to `/profile`.** Change password (a card on My Profile
until the same change) and Your data and privacy (its own sidebar row until
then) both moved into it, alongside Sign out, which moved out of its own
standalone button beside the chip into the same menu. All three are account-
level actions a member reaches rarely and that are not tied to whichever
screen they happen to be on - grouping them under the member's own name reads
better than three destinations competing with the feature nav for space, and
matches the "avatar menu" pattern most sites already put exactly these three
kinds of things behind. My Profile itself stayed a sidebar row: editing
profile fields is not an occasional account action, it is what that whole
screen is for, so it did not belong in the same menu as the other three.

The trigger is a `<button>` with `aria-haspopup`, `aria-controls` and
`[attr.aria-expanded]`, the same accessible shape this app's confirmation
panels already use - `toggleUserMenu()` flips a signal rather than only
opening one, so pressing the chip again closes what is already open. Unlike
those panels the menu also gets a backdrop, the mobile nav drawer's own
pattern: a menu that floats over the page rather than expanding inline reads
as still-open until something closes it, and a member clicking anywhere else
on the screen expects that click to do that.

**Home hid modules the Samaaj had switched off.** The gateway already answers
404 for those routes, so offering the tile would have been offering a door to
a 404. The "no modules enabled" notice was keyed off whether any *optional*
module was on, not off an empty tile list, because Members and Family were not
module-gated and were always present.

**Home is gone as of 2026-09-11, and Timeline is the landing page now.** Every
tile Home ever offered was a route already on the sidebar - the one thing on
the screen a member saw most often was a second copy of the nav, which is what
prompted this. Its three notices moved rather than disappeared: the contact-
verification banner is now Timeline's own (`isContactVerified`, defaulted true
until `/me` answers, the same reasoning `canModerate` already followed); the
unread-notifications banner did not move, because the shell's bell badge
(`shell.component.ts`, present on every screen since the shell was built) had
made it redundant the moment the shell shipped, not just now; and the
"no modules enabled" notice became something more specific and more useful.

Timeline is module-gated on `community`, same as Home's own tile for it, but a
member can now land here even when that module is off - `/`, `/home`, and
"after signing in" all point here unconditionally, there being no meaningful
narrower fallback once Home's tiles were all sidebar duplicates regardless of
which modules were on. `TimelineComponent.load()` tells a 404 on the feed apart
from every other failure and shows "Timeline is not switched on for your
Samaaj yet" instead of the generic error box - a distinction that is not a
guess: a feed is a list, so nothing about `GET /v1/timeline/posts` can 404 for
a per-item reason, which means a 404 there can only mean the module gate. The
compose form and feed are hidden in that state rather than shown broken.

`/home` redirects to `/timeline` rather than 404ing, and the sidebar's brand
wordmark is a link to `/timeline` too - the "click the logo" pattern this app
had never needed while Home held that job.

**A "Home" sidebar row came back four days later, relabelled rather than
rebuilt.** The paragraph above said Timeline's own row was the only way there
from the nav, on the reasoning that a second row pointing at the same
destination would be the exact redundancy retiring Home had just removed. That
reasoning was about *two rows to one destination*, and stands; what changed is
that the row is now named "Home" - what `/timeline` *is* to a member signing in
is the landing page, and the label says that plainly rather than naming the
screen's content. It also moved first in the Core group, ahead of My Profile,
matching where a "way back to the start" row reads as belonging. There is
still exactly one row for it, so the redundancy this whole change exists to
avoid has not returned - only its name and position did.

**Timeline gained a fourth reaction, `Dislike`, on 2026-09-12 - added to
`Reactions` in `timeline.models.ts` alongside `Appreciate`/`Support`/
`Celebrate` rather than in place of any of them.** The button needed no
template change: the reaction row already renders `Reactions` generically,
so the fourth entry in that list is the whole change on this side. The
service's own `ReactionType` enum and its validator's error message moved
with it - see timeline-service's own `CLAUDE.md`.

**Timeline gained author names and reaction icons on 2026-09-26, both
additions rather than redesigns.** `icon` sits beside `label` on each entry
in `Reactions` - decorative only, never sent; `type` is still the only part
of a reaction on the wire. Author names use the same batched, memoized
`resolveNames()` this app already established for a group's roster (see
"Names the portal cannot resolve are not invented" above) rather than a new
pattern: one `MembersApi.get()` per distinct `authorMemberId` on screen,
called from `load()` for the feed and from `toggleComments()` for whichever
thread is open, never for the whole Samaaj.

**Home opens on the reader's own face, and every post shows its author's
(2026-09-27).** A card at the top of Timeline carries the member's photo, name
and Gautra - `/v1/members/me`, plus the Gautra list only when they have picked
one - with a link to My Profile that says "Add a photo" when there is none.
Each post and comment has its author's photo beside their name; the path comes
from the same one `MembersApi.get()` per author that already resolves the
name, so faces cost no lookup names did not, and `scAuthedSrc` fetches them
with the token (the service's ETag makes a repeated face a 304). A member with
no photo, or one this reader may not see, is their initials. The profile card
failing to load leaves the name from `/me` and initials; the feed never waits
for it.

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

**Signing in is two steps since 2026-09-27, and the code-only tab is gone.**
Two factors are required of everybody (the user's decision): the password with
a CAPTCHA drawn by identity-tenant-service, then - on a browser the account
does not trust - the 6-digit code sent to the member's own mobile or email,
with a box to trust this browser for 30 days. The OTP tab below signed in with
the code alone, which with two factors required was the one way in that
skipped the password, so it was removed (confirmed with the user); the code
field the wireframe drew is the second step now. `SignInFlow` in libs/shared
holds the steps for both portals and this screen draws them. The trusted-device
token lives in `localStorage`, unlike the session: it excuses the code and
nothing else. The history of the OTP tab is kept below as it was.

**Screens with no endpoint are disabled and say why — until the endpoint
lands, and then they are wired, not faked a moment sooner.** The OTP tab was
the first of the login screen's two stubs to close: the passwordless code
commands landed, and a member picks a Samaaj-wide login
identifier, gets a real 6-digit code delivered through the same notification
pipeline the welcome message already uses, and the code signs them in
exactly like a password would - same `LoginResponse`, same lockout, same one
indistinguishable `Auth.InvalidCredentials` for a wrong code or an unknown
account. Successfully using a code also verifies the contact address it was
sent to, the same assurance redeeming an activation code already gives.

**Forgot password is the second, and redeems into a new password rather than
a session.** `/forgot` (the wireframe's `#forgot`) asks for an identifier and
answers identically whether or not it belongs to a real account, the same
anti-enumeration rule the OTP-request endpoint follows; `/reset` (`#otp`,
reused for a different purpose than the login screen's OTP tab - this one
sets a password, that one signs in) takes the code and the new password. No
token comes back from redeeming - proving you hold a contact address a code
was sent to is weaker than a real password, the same reasoning `/activate`
already follows for a converted child's first sign-in - so both hand back to
`/login` with a notice rather than skipping it.

**The wireframe's OTP field has no explicit "Send" control, only "Resend
OTP" - which reads as the code going out the moment the tab opens.** A real
account does not get a free credential mailing on a tab click, so this screen
adds the one control the wireframe left implicit: a "Send code" button, shown
until a code has actually been requested, after which the field and "Resend
OTP" appear exactly as drawn.

**A wireframe promise the platform cannot keep is dropped, not printed.** The
event detail wireframe says "You'll receive a notification reminder 24 hours
before the event". A notification channel exists now, but nothing in
events-service raises a reminder, so no reminder is sent and the shipped screen
still does not say it - a disabled control can explain itself, but a sentence of prose
cannot, and printing it would simply be a lie. There is a test asserting the
words are absent.

**A null field means "not shared", never "not set", and the screen must not
claim otherwise.** member-family-service returns null for a field the viewer
may not see rather than masking it, because a mask like "+91 98xxxxxx10" still
leaks length and shape. From the client the two cases are indistinguishable, so
the only honest label is "Not shared" - never "None" and never a bare dash.

**Three cycles running, the gap worth filling was an endpoint with no caller.**
The profile screen the welcome notification points at; timeline moderation,
without which no member post could ever be approved; and redeeming an
activation code, which the admin panel told people to do in this app while this
app had nowhere to do it - so no invited administrator could sign in and no
converted adult child could get an account. All three were complete, tested and
reachable only by curl, and nothing failed to say so.
`scripts/unreachable-endpoints.sh` is that check made repeatable; run it before
guessing at what to build.

**A member could be told to complete their profile and had nowhere to do it.**
The welcome notification every registration raises says "Complete your profile
to appear in the member directory"; `PATCH /v1/members/{id}` existed, and so did
`MembersApi.updateMe`, and no screen called either. That is the shape of gap
this app should look for: a client method with no caller.

**Unticking "listed in the member directory" is explained, not just offered.**
It takes a member out of the directory search and out of nothing else - their
name still appears on a post they wrote, in their family, and to the president
of a group they apply to. A checkbox that read as "hide me from the platform"
would be promising something the platform does not do, so the sentence under it
says what it does.

**"Upload Photo" is a real upload now, and it saves on its own.** It was a link
field for as long as the platform hosted no images, with a note saying what a
link cost: every member who opened the directory fetched it from whatever host it
named. The platform stores the bytes now, so the control is a file input and the
note says what that buys instead.

Uploading is its own request rather than part of Save, because a file and a form
field are different things and they were only ever one control while the photo
was text somebody typed. Choosing a picture takes effect immediately, which is
what people expect of a profile photo, and a failed upload cannot lose an address
somebody was halfway through editing.

**Change your password was a third, separate form on this screen, not a third
field on Save - and moved to its own screen entirely on 2026-09-11.** Nothing
on the platform could ever replace a working password before this existed -
not a member's, not a Samaaj Admin's, not the bootstrap Super Admin's - and it
is not directory data, so folding it into the same submission as a locality
edit would have made the two the same request for no reason; that reasoning
did not change when the form did. What moved is where it lives:
`change-password.component.ts` now, reachable from the top bar's user menu
alongside Privacy settings and Sign out rather than inline under Save on My
Profile - see the shell's own note on the user menu, above. A wrong current password still answers
the same 403 `Auth.StepUpFailed` the privacy screen's erasure panel handles,
shown inline rather than signing the member out, since they are still who
they say they are - they simply mistyped. Every other signed-in device is
still signed out the moment it succeeds, which the screen still says plainly
rather than leaving a member to wonder why a different tab suddenly asks them
to sign in again.

**My Profile split into five cards on 2026-09-11, then into five tabs on
2026-09-12** — Basic, Professional, Educational, Address, Photo — and fields
moved off free text onto master-data-service, which this app had never called
before. `master-data.api.ts` (new, local to this feature) fetches Gautra,
Profession and Qualification lists once on init and City/Locality lists per
address's chosen City. Basic gained a Gautra `<select>`; Educational is one
Qualification `<select>`, sourced the same way Gautra is. **Professional was
relocated verbatim on 2026-09-11 and became a `<select>` of its own on
2026-09-12** — the field split deliberately left it as free text at the time,
and it moved onto master-data-service's `ProfessionType` category (one of the
platform's original four flat lookup lists, present since before Qualification
was ever added) once a dropdown was asked for; `form.profession: string`
became `form.professionId: string`, matching Qualification's shape exactly.

**One tab renders at a time, all five behind one `<form>` and one Save
button.** The cards used to be five stacked sections a member scrolled past
to reach the one they wanted; a horizontal tab bar (`role="tablist"`, the
same pattern admin-portal's Master Data screen adopted the same day) now
shows one at a time, switched by `activeTab`. Switching tabs never touches
`form`, so a half-edited Address survives a detour to Educational and back -
the fields are simply not rendered while their tab is inactive, and Angular's
two-way `ngModel` binding does not need them mounted to hold their values.
The one client-side validation (a required full name, on Basic) jumps the
screen back to Basic on a failed save, because an error on a tab nobody is
looking at is not a validation message.

**Address is Permanent and Temporary, each a line plus a City→Locality
cascade.** Choosing a City clears that address's own Locality field and
fetches that City's own Locality list — the same "an uninitialised `<select>`
renders nothing" trap this file already names for the Pathshala child picker,
avoided the same way, seeded to `''`. Permanent and Temporary track separate
City/Locality state because they can legitimately name different cities.

**"Same as permanent" is a one-time copy, not a live link.** Checking it
copies the current Permanent line/city/locality into Temporary and disables
those inputs; unchecking hands them back for independent editing from
wherever the copy left off. The server always stores two independent
structs — this was the user's own choice, a "copy shortcut" over "kept in
sync," so nothing on the wire or in the domain treats Temporary as derived
from Permanent.

**A photo is fetched, never linked.** `photoUrl` is a path on this platform and
authorized per request, so an `<img src>` would fail — the browser sends no
`Authorization` header for a tag it fetches itself. `libs/shared`'s
`[scAuthedSrc]` fetches through `HttpClient` and hands the element an object URL.
Any screen showing a photo uses it.

**Every spec that renders a profile or a child now has a second request to
settle.** The photo fetch is real HTTP, so a test that leaves it open fails
`http.verify()` in `afterEach` and leaves the TestBed dirty for everything after
it — which showed up as six unrelated tests failing with "Cannot configure the
test module". Both member specs have a `settlePhotos()` helper for it, and both
stub `URL.createObjectURL`, which jsdom does not implement.

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

**And the tick can now be untied, which for three months it could not.** The
consent is what makes the record lawful; withdrawing it had no endpoint, so the
only way was `/privacy` → erase your entire account. Section 6(4) asks for ease
comparable to giving, and giving was that one tick on this screen. The control
sits under the consent line it undoes, and appears **only for the member whose
consent it is** — a head who inherited the household did not give it.

**It asks first, and that is not this app contradicting itself.** The rule two
paragraphs down is that withdrawing a consent gets no confirmation, because an
"are you sure?" in front of a right makes it harder to exercise than the Act
allows. That rule is about a *reversible* withdrawal: a member who unticks a
consent can tick it again. This one destroys a record nothing on the platform
can restore, and the parent would have to enter the child again from scratch.
A single unguarded click on that is not ease, it is a trap. So the panel says
what is about to happen rather than asking whether they are sure, and the
trigger stays in the DOM with `aria-expanded` — the focus rule three admin
screens taught this repo.

**The two buttons were nearly identical and that was a real defect, not a test
problem.** The trigger and the confirmation both began "Withdraw consent", so a
screen reader listing the page's buttons read the same phrase twice with no way
to tell which was the one that acted. They are "Withdraw parental consent" and
"Remove this record permanently" now. A spec caught it by matching the wrong
one.

**A workflow screen renders its buttons from the server, never from the
status.** Social issues have eight states and a transition table the aggregate
enforces; the service returns `availableTransitions` computed from that same
table, so a button that appears is a move the server will accept. Deriving the
buttons here would put a second copy of the table in the portal, and the first
time somebody added a state the screen would be confidently wrong. It also
means one screen serves a member and a reviewer without knowing which it is
talking to.

**An issue's locality is picked, not typed, as of 2026-09-26.** A typed
"Hiran Magri" matched nobody's token, so no Area Admin could ever be the one
to review it. The raise and revise forms offer the master-data Localities
("Name, City", loaded once) and send the id with its label as the display
text, so every screen still reads the one `locality` field. An issue raised
before this carries text only; the revise form offers that text back as its
own "(as typed)" option, chosen to start with, because the service replaces
both fields on a revision and correcting a title must not quietly drop the
place an issue was about.

**A screen never asks for something it has been told it may not have.** The
group detail screen fetches the president's review queue only when the group
says `iAmThePresident`. That endpoint answers 404 to anyone else - deliberately,
so a 403 cannot confirm a group has applications pending - so asking
speculatively would mean a 404 on every ordinary member's visit, and a console
full of them is how a real 404 stops being noticed.

**Every group member holds one of the group's volunteer roles, as of
2026-09-26 - positions stopped being free text.** Volunteer (everybody's until
elected to another, no seat limit) and President (one seat) are built in; the
group defines the rest in the "Volunteer roles" section, each with the seats an
election for it fills. `group.iCanRunElections` - the president, or an
administrator of the group - decides who sees the role controls, the role picker
on each Members row, and the election controls; `iCanLead` still decides only
the join-request queue. The picker never offers President, and the president's
own row has no picker: that role moves only with the presidency.

**A multi-seat ballot is ticked, then cast once.** Single-seat elections keep
the one-click Vote button. For a role with more seats each candidate gets a
checkbox, capped at the seats by disabling the rest rather than refusing on
submit, and one "Cast my ballot" button sends them together - the service keeps
a ballot final, so the screen says so above the list instead of letting a
member cast two picks and discover they cannot add a third. `myVoteCandidateIds`
marks every pick as "Your vote"; a result names every winner.

**Positions came from elections since 2026-09-12.** Accepting a join request
carries no role; an election - or the manual picker - is where one comes from.

**Nominations that closed too early are reopened by editing, not a separate
"reopen" button.** An election left `NominationsOpen` past its own end date
never advances `Status` - `acceptsNominations` is already false with nothing
to walk back - so the president's `Edit` control (Draft/NominationsOpen only,
disappearing once voting has opened) corrects the position title and all four
windows, and pushing the nomination close date out again is the whole fix.

**The nominee picker is a `<select>` built from the group's own already-
loaded roster, not a name search.** Celebrities of Samaaj needed
`MembersApi.search()` and an autosuggest because its ballot is Samaaj-wide;
a volunteer group's roster is already on screen and typically small, so
there is nothing here for that fix to solve.

**Names the portal cannot resolve are not invented.** Names live in
member-family-service, off the wire everywhere else, so a screen either
resolves a bounded, on-screen set of ids through it, or says the thing it *can*
know instead of printing an id or a placeholder. Groups (a group's own roster
and applications) and Timeline (the loaded page of the feed, and whichever
comment thread is open) both resolve this way - one `MembersApi.get()` per
distinct id actually on screen, memoized so it is never repeated, falling back
to a generic label ("A member") only if that one lookup fails. What no screen
does is resolve unboundedly - a call per row across a list with no fixed size
- which is why Timeline still reads as "Your post"/"You" for the reader's own
posts rather than a second lookup, and why Events' organizer is "A volunteer
group"/"The Samaaj" rather than a member id at all. The wireframes' "President:
Rajesh Jain" is prototype data, not a field the API has.

**Money is integer paise on the wire and converted in exactly one place.**
boli-service holds every amount as a `long` because a Boli is money the Samaaj
announces and collects against. `libs/shared`'s `money.ts` — which lived here as
`features/boli/boli.format.ts` until the admin panel grew a screen that opens a
Boli, and moved when a second app needed it rather than in anticipation of one
(root `CLAUDE.md` §7) — is the only file that divides
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

**A closing time the server can move has to say so.** A Boli with
`autoExtendSeconds` set extends when somebody bids inside that window, so
printing `endAt` on its own would be the portal stating a time that stops being
true. The line under the countdown says what the window is and that a late bid
moves the close — which is also most of what makes the rule work: sniping is
only pointless once bidders know a late bid buys everybody else another window.
The window is quoted in minutes only when it is a whole number of them, because
rounding 90 seconds up to two minutes prints a longer window than the server is
actually keeping, on the one line a bidder is being asked to rely on.

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

**"Waiting to join a household" is a state this screen did not have.**
`/families/mine` answers with the household for somebody whose request is only
pending — deliberately, since a pending request counts as belonging to one — so
the page drew it as theirs. Reading the viewer's own row is the only way to tell
the two apart, and now it does: a member who has asked and not been answered gets
a card saying so, naming who they asked, and offering to take the request back.

That button had nothing behind it until this cycle. A pending request blocks
joining anywhere else *and* creating your own household, and nothing could cancel
one, so a head who never answered left that member stuck indefinitely with no way
out that did not run through somebody else.

**The refusal is shown at page level, not inside the card.** Withdrawing re-reads
so the screen stops claiming to be waiting — and the one refusal worth showing is
"the head accepted while you were deciding", which is exactly the case where the
re-read takes the waiting card away. A message inside it would be removed by the
same reload that made it true. A test caught that; the first version put the
message where nobody would ever see it.

**Leaving a household is a separate control from withdrawing a request, and it
asks first.** Taking back a request nobody has answered affects nobody, so it is
one press. Leaving affects everyone still in the household — the family code
they share stays, but the reader is gone from it, and if the reader was the head
somebody else becomes it — so the button opens a confirmation panel that says
which of those applies before anything is sent. A head is told explicitly that
"the longest-standing member takes over", because a head who thinks leaving
dissolves the household is making a different decision than the one they are
actually making.

The trigger stays in the DOM while the panel is open, carrying
`[attr.aria-expanded]`, rather than being swapped for the panel: a control that
vanishes when pressed takes the focus with it, and the member is left tabbing
from the top of the page to find what they just opened.

**The one refusal here is about children — and it stopped being absolute a
cycle after this was written.** A household with a minor's record whose last
remaining member leaves would leave that record with nobody able to manage it,
so the service answers 409 and the screen says why rather than offering a
control that will always fail. This paragraph used to say nothing on the
platform could delete a child record at all, true when leaving shipped and
false since: withdrawing the consent it rests on (above, on the child's own
card) removes it, and a household that does so can then leave. The refusal is
also narrower than "any child" now — a converted child holds their own account
and needs nobody to stay for them, so only a minor's record counts.

**A parent's own children are the one place this app does resolve names.** The
rule below — that names the portal cannot resolve are not invented — is about
ids belonging to people the reader has no particular claim on, where resolving
them would be a call per row. A parent's children are one call to
`/v1/children`, a list they already hold, and "Waiting for a place" against an
opaque id would be useless to the only person the Pathshala screens are for.
The class's teachers are named too, since 2026-09-27 - not by resolving ids, but
because a teacher became the Pathshala's own record and `my-class` sends their
names (never their contact details) with the class.

**Asking for a place asks for a name and a phone** - the ones the Pathshala will
call about the child, which its staff see whatever the member's directory
privacy setting says. The form says so, and starts on the member's own name and
mobile from `/v1/members/me`, fetched once and only when a child is picked.
**Messages a Pathshala sends to parents** head the Pathshala screen when there
are any (`GET /v1/pathshala/announcements`); failing to read them leaves the
rest of the screen alone. Each also arrives as an in-app notification.

**A Pathshala tile shows where it is, and it was printing "[object Object]".**
`address` used to be a string; the service has sent a structured address
(line plus country/state/city/locality ids) since the admin create form became a
cascade, and this screen's model never followed - the spec's fixture still used
the old string, so nothing failed. The directory now loads the Country, State,
City and Locality lists once and resolves ids through them, never a call per
tile; an id that does not resolve is left out rather than printed, and a failed
lookup leaves the free-text line on its own instead of failing the directory.

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

**"You have 12 notifications" counted the list, not the unread ones.** There was
no read state to count until there was, so Home set the badge to
`found.length` — a number that never moved no matter what the member did with
it, which is how a badge trains people to ignore it. `readAt` is now this
member's own and the notice says "unread".

**A broadcast is one row a whole Samaaj shares, so "New" is a claim about the
reader.** The service keeps read state per person; a Samaaj-wide announcement
read by four hundred others is still unread for this member. The Notifications
screen and Home both read `readAt` and never infer from anything else.

**Class names are checked against `styles.css` before they are used.** This
screen was written with `.muted` and `.visually-hidden`, neither of which
exists here — the app has `.small` and `.sr-only`. Nothing fails: the elements
render unstyled and look almost right, which is the same silent failure as the
`.warn` pill that shipped in the wrong colour and the module keys that matched
nothing. The tokens in a feature stylesheet need the same check; `--border` is
not a token in this app, `--line` is.

**Shared page primitives live in `src/styles.css`, not in a feature
stylesheet.** The pill colours (`.ok`, `.warn`, `.danger`), the two-column
`.grid2`, the `.progress` bar, `.back` and the table rules are all used by more
than one screen. Timeline shipped marking a pill `.warn` with nothing defining
it: the pill rendered in the wrong colour, which is exactly the kind of thing
nobody notices. Every one of those pill variants sets a text colour as well as a
background, so a state is never carried by hue alone.

**A Pathshala teacher who is not a member is turned away at sign-in
(2026-09-27)** - password or OTP - and signed out again, with a sentence
pointing at the admin panel. The account holds the teacher role and not the
Member role, so every member-facing read already refuses it server-side
(no `Members.Read`); refusing the session here is what saves them a portal full
of errors. A member who also teaches signs in as normal.

**After signing in the portal simply navigates to Timeline.** The wireframe
sent a member to their Samaaj's subdomain; there is no subdomain now, so there
is nowhere else to go but the one screen with something on it to read.

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

**An order's running total is a display convenience, not a submission.**
Business detail shows a total computed from the quantities a buyer has
typed, purely so they can see what they are about to send - the number
businesses-service actually records is computed server-side from each
product's price at the moment the order is placed, and the client's own
figure is never sent. Placing an order also omits any product left at
quantity zero, rather than sending a zero-quantity line for every product on
the business.

**The order form is simply absent from a business's own public page, for
its own owner.** businesses-service refuses ordering from your own
business; the screen does not wait to find that out from a 400 - `isOwner()`
compares the signed-in member's id (`AuthService.user()?.userId`) against
`Business.ownerMemberId` and swaps the form for a plain sentence instead,
the same "don't offer a control that always fails" reasoning the admin
panel's own Pathshala-create note already states.

**Nominating for Celebrities of Samaaj asked for a raw member id typed by
hand, and that was the whole reason it read as broken.** The service typed
`MemberId` as a `Guid` on the wire; a value that failed to parse crashed
ASP.NET Core's own JSON model binding with an unhandled 400 before
FluentValidation ever ran, and even a value that *did* parse was a GUID
copied off the member directory by hand into a bare text box next to a
placeholder saying to do exactly that. The nominee is found by typing their
name instead - `CampaignDetailComponent` fires `MembersApi.search()` through
a `switchMap` (cancelling a slower reply for "Rav" the moment "Ravi" is
typed, rather than adding a debounce this codebase has no other example of)
and `memberId` is set only by picking a result, never typed. The category
field, previously optional, is required as of the same cycle: a nomination
with no stated reason or speciality gave a reviewer nothing to judge it by.

**Same-Samaaj member search, without a directory screen's own paging or
filters.** The nominee picker calls the same `MembersApi.search(term)` the
Members directory uses, capped at the service's own default of 50 - a
nomination is one member found by name, not a browse, so nothing here needs
Locality filtering or pagination on top of it.

**Competitions is a new module this cycle, standalone from Events by
design (the user's own scope decision, not an oversight) - shaped after
Celebrities of Samaaj's list/detail split, with one deliberate difference.**
A campaign's ballot shows every candidate's vote count once the tally is
visible; a competition's entries never show a mark to an ordinary member at
all, before or after publication - only the published ranking's rank and
average. `RoundDetailResponse.canSeeScores` is what the screen reads to
decide, the same "ask the server, don't guess from a role" instinct
`tallyVisible` already established.

**Judging is a member-portal screen, not an admin-portal one, for the same
reason accepting a group's join request is a member act rather than an
admin one.** `CompetitionJudge` grants no platform role - a judge is simply
a member the organiser named, the identical "the president is just a
member" shape volunteer-groups-service already uses - so recording a mark
belongs where an ordinary member's own actions live. The score box is an
**upsert**, deliberately unlike casting a vote: submitting it again
corrects the mark rather than being refused, because correcting a
misrecorded score before judging closes is the ordinary case here, not
ballot-stuffing.

**Registering, withdrawing, and registering again all reach the same
underlying row.** `CompetitionRegistration` is reactivated rather than
duplicated, so a member who withdraws and changes their mind before the
window closes gets back the same registration rather than a second one -
mirrored by the screen simply calling `register()` again with no special
case for "I withdrew earlier."

**The Node server sends a content security policy and the other security
headers (2026-09-27)**, in `server.ts`, after the `/v1` proxy so API responses
keep the gateway's. The policy is this origin only, scripts held to `'self'`,
styles `'unsafe-inline'` for Angular's injected component styles; `x-powered-by`
is off. It holds only while every route renders on the client
(`app.routes.server.ts`): a server-rendered route would bring event replay's
inline script, which needs a CSP nonce wired first. Nothing may load from
another origin without widening the policy on purpose, and critical-CSS
inlining stays off in `angular.json` - its inline `onload` handler is exactly
what the policy blocks.

## Accessibility

The requirements call for WCAG 2.1 AA. **The audit was done on 2026-09-01**;
what it checked and what it found are below, so the next pass knows what has
already been looked at rather than starting over.

**Card titles became `h2` on 2026-09-02.** Six screens here went `h1` straight to
`h3`, leaving a level missing from the outline (WCAG 1.3.1) — the first audit
looked at labels, focus and live regions and did not check heading structure.
It was found while auditing the admin panel, and fixed in both apps at once,
because a finding that applies to both is not one app's finding. `.card h2` and
`.card h3` are given the size an `h3` rendered at before, so nothing moved on
screen; the one spec that selected on `.card h3` now selects `.card h2`.

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

**The erasure confirmation had the exact bug the first audit already fixed
elsewhere, on 2026-09-05.** `PrivacyComponent`'s "Erase my account" panel
rendered its trigger and its confirmation as an `@if`/`@else` pair, so
confirming replaced the button the member had just pressed and dropped
keyboard focus to the document body - the same WCAG 2.4.3 finding the first
audit fixed on three admin screens, on the single most irreversible action
this app has. `family.component.ts`'s own leave-household panel got this
right and even cites the finding in a comment; nobody had checked erasure
against it. The trigger now renders unconditionally with `aria-expanded`,
matching that panel exactly, and `askToConfirm()` toggles rather than only
opening, so pressing it again closes what is already open.

The warning paragraph inside the confirm panel also had no `role="status"`,
the WCAG 4.1.3 half of the same finding: nothing else moves focus when the
panel appears, so a screen reader user heard nothing. Fixed on the paragraph
itself rather than a wrapping element, so the surrounding `.card` keeps
whatever implicit role it already had.

Found by testing the live panel's DOM directly - `aria-expanded` and the
trigger's presence after activation - rather than by rereading the template,
which is what a second `@if`/`@else` pair reads as correct at a glance.

Not yet done: a pass with a real screen reader, and keyboard-only walkthroughs
of the longer workflows (the Boli bid form, the issue transitions). Those need a
person, not a script.

**Samaaj Panch is a new module, with no wireframe to translate.** Behind its
own key (`ModuleKeys.Panch`, off by default), under Community. Three things
the screens keep straight. **A ballot is up to ten names and is final:**
ticking stops at the seat count rather than letting somebody build an
eleventh choice and find out on submit, there is no tick box against your
own name (the service refuses a self-vote), and casting twice is reported
by the service as `accepted: false` rather than an error. **A null vote
count is not zero** - `tallyVisible` says whether this caller is shown
counts at all. **Open means the flags, not the status:** the screens read
`acceptsNominations`/`acceptsVotes`, because only the server knows the time
it is deciding against. Names are resolved once per seated member or
candidate through the directory (`members.get`) and fall back to "A member".

**Trusts is another new module, behind its own key (`ModuleKeys.Trust`, off
by default), and shipped with no screen in either app until now** -
`trust-service` itself landed across two backend-only cycles. **The roster is
read-only here on purpose**: membership is admin-curated (confirmed with the
user rather than derived from where a member lives), so unlike Volunteer
Groups this screen has no "Apply to join" at all, only the roster and the
elections. **The electorate is this Trust's own active roster, not the whole
Samaaj** - the identical rule volunteer-groups-service's own elections use -
so the nominee picker is a `<select>` built from the roster already loaded on
screen, the same reasoning the group detail screen gives for its own picker
rather than Celebrities of Samaaj's name search. Self-votes are refused and a
repeat vote or nomination comes back as success with `accepted`/`nominated:
false`, matching every other election on this platform.

**A Trust election fills a role's seats (2026-09-27)**, and its ballot is
Volunteer Groups' copied: one Vote button for a single seat, tick boxes capped
at the seats and one "Cast my ballot" for more, `myVoteCandidateIds` marking
every pick and `winnerMemberIds` naming every winner.
