# admin-portal

The Angular app Samaaj and platform administrators use. Read the root
`CLAUDE.md` §7 first for the conventions this follows, and
`.claude/skills/wireframe-to-angular/SKILL.md` before translating another
screen. The spec is `docs/product/wireframes/admin-panel-wireframes.html`.

## Screens

| Screen | Route | Wireframe section | Status |
|---|---|---|---|
| Sign in | `/login` | — (the wireframe has none) | built. A CAPTCHA and the password, then a 6-digit code on an untrusted browser (trust it for 30 days) - `SignInFlow` in libs/shared, shared with the member portal |
| Dashboard | `/dashboard` | `#dashboard` | built — see below |
| Samaaj / Tenants | `/tenants` | `#tenants` | built. Status, modules, logo, and the DPDP s.13 grievance contact |
| Create Samaaj | `/tenants/new` | `#createtenant` | built |
| Admin Users & Roles | `/admins` | `#admins` | built. Roles, re-issuing a one-time activation code, and suspending or reinstating an account |
| Invite Admin | `/admins/invite` | `#inviteadmin` | built |
| Role & Permission Matrix | `/roles` | `#rolematrix` | built, editable per Samaaj |
| Families & Children | `/families` | — | built; no wireframe screen lists households. Each household with its members (where they live, who is head) and minor children as ages, filtered by locality, paged; links to the conversion queue |
| Adult Child Conversion Queue | `/conversions` | `#conversionqueue` | built |
| Audit Logs | `/audit` | `#audit` | built |
| Timeline / Moderation | `/moderation` | `#content` | built |
| Jain Pathshala | `/pathshala` | `#pathshala` | built — the list, as tiles with Open, Edit (a modal) and Stand down |
| Pathshala detail | `/pathshala/:id` | — | built; no wireframe covers running one. Nine tabs: sessions, classes, teachers, students (with the placement queue), exams, progress report, attendance, donations, announcements |
| Class detail | `/pathshala/:id/classes/:classId` | — | built. Five tabs - Register (first), Roll, Exams and results, Timetable, Teachers (picked from the Pathshala's teacher records) - with assigning a teacher, adding a slot and setting an exam in popups. The Pathshala page's own session, class, donation and announcement forms are popups too |
| Notifications | `/notifications` | `#notifications` | built — compose and recent, without the Audience and Channel dropdowns |
| Auctions / Boli | `/boli` | `#boli` + `#publishboli` | built. Occasions - announced and corrected (until closed) in a popup, with a locality - and the publication queue with its confirmation |
| Occasion detail | `/boli/:id` | — | built; no wireframe covers running an auction. Types, opening a Boli, closing, recording |
| Events | `/events` | `#events` | built. Drafts and published together, create, publish, cancel |
| Event detail | `/events/:id` | — | built; no wireframe covers one. A page: a photo cover (or a calendar leaf), tabs - About, Schedule, Going (who is going, who is waiting, and in what order) - and Edit, one popup for the details and the photo |
| Celebrities / Voting | `/voting` | `#celebrity` | built. Campaigns, setting one up, and editing one (a modal, until voting opens) |
| Campaign detail | `/voting/:id` | — | built; no wireframe covers running one. Stage, ballot, nominations, tally, result |
| Competitions | `/competitions` | — | built; no wireframe covers this new module. Competitions; setting one up and correcting one (until registration closes) in a popup, with an optional locality |
| Competition detail | `/competitions/:id` | — | built; no wireframe covers running one. Stage, judges, rounds, entries, publish results |
| Samaaj Panch | `/panch` | — | built; no wireframe covers it. The seated Panch with Remove, and the elections list with a create modal |
| Panch election | `/panch/:id` | — | built. Stage, approving nominations, the tally, publishing (which replaces the Panch) |
| Volunteer Groups | `/groups` | `#groups` (member-facing) | built. Create (president optional), set a president later, and stand one down |
| Group roles & elections | `/groups/:id` | — | built; no wireframe covers running one. Define volunteer roles and their seats, assign them, open an election for any role, approve nominations, move its stages, publish - the president or an administrator of the group |
| Trusts | `/trust` | — | built; no wireframe covers this new module. This Samaaj's locality Trusts; setting one up and renaming one in a popup |
| Trust detail | `/trust/:id` | — | built; no wireframe covers running one. Six tabs: members (add in a popup, a role picker per row, remove), roles (define with seats, rename/resize, remove), elections (set up for a role, approve nominations, move stages, publish), donations (pledge, then collect against a receipt number), collections (what a Boli winner or a pledge owes; record the payment), expenses (record, approve) |
| Social Issues | `/issues` | `#issues` | built. The reviewer's approval queue |
| Members | `/members` | `#members` | built. Search the Samaaj directory and open somebody |
| Member detail | `/members/:id` | — | built; the wireframe's "View" had no screen behind it. The member's page - photo, details, address, household, volunteer groups - with the correction form behind "Correct details". Every member list in this panel links here |
| Change password | `/change-password` | — | built; no wireframe covers it — the wireframe's sign-in screen has no forgot-password link at all |
| Reports & Analytics | `/reports` | `#reports` | built — the three per-Samaaj cards only. The wireframe's Super-Admin-only cross-tenant comparison table is a deferred follow-up, see below |
| Settings | `/settings` | `#settings` | built — Security only, see below. Tenant Branding and Module Controls point at the Samaaj screen, which already covers them; Notification Providers stays honestly unbuilt |

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

**The dashboard's three remaining tiles turned out to need no new endpoint at
all, and one of them nearly cost a privacy rule this platform already has.**
Events and Auctions are each exactly the count of the list its own screen
already loads (`listEvents(true, false)`, `listOccasions()`) — no aggregate
was missing, just a caller. Students very nearly got a real backend field
(`PathshalaResponse.StudentCount`, computed once per Samaaj instead of per
Pathshala) before it became clear that response is `ListPathshalasQuery`'s,
gated on `Members.Read` alone — the same permission every member holds, and
the same endpoint member-portal's own Pathshala list calls for a parent.
member-portal's list screen has its own comment explaining why it does not
show a student count there: *"a roll size is a fact about other people's
children."* Adding the field to the shared response would have handed every
parent exactly the number that comment says they should not have. The tile
instead calls `pathshala(id)` once per Pathshala the Samaaj runs — the same
per-Pathshala detail call the directory screen already makes when an
administrator opens one — and sums the classes' student counts across all of
them. A Samaaj running many Pathshalas at once would be the reason to add a
manager-only aggregate endpoint later; nothing in this platform's data
suggests that is the common case yet.

**Reports & Analytics ships its three per-Samaaj cards and deliberately not
the wireframe's Super-Admin-only tenant comparison table.** The three cards
needed one small, genuinely new, admin-only query each in member-family-service
and pathshala-service (events needed none — `registeredCount` was already on
the wire). Neither repeats the near-miss the dashboard's Pathshala tile had:
both are gated `SamaajAdmin`/`SuperAdmin` from the point they were created,
never routed through a permission a plain member already holds.

The comparison table is a different shape of problem and stayed out of this
cycle on purpose. Every query on this platform — including these two new
ones — is scoped to one tenant by the global `ITenantScopedEntity` query
filter; a table with one row per Samaaj needs either a genuinely new
cross-tenant aggregate endpoint in three services (bypassing that filter
under a SuperAdmin-only gate, something this platform has never built) or a
fan-out of one `X-Tenant-Override-Id` request per tenant per metric — the
same N+1 shape flagged above for the Pathshala tile, multiplied across every
Samaaj on the platform instead of one. Asked, the choice was to ship what
composes cleanly now and scope the comparison table as its own piece of work
later, rather than pick between inventing a new architectural pattern under
time pressure and shipping a fan-out already known to be the wrong shape.

**Settings' four wireframe cards turned out to be four different sizes, and
only one needed new work.** Module Controls and Tenant Branding's logo and
contact fields are already built, on the Samaaj/Tenants screen — the
wireframe's own JS even navigates there for Module Controls rather than to a
second copy, so this screen's cards do the same. "Colors" is not built:
nothing in either Angular app has a per-tenant theming system to apply one
to, and a stored colour nothing renders would be exactly the half-finished
feature this platform's conventions warn against, so it stayed out rather
than being added as an unused field. Notification Providers stays honestly
unbuilt — there is no real email, SMS or WhatsApp provider anywhere on this
platform, only the logging stand-in, and a "Configure" control for a
provider that does not exist would be a screen promising something untrue,
the same reasoning this file's own "what they say has to stay true" note
already states about the nav's disabled items.

**Security is the one real capability this screen adds**, and it needed
genuinely new work in identity-tenant-service: five platform-wide constants
(password floor, OTP lifetime, lockout threshold and duration, session
length) became a Samaaj's own sparse overrides, resolved through one new
`ISecurityPolicyResolver` rather than five call sites each reading a
constant directly. This screen's own tenant-scoping is not the dashboard's
or the Reports screen's: a Samaaj Admin's own tenant never runs through
`AdminScope`, which only ever holds a Super Admin's *chosen* Samaaj, so the
Security card falls back to `auth.user()?.tenantId` for anyone who is not a
Super Admin — reading the platform sentinel `Guid.Empty` off that same field
for a Super Admin would silently ask the API for a nonexistent tenant's
policy, which is why that fallback is guarded by `isSuperAdmin()` rather than
applied unconditionally.

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

**The register's date is checked before it is sent (2026-10-24).** The service
refuses a day still to come and a day the class does not meet, and the screen
used to find out only by asking: it opened on today's UTC date whatever the
timetable said, so a Saturday-only class opened on a day it could not be marked,
and the next obvious pick - the coming Saturday - was refused as "before the
class has met". It now opens on the last day the class meets, caps the picker at
today on the browser's own calendar, says which days the class meets, and names
the problem with Submit disabled. A class with no timetable meets any day, as
the service has it.

**A Pathshala's donations are paid or announced (2026-10-24).** Recording one
asks which: paid now (with its mode, and a receipt number), or announced to be
paid later (no mode, no receipt). Each row has Edit and Delete, and an
announcement also has Mark paid, which is what issues its receipt. The ledger
shows what was received and what is still to come, and a Show filter narrows it
to paid, not paid yet, or deleted. Deleting a paid entry asks why and keeps
the row in the service - a receipt number is never issued twice - so it is
under Show. Deleting an announcement erases it, asking for nothing, because no
receipt names it. This replaced Void and a stray Cancel
that the popup conversion had left in every row.

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

**A Pathshala is run from nine tabs (2026-09-27)**, each its own component
beside `pathshala-detail.component.ts` and each loading when opened, so a
Pathshala costs one call to open rather than nine. Sessions and classes come
with the Pathshala; Teachers, Students, Exams, Progress report, Attendance,
Donations and Announcements read pathshala-service's staff endpoints. Somebody
who runs it sees the forms; one of its teachers sees the same tabs read-only,
narrowed by the service to the classes they teach, and can message parents.
Donations is not drawn for a teacher at all - who gave how much is the
administrators' to keep, and the service refuses them the ledger. A class page
links back with `?tab=classes`.

**A teacher is the Pathshala's record, not a member.** The Teachers tab adds,
edits and removes records with a name, phone, email and qualification; the class
screen's picker offers those records. A login is optional and is two acts on two
services - `setRole(PathshalaTeacher)` for an existing member (whose member id
is their account id), or Invite Admin's endpoint for a new account, and then the
account id written onto the record. Only a Samaaj Admin or Super Admin may grant
a role, so only they see "Give a login"; an Area Admin keeps the record and asks.
A new login's activation code is shown once in the dialog and leaves the DOM
with it, and it is kept even when linking fails after the account was made - a
code never shown is an account nobody can activate. Unlinking leaves the role on
the account, and the dialog says so.

**The placement queue moved onto the Students tab**, beside the placed
children, each row with the name and phone the parent gave when they asked. It
reads `GET /pathshalas/{id}/students`, which carries waiting requests for an
administrator, so `GET /enrollments/requests` has no caller in this app any
more; it stays for the smoke run. Placing offers only the current session's
classes. A request made before the form asked for a contact says so.

**Editing and standing a Pathshala down moved from its detail screen to its
tile in the list (2026-09-26)**, the same place Events and Groups put them.
Creating one stays a Super Admin's act; correcting one is `Pathshala.Manage`,
because a wrong address is the Samaaj's to fix and the address is also what
decides which Area Admin runs it. "Edit" opens the modal Events and Groups use, on the same
Country -> State -> City -> Locality cascade as the create form, with every
level above a set one loaded before it is shown - a select whose options have
not arrived renders blank, which would read as the address being empty. An
Area Admin sees the cascade disabled: the service refuses to move a Pathshala
out of their locality, so the form does not offer to. Save replaces all
three fields and re-reads the Pathshala.

**Standing a Pathshala down is confirmed and not reversible here.** Every
session, class, register and exam result is kept — a Pathshala that closed still
taught the children who attended it. The screen says which of those two things
is happening, because "deactivate" alone reads like deletion.

**A volunteer group may be created with or without a president (changed
2026-09-19; it was required before).** The create form's president select
starts on "No president yet" and the list says "Not set" for such a group, with
a "Set president" control that is the same panel as "Change". A group with none
is not a dead end any more: a Samaaj admin accepts its joiners and can run a
"President" election until one exists - members nominate and vote in the member
portal, the administrator opens it, approves the ballot and publishes it from the
group's own screen here - and that election's winner becomes the real president
(see volunteer-groups-service's own notes).
Standing one down
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

**Event type is optional, resolved to a label here rather than left an id.**
`SamaajEvent.eventTypeId` names a master-data-service `LookupItem`
(`Category == EventType`), added 2026-09-12 alongside the same
`BusinessCategory` zero-migration pattern — except this one needed its own
additive migration on `events-service`'s own table, since a `LookupItem`
category is free but a *consumer* storing the id still needs a column for
it. The create form's dropdown and the detail screen's own label both call
`AdminApi.listLookupItems('EventType')` directly; a Samaaj that has not
seeded any values yet gets an empty list rather than a broken form — the
field stays optional for exactly that reason. Cover photos for events
arrived on 2026-10-24 (events-service's `EventPhoto`); at the time they were
scoped out of this pass: every other stored image on this platform (member
photos, children's photos, Samaaj logos) needed its own `ImageContent`
sniffing type, storage table and upload/get endpoints built per service
from scratch, and bundling that alongside a zero-migration lookup category
would have been a second, much larger feature riding on this one's name.

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

**The create form's address became a Country -> State -> City -> Locality
cascade on 2026-09-12, replacing one free-text field.** Each select is
populated only once its parent is chosen, the same discipline the Master
Data screen's own Cities/Localities tabs already apply, and every level is
optional: a Pathshala can be created with only a free-text address line and
nothing picked. The Pathshala list card shows only that free-text line, not
a resolved City or Locality name — resolving one would mean a lookup per
Pathshala on a screen that lists every one of them, the cost this app avoids
everywhere else it has a choice between an id and a cheap name.

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

**No account on the platform could ever change its own password, until the
same day this file's top bar name became a link.** Not a Samaaj Admin's, not
the bootstrap Super Admin's - `identity-tenant-service`'s `User` aggregate had
exactly two places that ever wrote a password hash, construction and a
one-time activation redemption, and neither runs again once an account is
already active. The wireframe's sign-in screen has no forgot-password link at
all to translate, so this panel's own top bar is where the control lives:
the caller's own name, which used to be a bare `<span>`, is now a
`routerLink` to `/change-password` - the same pattern member-portal's shell
already uses for its user chip linking to `/profile`. Changing a password
signs out every other device the account is signed into; the screen says so,
rather than leaving whoever is signed in elsewhere to wonder why.

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

**The correction form's fields followed member-family-service's structured
address, Gautra and Qualification change on 2026-09-11.** `Address`/`Locality`
free text became a Permanent and Temporary address pair, each a line plus the
same City→Locality cascade member-portal's own My Profile now uses, plus new
Gautra and Qualification `<select>`s — all sourced from `AdminApi`'s existing
`listGautras()`, `listCities()`, `listLocalities()` and
`listLookupItems('Qualification', false)`, already on this client from the
master-data screen. The form still carries no privacy controls: that rule did
not change, only which factual fields it corrects. The Locality search filter
on the list screen made the same free-text-to-dropdown swap, resolved against
the same full Locality list fetched once.

**Profession is shown and cannot be searched, and the screen says why.** The
field carries a per-field privacy level, so a server-side filter on it would let
anybody confirm a private value one query at a time — member-family-service
refuses to offer one for that reason. A box quietly missing from a search form
reads as an oversight; a sentence saying it is a privacy decision does not.

**The correction form's free-text Profession input became a `<select>` on
2026-09-12, one cycle after the structured-address/Gautra/Qualification
change.** `Profession` was the one field that change deliberately left as
free text; it moved onto master-data-service's `ProfessionType` category —
already one of the platform's original four flat lookup lists, so nothing
needed to be added to master-data-service itself — the same "point an
existing screen at an existing list" shape `Qualification`'s own `<select>`
already used. `AdminApi.listLookupItems('ProfessionType')` is the only new
call; `listLookupItems` was already generic per category. `form.profession`
became `form.professionId`, and the request body's field follows suit.

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

**A campaign can be edited until voting opens, and the form is a modal now.** There
was no update command at all, so a wrong date or title meant a new campaign.
`UpdateCampaignCommand` (`PATCH /campaigns/{id}`, the same permission as
creating one) allows a Draft or a campaign taking nominations and answers 409
from voting onward: people have been told the windows and whether the tally is
live, and votes may exist. The screen mirrors that - Edit is not drawn for a
campaign the service would refuse - and the create form moved into the same
modal Master Data and Events use, with each window's own order now checked
beside the existing "voting starts after nominations close" rule.

**Competitions is a new module this cycle, with no wireframe to translate -
shaped after Celebrities / Voting's own two screens instead.** A
competition's registration window plays the same role a campaign's
nomination/voting windows do (`acceptsRegistrations`, status-and-clock
both), and the detail screen's "one stage, one button" reasoning is
identical: Draft → RegistrationOpen → RegistrationClosed → InProgress →
Closed, strictly forward, so the screen offers exactly the one move the
competition can make rather than four buttons and three refusals.

**Entries are created by the organiser, one at a time, in every round
including the first - there is no "advance the top N" button anywhere on
this screen, and that is deliberate.** `Round`'s own doc comment explains
why: a top-N cutoff has its own edge cases (a tie at the cutoff, a
withdrawal mid-competition) the backlog never asked for, and "does an
entry exist for this round" already answers "is this participant still in
it." The entry picker on a selected round only offers registered members
who do not already have one, computed client-side from
`registeredMemberIds` and the round's own entries - the same "let the
server's shape decide, don't re-derive it" instinct the role matrix screen
already gets right.

**This screen never shows an individual judge's mark, only a computed
average.** `RoundDetailResponse.canSeeScores` is true for the organiser
throughout - the same "an administrator sees the tally regardless" stance
Celebrities / Voting already takes - and recording an actual score is a
judge's own act, on member-portal, not something this panel offers at all.

**Publishing a round's result is not confirmed the way a campaign's is.**
Unlike celebrity-voting-service, publishing a closed round twice returns
the same frozen result rather than being refused, so there is nothing
irreversible about a second click to warn against - the button simply
disappears once `hasPublishedResult` is true.

**nginx sends a content security policy and the other security headers
(2026-09-27)**, from `security-headers.conf`, included in each location because
nginx drops inherited `add_header`s the moment a location sets its own. The
policy is this origin only, with no `'unsafe-inline'`: since 2026-10-21 each
`index.html` response carries a nonce (`$request_id`) in the policy, and
`sub_filter` stamps the same value on `<app-root>` as `ngCspNonce`, which
Angular puts on every `<style>` it injects. Three things follow for anybody
changing this app. **No inline `style="..."` attribute in a template** - a nonce
does not cover one, so it silently does nothing; use a class. **Nothing may load from another origin** - a
font from a CDN, an analytics script - without widening the policy on purpose.
And **critical-CSS inlining stays off** in `angular.json`'s production build:
it loads the stylesheet through an inline `onload` handler the policy blocks,
which left this panel unstyled the first time the policy went on. `/v1` is left
to the gateway's own headers.

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

**Make that 24, one of them missed on the day this was written and found on
2026-09-05.** The Role & Permission Matrix — this app's own widest table — had
no caption at all, the exact gap this paragraph describes, in the one screen
its own accessibility note already singles out for needing more attention, not
less. `scripts/table-caption-coverage.sh` now holds every table to this, and
CI runs it, so a 25th table missing one fails the day it lands rather than on
the next full re-read of every screen.

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

**Below 700px the sidebar is an off-canvas drawer, not a stack, and this was
member-portal's fix copied rather than a separate finding.** The old mobile
rule dropped `.side` to `position: static; width: 100%`, so an administrator
on a phone scrolled past every nav group - Overview through Operations -
before reaching a single word of the screen they opened the panel for. A
`navOpen` signal on `ShellComponent` now gates a fixed drawer that transforms
in over a backdrop, triggered by a floating hamburger button that stays in
the DOM at every width. It closes on a backdrop click and on any nav link
click. Both shells had the identical defect because both were built the same
way - a sidebar sized for desktop with a narrow-viewport rule that only ever
stacked it, never collapsed it.

Not yet done: a pass with a real screen reader, and a keyboard-only walkthrough
of the role matrix, which is the widest table in either app.

**Samaaj Panch is a new module, with no wireframe to translate.** It sits
under Community, and is off by default (`panch` module key; a 404 from
`/v1/panch/panel` reads as 'this Samaaj does not run it', not as an error).
An administrator convenes an election and runs its stages, but never
chooses who wins: this screen has "Set up an election" and "Remove" and
deliberately no "Appoint". The election detail is the campaign detail's
shape again - one button for the one next stage, approve or turn down a
nomination, a confirmed publish - with one difference the screen has to say
out loud: **publishing replaces the whole Panch**, so the confirmation
names that. Removing a member asks first and leaves the seat empty; nobody
is promoted. Names come from the member directory (`/v1/members`, first
hundred) and fall back to 'A member' rather than a GUID.

**Volunteer group elections had no screen in this panel, only in the member
portal, and the one election an administrator is meant to run was the case that
needed it most.** A group can be created with no president, and the service lets
a Samaaj admin run its *President* election - and only that - while none exists.
The member portal's group screen has the whole flow, but a Super Admin is not a
member and an administrator should not have to change apps to start the election
that is theirs. `/groups/:id` covers opening one, approving who reaches the
ballot, the stage button, the tally, and a confirmed publish. Nominating and
voting are deliberately not here - both need a seat in the group.

**Since 2026-09-26 an administrator runs a group's roles and every one of its
elections, at any time - not only the President election of a group with
none.** The same page now has a "Volunteer roles" card (define a role with its
seats, rename or resize it, remove one nobody holds - Volunteer and President
are built in and offered neither), a Members card with a role picker per row
(never President, and no picker on the president's own row, since that role
moves only with the presidency), and an election form that picks a role rather
than typing a title. What it offers is the service's own answer:
`group.iCanRunElections` for roles and elections, `group.iCanLead` for the
join-request queue, which an administrator still reaches only while the group
has no president. Publishing names every winner and says that whoever held the
role and lost goes back to Volunteer.

**An election that ran out of time is reopened by editing it, not by a separate
control.** Nominations left NominationsOpen past their own close date have
nothing to undo - the status never advanced - so `Edit` (Draft/NominationsOpen
only, mirroring the campaign screen's own rule) corrects the role and
all four windows, and pushing the nomination close date out is the whole fix.
The button disappears once voting has opened, the same reasoning every other
edit-until-a-point control in this panel already uses.

**The page also carries the join-requests queue, because an election with nobody to
nominate is a dead end.** The electorate is the group's own members, and a group
created with no president has none - so its nominee picker is empty (and was, in
the member portal, with no explanation) until somebody accepts its first joiners.
The service lets an administrator do that only while there is no president, the
same window as the President election, and this screen asks for the queue only
when `iCanLead` says it may (anybody else gets a 404).

**An Area Admin is invited like any other administrator, with one extra
field.** Ticking `AreaAdmin` on Invite Admin (either mode) clears every other
role and shows a Locality select; ticking another role clears `AreaAdmin`.
identity-tenant-service refuses both the mixture and a missing locality, so the
form never offers either. Once signed in, an Area Admin lands on Members
rather than the dashboard, sees Members, Volunteer Groups, Events, Jain
Pathshala and Social Issues in the nav, and is told which locality they
manage in a banner above every screen - the Samaaj-wide dashboard tiles are
not theirs to read. None of that is the control: each service applies the
locality from the token. The Members screen fixes its Locality filter to
theirs, because the service gives the administrator's view only to a search
confined to that locality - anything wider is the member directory they also
have as a member, which is not what this screen is for.

**Locality bounds the administrator, never the member.** Groups and events
carry an optional `localityId` now (a Locality column, and a select in their
modals); an Area Admin's select is fixed to their own locality, and a Samaaj
Admin's offers "The whole Samaaj". A row in another locality - or with none,
which is the Samaaj Admin's alone - shows "Another locality" and no controls,
and the event and Pathshala detail screens hold back their attendee list,
placement queue, sessions and classes for one, because the services answer
404 there and a screen should not ask for what it has been told it may not
have. `core/area-scope.ts`'s `AreaScope.administers(localityId)` is that one
check, true for anybody who is not an Area Admin. A Pathshala's locality is
its address's, so one created before localities mattered stays the Samaaj
Admin's until somebody gives it one with Edit details (below).
Social issues are filtered by the service itself: the approval queue simply
returns only the Area Admin's locality's issues.

**Every module list has a Locality filter (2026-09-27)** - Volunteer Groups,
Events, Social Issues, Trusts, Jain Pathshala, Auctions / Boli, Competitions,
and Families & Children. `core/locality-filter.component.ts` is the one
control: All localities, the whole Samaaj only (rows with no locality), or one
locality, from `LocalityOptions`. On the first seven it narrows the rows the
screen already loaded - each list is small, loaded whole, and names its
locality on every row, so filtering in the page costs no request and cannot
disagree with what it filters. Families & Children is the exception: it is
paged, and a household has no locality of its own - it is found by where its
members live - so member-family-service does the filtering (`GET
/v1/families?localityId=`) and "the whole Samaaj only" is not offered. An Area
Admin's filter there is fixed to their locality, the Members screen's rule,
because the service refuses them any other. "Families & Children" in the nav
used to open the conversion queue, the only part of that section with a
backend; it opens the household list now, which links to the queue.

**Trusts shipped with no admin screen at all until now**, the same shape of gap
as every other case in this file: `trust-service` (roster CRUD, then
elections) landed across two backend-only cycles, reachable only by curl.
`/trust` and `/trust/:id` are shaped after Volunteer Groups' own two screens
rather than Panch's, because a Trust's roster is admin-added directly - there
is no join-request queue to review, only "Add a trustee" (a directory search,
the same one Invite Admin's "existing member" mode uses, since a Samaaj's
whole membership is not already on screen the way a small group's roster is)
and "Remove", with no confirmation on either - reversible, since re-adding a
removed member reactivates their old row rather than refusing them.

**A Trust's money is three more tabs on its page (2026-09-27)**, each its own
component loading when opened, the roster and elections staying the first tab
so nothing about them moved. Donations are recorded as pledges and marked
collected, which is when trust-service issues the receipt number - the
screen shows pledged and collected totals separately, because what was
promised and what arrived are different questions. Expenses are recorded and
then approved, with both names shown. Collections has no create form at all:
an amount falls due only when a Boli in the Trust's locality has its result
announced, owed by the winner, and the tab records how it was paid. Names
(donors, approvers, payers) come from the page's one directory call.

**A teacher sees Jain Pathshala and nothing else (2026-09-27)**, and lands on
it after signing in - a member who teaches as much as a teacher from outside
the Samaaj. The Pathshala list asks for `?staffed=true` and the Pathshala and
class pages ask `?asStaff=true` whenever the caller lacks `Pathshala.Manage`,
so a Pathshala they do not teach at reads as "no such Pathshala". The nav is a
courtesy; pathshala-service enforces it, and forces the staff view for a
teacher who is not a member. "Create a login" on the Teachers tab makes exactly
that kind of account, and says so.

**Trusts, competitions and Boli occasions are created and edited in a popup
(2026-09-27)**, the dialog Events and Pathshalas use, each its own component
beside its list. Each service gained the update command it lacked: a Trust is
renamed (its locality stays - it is what a published Boli is matched on), a
competition is corrected until registration closes, an occasion until it
closes, and Edit is drawn only while the service would take it. Their
locality selects and Locality columns read `core/locality-options.ts`, which
loads every locality once per session as "Name, City" - the same list the
locality filters use - so no row prints a raw id.

**Pledges are chased from Collections too.** A donor who is a member is picked
from the directory (the same search as Add a trustee), so the pledge is owed
by their account; one with no account is typed by name, and Collections shows
that name. Paying a pledge there collects the donation, and collecting it on
Donations settles it there. "Send a reminder" sends the member an in-app
notification; the button is not drawn for a donor with no account and is
disabled for a day after the last one, mirroring the service's rule so it is
not a control that always answers 409.

**A Trust's page is six tabs since 2026-09-27, each its own component loading
when it is opened**: Members, Roles, Elections, Donations, Collections,
Expenses - the order the request named them in, with Roles beside Members
because each needs the other. The page itself loads the Trust and the
directory's names once and hands the names to every tab. Positions stopped
being free text the same day: a Trust has roles, Trustee built in and the rest
its own with seats, the shape Volunteer Groups' "Volunteer roles" card already
had and copied from it. Adding a trustee is a popup (search the directory, pick
a role with a free seat); each Members row has a role select offering only
roles with a free seat plus the one held, and a refused move re-reads the
roster so the select never shows a change the service refused. The Elections
tab's form picks a role - never Trustee, and never one already being elected -
and its publish confirmation says the winners are seated and the losing
holders go back to Trustee.

**There is no `iCanLead`/president-equivalent split here, and that is a real
difference from Volunteer Groups, not an oversight.** A Trust's positions are
plural - President, Secretary, Treasurer - so there is no single instance-level
leader to gate the roster or the election commands on; every admin-side action
stays behind the one `Trust.Manage` permission, and this screen renders every
control unconditionally rather than computing anything like `iCanLead` itself.
The election flow is otherwise volunteer-groups' `group-elections` screen
copied almost line for line - one button per forward stage, a confirmed
publish - deliberately not shared code, per this repo's own "copy the
template, don't couple the services" rule.
