# API Contracts

Concise endpoint reference per service — enough to start wiring the
frontend and writing integration tests. Full request/response schemas
belong in each service's own OpenAPI/Swagger output (generated from the
Minimal API definitions), not duplicated here by hand.

All paths are relative to the gateway route prefix from `SERVICES.md`
(e.g. `identity-tenant-service` paths below are under `/v1/identity`).
All authenticated endpoints require a valid JWT; the **Roles** column is
enforced by `TenantAuthorizationBehavior`, not just UI hiding.

## identity-tenant-service — `/v1/identity`

| Method | Path | Roles | Purpose |
|---|---|---|---|
| POST | `/tenants` | SuperAdmin | Create a Samaaj tenant |
| PATCH | `/tenants/{id}/status` | SuperAdmin | Activate/deactivate/archive. Deactivating and archiving also require the caller's own `password` in the body, and answer **403** (`Auth.StepUpFailed`) without it |
| GET | `/tenants` | SuperAdmin | List all tenants |
| GET | `/tenants/{slug}` | Anonymous | Resolve slug → tenant (registration picker) |
| GET | `/tenants/by-id/{id}` | Anonymous | Resolve id → tenant (used by the gateway on every request) |
| GET | `/tenants/directory` | Anonymous | Active Samaaj a visitor can register into |
| PUT | `/tenants/{id}/grievance-contact` | SuperAdmin, SamaajAdmin | Name who members complain to (DPDP s.13) |
| POST | `/register` | Anonymous | Member registration into one Samaaj |
| POST | `/token/refresh` | Anonymous | Exchange a refresh token for a new access token and the next refresh token. Single-use: presenting one twice ends the whole session |
| POST | `/login` | Anonymous | Common login → returns tenant-scoped JWT |
| POST | `/otp/request` | Anonymous | Sends a one-time sign-in code. Answers the same way whether or not the identifier belongs to a real, active account |
| POST | `/otp/login` | Anonymous | Sign in with the code instead of a password. Same response shape as `/login`; a wrong code, an unknown account and an expired code are all the same **401** (`Auth.InvalidCredentials`), and count toward the same lockout a wrong password does |
| POST | `/password-reset/request` | Anonymous | Sends a password reset code. Same anti-enumeration answer as `/otp/request` |
| POST | `/password-reset/redeem` | Anonymous | Redeem the code and set a new password. **No token** — sign in normally next, the same choice `/activations/redeem` makes |
| GET | `/activations/pending` | SamaajAdmin | Accounts awaiting activation |
| POST | `/activations/{userId}/code` | SamaajAdmin | Mint a one-time activation code (returned once) |
| POST | `/activations/redeem` | Anonymous | Redeem a code and set a first password. The member portal's `/activate` screen; answers with who the account belongs to and **no token**, so the next step is an ordinary sign-in |
| GET | `/consent-notice` | Anonymous | The consent notice and its version (DPDP s.5) |
| POST | `/me/consents/{purpose}/withdraw` | Authenticated | Withdraw one consent (DPDP s.6(4)) |
| GET | `/me/data-export` | Authenticated | What this service holds about you (DPDP s.11) |
| POST | `/me/erase` | Authenticated | Erase this account and, by event, everything the platform holds (DPDP s.12). Requires the caller's `password`; a wrong one answers **403** (`Auth.StepUpFailed`) |
| GET | `/tenants` | `SuperAdmin` + `Tenant.Manage` | Every Samaaj, any status; `?status=` and `?search=` narrow it |
| GET | `/tenants/modules` | Anonymous | The closed list of module keys, with labels, for the toggles |
| PUT | `/tenants/{id}/modules` | `SuperAdmin` + `Tenant.Manage` | Replace the whole set of modules a Samaaj runs |
| GET | `/roles` | Authenticated | The role and permission matrix the backend enforces, as this Samaaj has it |
| PUT | `/roles/{roleId}/permissions/{permissionKey}` | `Roles.Manage` | Grant or revoke one permission on one role, body `{"granted":bool}`. One key per call, for the same reason `/admins/{userId}/roles/{role}` is. Two floors a Samaaj cannot cross: editing SuperAdmin at all is **403**, and revoking `Roles.Manage` from SamaajAdmin is **409** |
| GET | `/admins` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | This Samaaj's administrators and their roles |
| POST | `/admins` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | Invite an administrator; returns a one-time activation code |
| PUT | `/admins/{userId}/roles/{role}` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | Grant or revoke one role, body `{"granted":bool}` |
| PUT | `/admins/{userId}/status` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | Suspend or reinstate an account, body `{"suspended":bool,"password"?:string}`. `password` is the caller's own and required only to suspend — reinstating needs none, the same asymmetry `/tenants/{id}/status` draws. **409** on suspending yourself, or on either direction against an erased account |
| POST | `/logout` | Anonymous (the refresh token is the credential) | End this session; `everywhere: true` ends every session for the account |
| GET | `/me` | Authenticated | Current user + roles + tenant |
| POST | `/me/password` | Authenticated | Set a new password, body `{"currentPassword","newPassword"}`. A wrong current password answers **403** (`Auth.StepUpFailed`). Ends every other session for the account (`SessionEndReason.PasswordChanged`) |

> The original draft named these `/admin-users` and `PATCH /admin-users/{id}/roles`. They
> shipped as `POST /admins` and `PUT /admins/{userId}/roles/{role}` above:
> one role per call, because the screen is a set of checkboxes and a PATCH
> taking a whole role list makes two admins editing at once silently overwrite
> each other.

## timeline-service — `/v1/timeline`

> Module-gated on `community`. A Samaaj that has switched it off gets 404 on
> every path here, not 403 — a Samaaj that does not run a module should be
> indistinguishable from a platform that has no such feature.

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/posts` | `Timeline.Post` | The Samaaj's timeline, plus this member's own posts whatever their status |
| POST | `/posts` | `Timeline.Post` | Write a post. A member's goes to the queue; `asAnnouncement` needs `Timeline.Moderate` |
| GET | `/posts/moderation-queue` | `Timeline.Moderate` | Posts awaiting review, and approved posts members have reported. Each row carries `availableDecisions` — what the domain says is worth offering for that post — so a moderation screen renders buttons rather than deriving them from the status |
| GET | `/posts/{id}` | `Timeline.Post` | One post with its comments |
| POST | `/posts/{id}/moderate` | `Timeline.Moderate` | `{decision, reason}` — Approve, Reject, Hide or Restore. Reject and Hide need a reason |
| POST | `/posts/{id}/comments` | `Timeline.Post` | Comment on an approved post |
| PUT | `/posts/{id}/reaction` | `Timeline.Post` | Set, change or clear. Sending the same one removes it |
| POST | `/posts/{id}/report` | `Timeline.Post` | Flag for the moderators. Removes nothing by itself |

> The original draft named these `/feed` and `/moderation-queue` at the top
> level, and gated them by role. They shipped under `/posts` because both are
> views of the same collection, and gated by permission rather than role — see
> "Authorization" in `services/timeline-service/CLAUDE.md` for why.

## member-family-service — `/v1/members`, `/v1/families`

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/members` | Member, SamaajAdmin | Search/filter tenant directory |
| GET | `/members/{id}` | `Members.Read` | One member, through the same per-field privacy mapper the directory uses |
| PATCH | `/members/{id}` | Member, **self only** | Update your own profile. Replaces it whole: `privacy` and `isListedInDirectory` are both **required**, because defaulting either would silently reopen something a member had closed. Takes **no** photo field — see below |
| PATCH | `/members/{id}/details` | `SamaajAdmin` + `Members.Write`, never self | Correct somebody else's factual details. The same fields **minus** `privacy` and `isListedInDirectory`, which are the member's own. **409** on your own id: use the profile screen, which can set them |
| POST | `/members/{id}/photo` | Member (self), `Members.Write` | `multipart/form-data`, one file part. JPEG, PNG or WebP, 2 MB |
| GET | `/members/{id}/photo` | `Members.Read` | The bytes. `ETag` + `Cache-Control: private` |
| DELETE | `/members/{id}/photo` | Member (self), `Members.Write` | Idempotent |
| POST | `/children/{id}/photo` | The child's household | As above. **Not** opened by `Members.Write` |
| GET | `/children/{id}/photo` | The child's household | |
| DELETE | `/children/{id}/photo` | The child's household | Idempotent |
| POST | `/families` | Member | Create family, become head |
| POST | `/families/join-requests` | Member | Request to join via family code |
| DELETE | `/families/join-requests/mine` | Member | Take back a request nobody has decided. No id: a member has at most one. Idempotent, but **409** if the head accepted in the meantime — you are in that household now |
| DELETE | `/families/mine/membership` | Member | Leave the household you are in. Headship passes to the longest-standing member if you held it. Idempotent. **409** if you have only asked to join (withdraw instead), or if you are the last member and it has children |
| POST | `/families/join-requests/{id}/decide` | FamilyHead | Accept/reject |
| GET | `/children` | Member | Children in your own household |
| POST | `/children` | FamilyHead | Add child profile |
| DELETE | `/children/{id}/parental-consent` | The member who gave that consent | Withdraw it (DPDP s.6(4)). Removes the child's record: name, date of birth and photograph go, the row stays because other services hold the id. **404** to anybody else, including the household head, because the consent is not theirs. **409** on a converted child, whose data is held on their own consent now |
| GET | `/children/names?ids=…` | SamaajAdmin, SuperAdmin + `Members.Read` | Names for children the caller already holds the ids of — the Pathshala placement queue. **Names only**, never the child record: that carries a date of birth and the parental-consent record. Ids from another Samaaj are absent rather than refused, and at most 200 per call |
| GET | `/children/data-notice` | Member | What a parent is shown before adding a child (DPDP s.9) |
| GET | `/members/me/data-export` | Authenticated | Profile, family and children (DPDP s.11) |
| POST | `/children/{id}/conversion` | FamilyHead | Start adult-child conversion |
| GET | `/children/conversion-requests` | SamaajAdmin | Requests awaiting a decision |
| POST | `/children/conversion-requests/{id}/decide` | SamaajAdmin | Approve or reject (admin-approved, decided 2026-08-28) |

**`photoUrl` is still a string on every member and child response, and it still
goes straight into an `img src` — what changed is where it points.** It used to
be a URL a client supplied; it is now a path on this platform,
`/v1/members/{id}/photo` or `/v1/children/{id}/photo`, or null when there is no
photo. Neither portal's wire shape changed.

**A client cannot render it with a plain `<img src>`.** The path is authorized
per request like anything else, and a tag the browser fetches by itself carries
no `Authorization` header. Fetch it as a blob through the same HTTP client
everything else uses — `libs/shared`'s `AuthedImageDirective` is that, for both
apps. Serving photos unauthenticated behind unguessable URLs was the alternative
and `SECURITY-CHECKLIST.md` rules it out in as many words.

The upload is `multipart/form-data` with one file part. **The part's declared
content type is ignored**: the format is read from the bytes, and that derived
type is what the `GET` serves back. JPEG, PNG and WebP; SVG is refused because
these are served from the platform's own origin. 2 MB, answered as `413` when
the body exceeds it and `400` when the bytes are not an image. Responses carry a
strong `ETag`, so a directory page costs 304s on a second visit, and
`Cache-Control: private`, because a shared cache would hand an image to a caller
who never passed the check that produced it.

## volunteer-groups-service — `/v1/volunteer-groups`

> Module-gated on `community`, the same key as the timeline.

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/groups` | `Members.Read` | This Samaaj's groups, each with the asking member's standing |
| POST | `/groups` | `VolunteerGroups.Manage` | Create a group and name its president |
| GET | `/groups/{id}` | `Members.Read` | One group with its members |
| PATCH | `/groups/{id}/status` | `VolunteerGroups.Manage` | Activate or deactivate. A deactivated group keeps its members |
| POST | `/groups/{id}/applications` | `Members.Read` | Ask to join |
| GET | `/groups/{id}/applications` | `VolunteerGroups.Lead` + this group's president | The president's review queue |
| POST | `/groups/{id}/applications/{applicationId}/decide` | `VolunteerGroups.Lead` + president | `{accept, rolePosition}` |
| PUT | `/groups/{id}/members/{memberId}/position` | `VolunteerGroups.Lead` + president | Give a position, or clear it |
| DELETE | `/groups/{id}/members/{memberId}` | `VolunteerGroups.Lead` + president | Remove a member. **409** on the president — see below |
| PATCH | `/groups/{id}/president` | `VolunteerGroups.Manage` | Hand the group to a different member, who joins it if they were not in it. The outgoing president stays on as an ordinary member |

> The draft gated the president's operations on the `VolunteerGroupPresident`
> role, which nothing grants. They ship on `VolunteerGroups.Lead`, which every
> member holds and which grants nothing until they are actually a group's
> president — see "Authorization" in
> `services/volunteer-groups-service/CLAUDE.md`.

## events-service — `/v1/events`

> Module-gated on `community`, the same key as the timeline and volunteer
> groups.

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/` | `Members.Read` | The Samaaj's events, each with the asking member's own standing. `?includeDrafts` needs `Events.Publish`; a member asking gets the published list |
| POST | `/` | `Events.Publish` | Write an event down. It stays a draft |
| GET | `/{id}` | `Members.Read` | One event. A draft answers 404 to a member |
| POST | `/{id}/publish` | `Events.Publish` | Tell the Samaaj about it |
| POST | `/{id}/cancel` | `Events.Publish` | `{reason}`, required — members who were going are told it |
| GET | `/{id}/attendees` | `Events.Publish` | Who is coming and who is waiting |
| POST | `/{id}/registration` | `Members.Read` | RSVP, or join the waitlist when full. One call for both; the response says which and, for the waitlist, the position |
| DELETE | `/{id}/registration` | `Members.Read` | Give up a place or leave the queue. Promotes whoever waited longest |

## social-issues-service — `/v1/social-issues`

> Module-gated on `social-issues` — its own key, not `community`. Switching it
> off leaves the timeline and volunteer groups untouched.

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/` | `Members.Read` | Published issues, plus this member's own whatever their status. `?category=` filters |
| POST | `/` | `Members.Read` | Raise one. `submitNow=false` saves a draft only the author sees |
| GET | `/approval-queue` | `SocialIssues.Approve` | Submitted and under review, oldest first |
| GET | `/{id}` | `Members.Read` | One issue with its full status history |
| PUT | `/{id}` | `Members.Read` + author | Correct one that has not been decided |
| POST | `/{id}/status` | `Members.Read`, then per transition | `{status, reason}` — one endpoint for every move. Reject and ChangesRequested need a reason; the legal moves for this caller are on the issue as `availableTransitions` |

> The draft named `SubmitIssueCommand`, `DecideIssueCommand`,
> `PublishIssueCommand` and `CloseIssueCommand` as four separate operations.
> They ship as one `POST /{id}/status`, because the transition table decides
> legality and four handlers would be four copies of the same tenant, author and
> permission checks — see "The workflow" in
> `services/social-issues-service/CLAUDE.md`.

## celebrity-voting-service — `/v1/celebrity-voting`

| Method | Path | Permission | Purpose |
|---|---|---|---|
| GET | `/campaigns` | `Members.Read` | This Samaaj's campaigns, with the caller's own vote on each |
| POST | `/campaigns` | `CelebrityVoting.Configure` | Create a campaign (starts as a draft) |
| GET | `/campaigns/{id}` | `Members.Read` | One campaign with its ballot, and the tally if the caller may see it |
| POST | `/campaigns/{id}/status` | `CelebrityVoting.Configure` | Open nominations, open voting, or close |
| POST | `/campaigns/{id}/candidates` | `Members.Read` | Nominate a member |
| POST | `/campaigns/{id}/candidates/{candidateId}/decide` | `CelebrityVoting.Configure` | Put a nomination on the ballot, or remove it |
| POST | `/campaigns/{id}/votes` | `Members.Read` | Cast this member's one vote (idempotent per voter) |
| POST | `/campaigns/{id}/results` | `CelebrityVoting.Configure` | Compute the ranking and freeze it |
| GET | `/campaigns/{id}/results` | `Members.Read` | The published ranking, as frozen |

> **Shipped shape vs. the draft.** Four differences, each deliberate.
>
> `/close` and `/publish` are not two more verbs. Closing is a status move like
> the others, so it ships as `POST /{id}/status`; publishing is separate because
> it is the one transition that also computes and stores something.
>
> There is no `GET /campaigns/{id}/candidates`. The ballot is part of the
> campaign, and a separate call would let a client render candidates without the
> window, the status, or the visibility setting that decide what those candidates
> mean.
>
> Nominations are approved before they reach the ballot. Anyone may put anyone
> forward, and a Samaaj should not be made to hold a public vote about a person
> because one member typed their name.
>
> The draft said "Top 10"; the campaign carries a `topN` instead. Ten is a
> reasonable default, not a property of every Samaaj.

## pathshala-service — `/v1/pathshala`

| Method | Path | Permission | Purpose |
|---|---|---|---|
| GET | `/pathshalas` | `Members.Read` | This Samaaj's Pathshalas, with class and teacher counts |
| POST | `/pathshalas` | SuperAdmin **role** + `Pathshala.Manage` | Create the master record |
| GET | `/pathshalas/{id}` | `Members.Read` | Sessions and classes |
| POST | `/pathshalas/{id}/sessions` | `Pathshala.Manage` | Open an academic session; it becomes current |
| DELETE | `/pathshalas/{id}` | `Pathshala.Manage` | Stop the Pathshala operating |
| POST | `/pathshalas/{id}/classes` | `Pathshala.Manage` | Add a class to a session |
| POST | `/classes/{id}/schedule` | `Pathshala.Manage` | Add a weekly slot; overlaps refused |
| POST | `/classes/{id}/teachers` | `Pathshala.Manage` | Assign or remove a teacher |
| GET | `/classes/{id}/roll` | `Members.Read` + teaches this class | Who is on the roll |
| POST | `/classes/{id}/attendance` | `Pathshala.Attendance.Write` + teaches this class | Mark the whole register for one date |
| GET | `/classes/{id}/register?date=` | `Members.Read` + teaches this class | The register as it stands for that date; empty if unmarked |
| POST | `/classes/{id}/exams` | `Pathshala.Exams.Write` + teaches this class | Set an exam |
| GET | `/classes/{id}/exams` | `Pathshala.Exams.Write` + teaches this class | This class's exams, each with its recorded marks |
| POST | `/exams/{id}/results` | `Pathshala.Exams.Write` + teaches this class | Record or correct one mark |
| POST | `/pathshalas/{id}/enrollments` | `Members.Read` | Ask for a place for a child |
| GET | `/pathshalas/{id}/enrollments/requests` | `Pathshala.Manage` | The placement queue |
| POST | `/enrollments/{id}/placement` | `Pathshala.Manage` | Place in a class, or decline |
| DELETE | `/enrollments/{id}` | `Pathshala.Manage` | Withdraw; records are kept |
| GET | `/enrollments` | `Members.Read` | This member's own enrolments |
| GET | `/enrollments/{id}/my-class` | `Members.Read` + owns this enrolment | My Class |
| GET | `/enrollments/{id}/attendance` | `Members.Read` + owns this enrolment | My Attendance |
| GET | `/enrollments/{id}/exams` | `Members.Read` + owns this enrolment | My Exams |
| GET | `/enrollments/{id}/progress` | `Members.Read` + owns this enrolment | My Progress |

> **Shipped shape vs. the draft.** Four differences.
>
> **Enrolment is two calls, not one.** A parent asks; somebody at the Pathshala
> places the child in a class. The endpoint was always at the Pathshala rather
> than at a class, so a placement step was implied — and it is also the only
> check this service can make that the child is the caller's, which is
> member-family-service's fact and not ours.
>
> **Nothing is gated on the `PathshalaStudent` role.** Nothing grants it —
> enrolment happens in pathshala-service, which cannot write role grants in
> identity-tenant-service. "Owns this enrolment" above means the parent who
> asked, the student once conversion gives them an account, a teacher of their
> class, or a Pathshala administrator, decided against the data.
>
> **A teacher permission is necessary and not sufficient.**
> `Pathshala.Attendance.Write` says somebody is a teacher; the per-class check
> beside it says whose register they may mark.
>
> **`SamaajAdmin` now holds `Pathshala.Manage`**, which nothing held before.
> Creating the master record stays Super-Admin-only through a role check on that
> one command.

## boli-service — `/v1/boli`

Permissions rather than roles below: `Boli.Manage` and
`Boli.PublishResults` are both held by `SamaajAdmin` and `BoliManager`
today, but the split is what the service gates on.

The paths really do read `/v1/boli/boli/{id}` — the service prefix plus
the resource under it, the same shape as `/v1/pathshala/pathshalas`.

| Method | Path | Permission | Purpose |
|---|---|---|---|
| GET | `/occasions` | `Members.Read` | Occasions, newest first |
| POST | `/occasions` | `Boli.Manage` | Create occasion |
| GET | `/occasions/{id}` | `Members.Read` | Types and the Boli under it |
| POST | `/occasions/{id}/boli-types` | `Boli.Manage` | Define Boli type. One name per occasion |
| POST | `/occasions/{id}/status` | `Boli.Manage` | Activate or close. Never backwards |
| POST | `/occasions/{id}/boli` | `Boli.Manage` | Open a Boli for bidding |
| GET | `/boli/active` | `Members.Read` | Every Boli taking bids right now |
| GET | `/boli/{id}` | `Members.Read` | One Boli, its highest and the minimum next bid |
| POST | `/boli/{id}/bids` | `Members.Read` | Place bid. Being outbid answers **200** with `accepted: false` |
| GET | `/boli/{id}/bids` | `Members.Read` | Bid history: amounts and times, never who bid |
| POST | `/boli/{id}/close` | `Boli.Manage` | Close bidding. Idempotent |
| POST | `/boli/{id}/result` | `Boli.Manage` | Record result from the highest bid. Winner is not a parameter |
| GET | `/boli/{id}/result` | `Members.Read` | 404 until recorded; names no winner until published |
| POST | `/boli/{id}/result/publish` | `Boli.PublishResults` | Publish. Idempotent, and irreversible here |
| GET | `/results` | `Members.Read` | Everything announced, newest first |
| GET | `/results/pending` | `Boli.PublishResults` | Recorded and not yet announced, oldest first. Carries the amount and `recordedBy`, never a winner |

Amounts are integers in paise. A Boli is money, and a floating-point
field accumulates error that shows up as a winning bid a rupee off what
somebody actually offered.

Opening a Boli takes `autoExtendSeconds` (0–3600, default 0 = off).
A bid arriving within that many seconds of the close moves the close to
that many seconds **after the bid** — not after the old closing time, so
every bid buys the room the same full window to answer and bidding late
gains nothing. `BoliResponse` carries the setting back, and `endAt` is
whatever it currently is; a client showing a countdown should re-read
after placing a bid, as the member portal does — and should tell bidders
the window exists, because a closing time the server quietly moves is a
client stating something that stops being true, and the rule only deters
sniping once the people bidding know about it.

## audit-notification-service — `/v1/audit`, `/v1/notifications`

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/audit/logs` | SuperAdmin, SamaajAdmin | Query audit log (filterable) |
| GET | `/audit/me/data-export` | Authenticated | My notifications and my actions (DPDP s.11) |
| GET | `/notifications` | Authenticated | My notifications. In-app only: a message the platform also emailed is the same message, and returning both would show it twice |
| POST | `/notifications/{id}/read` | Authenticated | Mark read, for the caller only. Refuses 404 for another member's notification or another Samaaj's, and 409 for one sent by email or text - whether those were opened is not something this platform knows |
| POST | `/notifications/read-all` | Authenticated | Mark everything in the caller's list read, in one request. Leaves the timestamps on ones already read |
| POST | `/notifications/broadcast` | SamaajAdmin, SuperAdmin + `Notifications.Broadcast` | Announce to every member of **this** Samaaj. In-app only, and never platform-wide - see below |
| GET | `/notifications/broadcasts` | SamaajAdmin, SuperAdmin + `Notifications.Broadcast` | This Samaaj's announcements, with how many members opened each |

**A broadcast reaches one Samaaj, in the app.** The wireframe's Audience
dropdown also offers "All Members" across every Samaaj and "Specific Role";
neither is built. The first is a write that deliberately crosses tenants, which
nothing else on this platform does and which should not arrive as a side effect
of a dropdown. The second needs to know who holds which role, which lives in
identity-tenant-service.

Its Channel dropdown offers "In-App + Email" and "In-App + SMS/WhatsApp", and
those are not built either: audit-notification-service learns a member's contact
address from an event that carries one and keeps no directory, so there is no
set of addresses to send a Samaaj-wide message to. That is the same missing
piece as the DPDP s.8(6) duty to reach every affected person - see
`DPDP-COMPLIANCE.md`.

**Read state is per member, so a broadcast is one row a Samaaj shares and a
thousand people each read separately.** `readAt` on a notification is always
*the caller's*, never anyone else's. The unique index on
`(notification_id, user_id)` is what makes opening the same notification in two
tabs a no-op rather than an error.
