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
| Samaaj / Tenants | `/tenants` | `#tenants` | built. Status, modules, logo, and the DPDP s.13 grievance contact |
| Create Samaaj | `/tenants/new` | `#createtenant` | built |
| Admin Users & Roles | `/admins` | `#admins` | built. Roles, re-issuing a one-time activation code, and suspending or reinstating an account |
| Invite Admin | `/admins/invite` | `#inviteadmin` | built |
| Role & Permission Matrix | `/roles` | `#rolematrix` | built, editable per Samaaj |
| Adult Child Conversion Queue | `/conversions` | `#conversionqueue` | built |
| Audit Logs | `/audit` | `#audit` | built |
| Timeline / Moderation | `/moderation` | `#content` | built |
| Jain Pathshala | `/pathshala` | `#pathshala` | built — the list |
| Pathshala detail | `/pathshala/:id` | — | built; no wireframe covers running one. Sessions, classes and the placement queue |
| Class detail | `/pathshala/:id/classes/:classId` | — | built. Teachers, timetable, roll, register, exams and results |
| Notifications | `/notifications` | `#notifications` | built — compose and recent, without the Audience and Channel dropdowns |
| Auctions / Boli | `/boli` | `#boli` + `#publishboli` | built. Occasions, and the publication queue with its confirmation |
| Occasion detail | `/boli/:id` | — | built; no wireframe covers running an auction. Types, opening a Boli, closing, recording |
| Events | `/events` | `#events` | built. Drafts and published together, create, publish, cancel |
| Event detail | `/events/:id` | — | built; no wireframe covers one. Who is going, who is waiting, and in what order |
| Celebrities / Voting | `/voting` | `#celebrity` | built. Campaigns and setting one up |
| Campaign detail | `/voting/:id` | — | built; no wireframe covers running one. Stage, ballot, nominations, tally, result |
| Volunteer Groups | `/groups` | `#groups` (member-facing) | built. Create with a president, and stand one down |
| Social Issues | `/issues` | `#issues` | built. The reviewer's approval queue |
| Members | `/members` | `#members` | built. Search the Samaaj directory and open somebody |
| Member detail | `/members/:id` | — | built; the wireframe's "View" had no screen behind it. The correction form |
| Reports, Settings | — | various | not built. Reporting is a later phase; Samaaj settings live on the Samaaj screen |

The unbuilt screens appear in the nav, disabled, each saying why. That is
deliberate: the wireframe promised them, and an administrator who cannot see
that Pathshala is coming has been told less than the wireframe did.

**What they say has to stay true.** Those reasons read "the events service does
not exist yet" long after every one of those services shipped, which told an
administrator something plainly false about their own platform. They now say the
admin *screen* is not built, which is the thing that is actually missing.

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

**Running a Pathshala was a curl-only activity.** Thirteen of the twenty-seven
endpoints the sweep found were this module: a parent could ask for a place and
nobody could open a session, create a class, place a child, mark a register or
record an exam. Three screens now cover it — the list and the detail screen set
a school up and answer the parents, and the class screen teaches it.

**The class screen needed two endpoints that did not exist, and both absences
were the same shape.** Marking a register *amends*: every mark not named in the
submission is left as it was. A form that could not read the existing marks
would therefore ask a teacher correcting one child to re-enter the other
twenty-four from memory — and a half-remembered resubmission does not fail, it
just leaves the register quietly wrong. Nothing could read a class's register
back. Likewise, scheduling an exam answered with its id and nothing listed them
again, so an exam set last week could not be marked this week.

Both were invisible from either side alone: the write paths were complete and
correct, and the parent's read paths were complete and correct. It took a screen
trying to use them together for the gap to show. `GET /classes/{id}/register` and
`GET /classes/{id}/exams` were added for this, with the marks arriving alongside
the exams because recording a result also amends silently.

**An unmarked child is not sent, and this is the rule on that screen most worth
protecting.** The mark control's third state is "Not marked", it is where every
child starts, and those rows are left out of the submission entirely. Defaulting
them to Present would invent attendance for a child nobody saw — and every
attendance number this platform reports, on every screen, is a count over
exactly those rows. Two tests hold it.

**Withdrawing is on the roll, not the placement queue.** It is the same
`DELETE /enrollments/{id}` a placement decision does not use, and the screen says
what it does: the child comes off the roll and their attendance and results
stay. A child who left in March still attended from June.

**Creating a Pathshala is now offered, to Super Admins only — and this file was
wrong about it for several cycles.** It said a create form "would be a control
that always answers 403", which is true of a Samaaj administrator and false
about this panel: a Super Admin uses it too, scoped into a Samaaj. The endpoint
was left with no caller at all as a result. The form appears when the caller
holds `SuperAdmin` **and** a Samaaj is selected — the command creates the record
inside whichever Samaaj the request is scoped to, so offering it with no scope
would be offering to create one nowhere in particular.

Worth generalising: a control that one role may use is not a control the panel
should omit. The role matrix screen has always got this right by rendering what
the server says the caller may do.

**Standing a Pathshala down is confirmed and not reversible here.** Every
session, class, register and exam result is kept — a Pathshala that closed still
taught the children who attended it. The screen says which of those two things
is happening, because "deactivate" alone reads like deletion.

**A volunteer group is created with its president, not after it.** The command
takes the president's member id and installs them, because a group with nobody
able to decide its applications is a group whose join requests go nowhere — the
same dead end as a Pathshala enrolment nobody could place. Standing one down
keeps its members and history and is reversible, so the screen offers "Bring
back" rather than leaving it looking deleted.

**The social issues queue is the mildest gap of the set, and the screen does not
overstate it.** A reviewer was not stuck: the member portal's issue detail
already renders `availableTransitions`, so somebody holding an issue's id could
decide it. What was missing was any way to find out something was waiting. The
buttons come from `availableTransitions` and nothing here derives them — that
service has the one workflow on the platform with real branches, so a second
copy in the panel would be the copy that drifts.

**Nominating somebody was a dead end until the decide endpoint had a screen.**
A member puts a name forward and it sits as `Nominated` — never on the ballot,
because approving is an administrator's act and nothing could perform it. Since
the service refuses `VotingOpen` on an empty ballot, that made a campaign
something a Samaaj could start and then could not run.

**The stage is one button, because the sequence has one next step.** Draft →
NominationsOpen → VotingOpen → Closed, and publishing is its own call rather
than a sixth status. Four buttons with three refusals would be offering choices
that were never there — the same reasoning as the Boli occasion's single
forward move.

**Removing a candidate stops being offered once voting opens.** Removing them
then would discard the votes already cast for them, so the service refuses it
and the screen does not draw a button that always answers 409.

**Publishing is confirmed, and for a different reason than a Boli result.**
There, a repeat announcement changes nothing. Here a second publish would
compute a second ranking, and two rankings leave "the result" with no referent —
so the service refuses the second, and the confirmation is not ceremony.

**Zero votes and a tally you may not see are drawn differently.** `votes` is
null when the count is hidden from the caller, and the screen says "Not visible"
rather than "0". Zero is a claim, and the wrong one. An administrator sees the
count throughout even on a `HiddenUntilClose` campaign, and the screen says so,
because somebody has to be able to tell whether the thing is working.

**The window rule is duplicated from the service and must stay in step.** The
form refuses a voting window starting before nominations close, exactly as
`CreateCampaignCommandValidator` does, so that members who vote early see the
same ballot as members who vote late. Stricter here would be a campaign nobody
can create; looser would be a round trip that reads as a bug.

**The wireframe's "Eligible voters: 1,104" is gone, not faked.** That count
lives in member-family-service and the directory call this panel makes is capped
at a hundred — a number quietly wrong on any Samaaj larger than that is worse
than no number, the same reason the tenant list dropped its Members column.

**Members could register for events nobody could create.** The member portal's
events screens — the capacity pill, the waitlist, the promotion when a place is
given up — had been shipped against events that could only be conjured with
curl. Unlike the Boli and Pathshala gaps, this one needed no new endpoint: all
four were there and simply had no caller.

**The events screen shows drafts, and that is what it is for.** Creating and
publishing are separate commands because an event exists in somebody's head long
before the Samaaj should be told about it. `includeDrafts` is honoured for a
caller holding `Events.Publish` and quietly ignored for anyone else — the service
answers with the published list rather than 403, since refusing would tell a
member drafts exist at all — so the screen asks for them unconditionally.

**No denominator is the unlimited case, not a missing number.** The RSVP column
reads "186 / 200" with a capacity and "94" without, exactly as the wireframe drew
it. Capacity is nullable and the service refuses zero, so a blank box is sent as
null: `capacity || null` rather than `capacity ?? 0`, which would turn "no limit"
into the one value that means an event nobody can attend.

**Cancelling asks for a reason, and the reason is not a formality.** The service
requires one because everybody registered is told it, and a cancelled event
cannot be republished. The registrations survive on purpose — an attendee list
that vanished with the event is one nobody can notify — so the detail screen
keeps showing them.

**The attendee list is three lists.** Going, waiting, and gave up a place. The
waitlist is rendered in arrival order because that order is the substance: the
longest wait comes off the queue first, and a promoted member keeps the position
they had. Somebody who cancelled is not in the queue at all, and numbering them
in it would put a position against a person who is not waiting for anything.

**Members could bid on nothing.** Every Boli endpoint an organiser needs —
announcing an occasion, moving its status, defining a type, opening a Boli,
closing it, recording a result, announcing it — was complete, tested and
curl-only. The member portal's bidding screens had been shipped for cycles
against auctions nobody could open.

**The publication queue needed an endpoint that did not exist, and its absence
was invisible from both sides.** Recording a result and announcing it are two
acts on purpose, so a result sits between them; the only read that reached one
needed the Boli id you were already looking for. `GET /v1/boli/results/pending`
is that queue, and it is the wireframe's "Results Awaiting Publication" card,
which had been drawn but never answerable.

**The queue names an amount and never a winner, and the wireframe drew a
winner** ("₹18,400 — Member ID 1042"). boli-service names the winner in exactly
one shape and only once it is published, for everybody including the manager who
recorded it. Nothing is lost by the omission: the winner is read from the
highest bid and is not something the publisher chooses, so the amount is what
identifies it. Faking the wireframe's line here would have meant a second shape
carrying the winner early — replacing an invariant that is easy to keep with one
that is not.

**Publishing is confirmed inline rather than on its own screen.** The wireframe
made `#publishboli` a separate screen for the irreversibility warning and the
deliberate second click. Both are here; the screen is not, because the queue row
already carries everything that screen showed and there is no endpoint that
reads a single pending result — so a separate route would have had to fetch the
whole queue to draw one row of it.

**A 403 from the queue is not an empty queue.** `Boli.PublishResults` is a
separate permission from `Boli.Manage`, so a manager who may run an auction but
not announce its result gets told the queue belongs to somebody else. Telling
them nothing was waiting would be telling them something false about their own
Samaaj.

**Money conversion moved to `libs/shared`, and that is the rule working rather
than an exception to it.** `formatRupees`/`parseRupees` lived in the member
portal's Boli feature until this screen needed to type an amount; a type or a
function moves to the shared library when a second app needs it, not in
anticipation. Amounts are integer paise on the wire, and `parseRupees` rounds
rather than truncating — a detail that is worth exactly one implementation.

**`isNotFound` had three copies before it had one home.** It now lives in
`core/http-status.ts` alongside `isForbidden`. A duplicated predicate is
harmless until it is the one deciding whether a screen says "the module is off"
or shows a red error banner.

**Creating a Pathshala is deliberately not on these screens.**
`CreatePathshalaCommand` is Super Admin only (`DATA-MODEL.md` §9): the master
record belongs to the platform operator and everything about *running* it
belongs to the Samaaj. A create form on a Samaaj administrator's screen would be
a control that always answers 403.

**The placement queue makes two calls, and the second one is the point.**
pathshala-service stores a child by id and nothing else, so the queue is a list
of GUIDs. `GET /v1/children/names` resolves exactly the ids on screen - names
only, because the full child record carries a date of birth and the
parental-consent record. Rather than have one service reach into another per
row, the panel does what it does for post authors and member names: it is the
party already authenticated to both.

**Nothing on the platform could approve a post before the moderation screen.**
A member writes one, it lands `PendingReview`, and the only way it ever reached
the Samaaj's timeline was somebody curling the moderate endpoint. Both that and
the queue endpoint existed; no screen in either app called either. Worth
repeating as a habit: an endpoint with no caller is where these gaps live.

**The moderation buttons come from the server.** Each queue row carries
`availableDecisions` from `TimelinePost.AvailableDecisions`, and the screen
renders exactly those. Deriving them from the status would put a second copy of
the rule in the panel - the mistake the member portal's social-issues screen
exists not to make.

**A 404 from the moderation queue means the module is off, not that something
broke.** The gateway answers 404 for a Samaaj that has switched `community` off,
so that a Samaaj without the module is indistinguishable from a platform with no
such feature. Reporting it as an error would send an administrator hunting a bug
that is a setting.

**The author's name is resolved, in one call.** The queue carries member ids;
the panel loads the directory once and maps them, the same approach the member
portal takes, and falls back to "A member" rather than printing an id. An
administrator's directory includes members who have taken themselves out of it,
so a moderator does not meet an unresolvable author because that member chose to
be unlisted.

**The Compose form drops two of the wireframe's four fields.** Audience offered
"All Members" across every Samaaj and "Specific Role"; Channel offered
"In-App + Email" and "In-App + SMS/WhatsApp". None of the four is real. A write
that crosses every tenant is not something this platform does and should not
arrive as a side effect of a dropdown; role membership lives in
identity-tenant-service; and audit-notification-service holds no directory of
member addresses, so there is no set of addresses to send a Samaaj-wide message
to. The scope banner already names the Samaaj being written to, so the card says
which rather than offering a choice of one.

**The Recent table shows a read count, not "Delivered".** An in-app
announcement is delivered the moment the row is written, so a Delivered pill -
which the wireframe drew - is a word that is always true and therefore says
nothing. How many members opened it is the question somebody opens this screen
to ask, and nothing stops the same announcement being sent twice, so the list is
also what makes a duplicate visible before it is sent.

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

**Two API-client methods had no caller, and the endpoint sweep could not see
either.** `scripts/unreachable-endpoints.sh` finds callers by looking for `/v1/`
literals in app code — and every endpoint's literal lives in `admin-api.ts`, so
an endpoint whose *client method* nothing calls still counts as reached. The
dead end simply moved one layer up, which is what
`scripts/uncalled-api-methods.sh` now sweeps for.

**`setGrievanceContact`** is the one that mattered. DPDP section 13 requires a
Data Fiduciary to publish who answers a member's complaint about their data;
`DPDP-COMPLIANCE.md` marked the obligation **built**, and the only way for a
Samaaj to actually name anybody was curl. The Samaaj screen carries the control
now, and a Samaaj that has named nobody says so on its row without anything
being opened.

**Suspending an account had no screen at all, not merely no caller.**
`SetUserSuspensionCommand`, the step-up it requires to suspend, the self-suspend
refusal and the erased-account refusal were all built and tested against a
running database, and `PUT /admins/{userId}/status` was in `API-CONTRACTS.md`
from the day it landed — but `AdminListComponent` never called it, so a Samaaj
administrator with a problem account had no way to act on it short of asking
the platform operator to archive the whole Samaaj. The button sits beside the
status pill it changes, next to where re-issuing a code already lives for the
same reason: both are things a Samaaj does to one account rather than a
separate workflow. Suspending asks for the caller's own password first, the
same asymmetry the Samaaj status control already draws between taking
something out of service and restoring it; reinstating is one click, because
it is reversible by the very click that undoes it.

**`issueActivationCode`** is the one that made this file's neighbour lie. The
Invite screen has told administrators from the day it shipped that "a lost code
is re-issued from the Admin Users screen, which cancels this one" — and nothing
on that screen called it, so an account stuck at Pending Activation stayed stuck
while the dashboard counted it every day. The button is on the status cell of a
pending row, and the panel says the earlier code has been cancelled, because an
administrator who hands out a second code without knowing that leaves somebody
holding a dead one.

**The grievance form duplicates the service's rule and must stay in step.** A
name with no email and no phone is refused, because that is not a means of
redressal; clearing all three is allowed, because removing a contact is not the
same act as naming an unreachable one. The smoke run checks the refusal against
the service, so the copy here cannot quietly become stricter or looser than the
thing that decides.

**And backticks do not belong in an inline template's HTML comments.** An
Angular component's template is a JavaScript template literal, so a backtick
inside a `<!-- -->` ends it — the compiler then reports NG1002 "Incorrect number
of arguments to @Component decorator" and a cascade of syntax errors pointing at
lines that are fine. This has now cost two cycles.

**The Members screen closed a permission that was granted and unusable.**
`Members.Write` has been on SamaajAdmin since the catalogue was seeded and
`SERVICES.md` has always said it lets an administrator correct anyone's profile
in their Samaaj. There was no screen — and the endpoint that existed could not
have been used correctly by one. `PATCH /v1/members/{id}` replaces the profile
whole and therefore requires the member's privacy levels and whether they are
listed, and **no read an administrator can make returns either**. A form built
against it would have had to guess, and an unreadable level parses as Private,
so the likeliest accident was quietly hiding every field a member had chosen to
share.

`PATCH /v1/members/{id}/details` carries no privacy fields at all, which is why
this form has no privacy controls and no directory tick. They are not omitted
for space, and the note under the form says so: a control that cannot be saved
is worse than an absent one, and this panel has been wrong in that direction
before — it told itself a Pathshala create form "would be a control that always
answers 403" and left the endpoint with no caller for several cycles.

**Three of the wireframe's five Members columns are gone, and none of them is a
layout decision.** **ID** ("MEM-00124") is an identifier this platform does not
have; a member's id is a GUID nobody reads out over the phone, and a short code
would be one nothing else on the platform knows. **Family** would be a call per
row into a household this endpoint does not return, and a household's membership
is other members' data — so putting it on a directory row is a privacy decision.
**Status** belongs to identity-tenant-service, and its pending accounts already
have a screen under Admin Users. Locality takes their place because it is what
the search actually filters on.

**Profession is shown and cannot be searched, and the screen says why.** The
field carries a per-field privacy level, so a server-side filter on it would let
anybody confirm a private value one query at a time — member-family-service
refuses to offer one for that reason. A box quietly missing from a search form
reads as an oversight; a sentence saying it is a privacy decision does not.

**Class names are checked against a stylesheet before they are used, the same
rule member-portal's own `CLAUDE.md` states and this app had never written
down.** Six classes across seven screens matched nothing, on 2026-09-05:
`.input.inline` was pasted into four screens' own local styles with a drift
already between two of the four copies (`max-width: 220px` in three,
`200px` in the fourth) before becoming a fifth screen's silently-unstyled
class instead of a fifth copy; `.code` and `.confirm` each existed in exactly
one screen's local styles while a second screen used the class and defined
nothing. None of the three failed anything — the elements rendered unstyled
and mostly looked right, which is the same silent failure member-portal's own
`.muted`/`.visually-hidden` finding and the `.warn` pill in the wrong colour
already were. All three are now in `src/styles.css` once, and the four
duplicate `.input.inline` copies are gone.

Two more were plainer mistakes: `class="btn secondary"` on three buttons where
every other screen uses `.btn.alt` — `.secondary` was never a class this app
had, so the button rendered as a bare `.btn`, not the alt style two of the
three needed on a Cancel/Clear action. And three `<div class="field">`
wrappers around a label and an input, styled nowhere, doing nothing that
`label`'s own `margin-top` was not already doing — unlike the other five, this
one is not a class this app needed at all: every other form here places a
label and an input as direct siblings, so the wrapper is gone rather than
given a rule to keep it consistent with a pattern this app doesn't otherwise
use.

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

**A second pass was done on 2026-09-02**, because the first one predated the
eight screens built after it — Pathshala classes, Boli, events, voting, groups
and social issues had never been looked at. Four things came out of it, and all
four were also true of screens the first audit *had* covered.

**A confirmation that replaces its own trigger drops keyboard focus to the
body.** Three screens did this: Boli publication, campaign publication and
standing a Pathshala down all rendered the panel in an `@else` branch, so the
button the user had just activated left the DOM and took the focus point with it
(WCAG 2.4.3). Disabling the trigger instead is the same bug wearing a hat — a
disabled control is blurred and removed from the tab order. The trigger now
stays, and stays enabled, with `aria-expanded` saying what it did; pressing it
again re-opens what is already open, which is harmless. The events screen had
got this right by accident and is now explicit about it.

**Those panels are `role="status"`.** New content appearing after a click is
seen and not heard otherwise, and what appears here is the warning that the
action cannot be undone (WCAG 4.1.3).

**A third shape of confirmation had the same gap and was missed on both
passes, because it gets the *other* rule right.** Tenant deactivation's
"asks first" panel — and Admin Users & Roles' account-suspension panel,
copied from it on 2026-09-05 — keeps its trigger in the DOM with
`aria-expanded`, so neither was caught by the "replaces its own trigger"
finding above. But the warning paragraph inside (`Deactivating {Samaaj}
signs out every one of its members…` / `Suspending {name} signs them out
immediately…`) had no `role="status"` either, so a screen reader user still
heard nothing when it appeared — the identical WCAG 4.1.3 gap, on a
differently-shaped panel the first two passes had no reason to re-check.
Fixed on the warning paragraph itself rather than the surrounding `<form>`,
so the form keeps its own landmark role rather than trading it for a status
region.

**Every table has a `<caption class="sr-only">`.** There are 23 of them and none
had an accessible name; five screens draw three apiece, so a screen reader user
listing the tables on the class screen got "table, table, table". The caption is
1×1px rather than `display: none`, which is what keeps it in the accessibility
tree.

**Card titles are `h2`, not `h3`.** Every screen in both apps went `h1` straight
to `h3`, which leaves the outline with a level missing (WCAG 1.3.1). Sub-headings
inside a card stay `h3` and now sit correctly under the card's `h2`. Both
stylesheets give the two the size an `h3` rendered at before, so nothing moved on
screen — verified in a browser against the built CSS, at 18.72px in both apps.
The member portal had the same defect in six screens and was fixed with it: a
finding that applies to both apps is not an admin-panel finding.

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
