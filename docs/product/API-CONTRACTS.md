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
| GET | `/tenants/{id}/security-policy` | SuperAdmin, SamaajAdmin (own Samaaj) | A Samaaj's own overrides to the platform's password/OTP/lockout/session constants, raw and nullable |
| PUT | `/tenants/{id}/security-policy` | SuperAdmin, SamaajAdmin (own Samaaj) | Replace those overrides, whole. A null field clears that override back to the platform default |
| POST | `/register` | Anonymous | Member registration into one Samaaj |
| POST | `/token/refresh` | Anonymous | Exchange a refresh token for a new access token and the next refresh token. Single-use: presenting one twice ends the whole session |
| POST | `/captcha` | Anonymous | A sign-in CAPTCHA: `{ captchaId, imagePng (base64), expiresAt }`. Single use, five minutes, drawn by this service |
| POST | `/login` | Anonymous | Step one: `{ mobileOrEmail, password, captchaId, captchaAnswer, deviceToken? }`. A missing or wrong CAPTCHA is **401** `Auth.CaptchaFailed`, checked before any account is read. On a trusted device answers `{ codeRequired: false, session }` (the tenant-scoped JWT and refresh token); anywhere else sends a 6-digit code and answers `{ codeRequired: true, challengeToken, codeSentTo (masked), challengeExpiresAt }` |
| POST | `/login/verify` | Anonymous | Step two: `{ challengeToken, code, trustDevice }` → `{ session, deviceToken?, deviceTrustedUntil? }`. A wrong code, an unknown or expired challenge are one **401** `Auth.InvalidCode`, and count toward the same lockout a wrong password does. `trustDevice` skips step two for 30 days on this browser |
| POST | `/login/resend` | Anonymous | `{ challengeToken }` - a new code, at most every 30 seconds; never extends the challenge. Answers the same either way |
| POST | `/password-reset/request` | Anonymous | Sends a password reset code. Answers the same whether or not the account exists |
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
| POST | `/admins` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | Invite an administrator; returns a one-time activation code. `localityId` is required when `roles` is exactly `["AreaAdmin"]` and refused otherwise; an `AreaAdmin` invitation may name no other role |
| PUT | `/admins/{userId}/roles/{role}` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | Grant or revoke one role, body `{"granted":bool,"localityId"?:string}`. `localityId` is required when granting `AreaAdmin` and refused otherwise |
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
| GET | `/members` | Member, SamaajAdmin | Search/filter tenant directory. `term` matches full name; `localityId` is an exact match against the searched member's permanent Locality (a master-data-service id, not a substring — see below). `gautraId` and `professionId` filter exactly too; `professionId` matches only professions the caller could already read on the row (never another member's Private one). `?skip=&take=` paged as `{ items, hasMore }`. Each row carries `fatherName`/`motherName` from the member's household, null where it records none the caller may see. An Area Admin gets the administrator's view (unlisted members, private fields) only when `localityId` is their token's own `locality_id`, and on `/members/{id}` only for a member living there; otherwise the member view |
| GET | `/members/registration-summary` | `SamaajAdmin`/`SuperAdmin` + `Members.Read` | Registrations in the current 30-day window against the 30 days before it, for the admin Reports & Analytics screen |
| GET | `/members/{id}` | `Members.Read` | One member, through the same per-field privacy mapper the directory uses, with `fatherName`/`motherName` by the directory's rules |
| GET | `/members/{id}/household` | `Members.Read` | `{ household }` - the household they actively belong to: `namedAfter` (head, or admin) and `members` (`memberId`, `fullName`, `photoUrl`, `relationshipToAdmin`, `isHead`, `isAdmin`) - only people the caller could find in the directory, never a child's record or the family code. `household` is null when they are in none |
| PATCH | `/members/{id}` | Member, **self only** | Update your own profile. Replaces it whole: `privacy` and `isListedInDirectory` are both **required**, because defaulting either would silently reopen something a member had closed. Also carries `gautraId`, `professionId`, `qualificationId` (all master-data-service ids), `permanentAddress` (`{line, cityId, localityId}`, required), `temporaryAddress` (same shape, nullable) and `maritalStatus` (`Unmarried`, `Married`, `Divorced` or null - optional, and returned only on `/members/me`). Takes **no** photo field — see below |
| PATCH | `/members/{id}/details` | `SamaajAdmin` or `AreaAdmin` (own locality only) + `Members.Write`, never self | Correct somebody else's factual details. The same fields **minus** `privacy` and `isListedInDirectory`, which are the member's own. **409** on your own id: use the profile screen, which can set them |
| POST | `/members/{id}/photo` | Member (self), `Members.Write` | `multipart/form-data`, one file part. JPEG, PNG or WebP, 2 MB |
| GET | `/members/{id}/photo` | `Members.Read` | The bytes. `ETag` + `Cache-Control: private` |
| DELETE | `/members/{id}/photo` | Member (self), `Members.Write` | Idempotent |
| POST | `/children/{id}/photo` | The child's household | As above. **Not** opened by `Members.Write` |
| GET | `/children/{id}/photo` | The child's household | |
| DELETE | `/children/{id}/photo` | The child's household | Idempotent |
| POST | `/families` | Member | Create family, become its **family admin** |
| GET | `/families` | SamaajAdmin / SuperAdmin / AreaAdmin + `Members.Write` | Households with their active members and minor children (an age, not a date of birth), newest first, `?skip=&take=` paged as `{ items, hasMore }`. `?localityId=` keeps households with an active member whose permanent address is there. **403** to an Area Admin naming any locality but their own, or none |
| PUT | `/families/mine/head` | Family admin | Name the household's **family head** (its eldest member, a label that grants nothing) from its active members, or clear it with an empty id. **403** to anybody but the family admin, **409** if the person is not an active member |
| POST | `/families/join-requests` | Member | Request to join via family code |
| DELETE | `/families/join-requests/mine` | Member | Take back a request nobody has decided. No id: a member has at most one. Idempotent, but **409** if the family admin accepted in the meantime — you are in that household now |
| DELETE | `/families/mine/membership` | Member | Leave the household you are in. The admin role passes to the longest-standing member if you held it; a family head who leaves is cleared. Idempotent. **409** if you have only asked to join (withdraw instead), or if you are the last member and it has children |
| POST | `/families/join-requests/{id}/decide` | Family admin | Accept/reject |
| GET | `/children` | Member | Children in your own household |
| POST | `/children` | Family admin | Add child profile |
| DELETE | `/children/{id}/parental-consent` | The member who gave that consent | Withdraw it (DPDP s.6(4)). Removes the child's record: name, date of birth and photograph go, the row stays because other services hold the id. **404** to anybody else, including the family admin, because the consent is not theirs. **409** on a converted child, whose data is held on their own consent now |
| GET | `/children/names?ids=…` | SamaajAdmin, SuperAdmin, AreaAdmin, PathshalaTeacher + `Members.Read` | Names for children the caller already holds the ids of — the Pathshala placement queue. **Names only**, never the child record: that carries a date of birth and the parental-consent record. Ids from another Samaaj are absent rather than refused, and at most 200 per call |
| GET | `/children/data-notice` | Member | What a parent is shown before adding a child (DPDP s.9) |
| GET | `/members/me/data-export` | Authenticated | Profile, family and children (DPDP s.11) |
| POST | `/children/{id}/conversion` | Family admin | Start adult-child conversion |
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
| GET | `/members/{memberId}/groups` | `Members.Read` | The groups one member belongs to: `groupId`, `name`, `photoUrl`, `roleTitle`, `isPresident`, `joinedAt`. Never a group they only applied to |
| POST | `/groups` | `VolunteerGroups.Manage` | Create a group. `presidentMemberId` is **optional** (a string, empty or omitted for none): a group may exist before anyone leads it. `localityId` optional; an Area Admin's group is always in their own locality (omitted: filled in; other: **403**) |
| GET | `/groups/{id}` | `Members.Read` | One group with its members (each with `roleId` and the role's title as `rolePosition`) and its `roles` (title, kind, seats, holder count). `iCanRunElections` says whether the caller may manage roles and elections |
| PATCH | `/groups/{id}` | `VolunteerGroups.Manage` | Correct the name, description, focus area or locality. Not the president, membership or status — each its own endpoint. An Area Admin: own locality's groups only (**404** otherwise), and cannot move one out (**403**) |
| PATCH | `/groups/{id}/status` | `VolunteerGroups.Manage` | Activate or deactivate. A deactivated group keeps its members |
| POST | `/groups/{id}/photo` | `VolunteerGroups.Manage` (an Area Admin: own locality, **404** otherwise) | The group's banner photo. `multipart/form-data`, one file part. JPEG, PNG or WebP, 2 MB; replaces any photo. `photoUrl` on the group then points here |
| GET | `/groups/{id}/photo` | `Members.Read` | The bytes. `ETag` + `Cache-Control: private` |
| DELETE | `/groups/{id}/photo` | `VolunteerGroups.Manage` | Idempotent |
| POST | `/groups/{id}/applications` | `Members.Read` | Ask to join |
| GET | `/groups/{id}/applications` | `VolunteerGroups.Lead` + this group's president, or a Samaaj admin while it has none | The review queue |
| POST | `/groups/{id}/applications/{applicationId}/decide` | `VolunteerGroups.Lead` + president, or a Samaaj admin while it has none | `{accept}`. An accepted member starts as a Volunteer |
| POST | `/groups/{id}/roles` | `VolunteerGroups.Lead` + president or an administrator of the group | Define a role `{title, seats}` (1-100). Titles unique per group, whatever their case |
| PATCH | `/groups/{id}/roles/{roleId}` | `VolunteerGroups.Lead` + president or administrator | Rename or resize. **409** for Volunteer/President, or seats below its holders |
| DELETE | `/groups/{id}/roles/{roleId}` | `VolunteerGroups.Lead` + president or administrator | **409** while anybody holds it or an election is deciding it |
| PUT | `/groups/{id}/members/{memberId}/role` | `VolunteerGroups.Lead` + president or administrator | `{roleId}`. **409** when the role is full, or for the President role either way — that moves only with the presidency |
| DELETE | `/groups/{id}/members/{memberId}` | `VolunteerGroups.Lead` + president | Remove a member. **409** on the president — see below |
| PATCH | `/groups/{id}/president` | `VolunteerGroups.Manage` | Hand the group to a different member, who joins it if they were not in it. The outgoing president stays on as an ordinary member |
| GET | `/groups/{id}/elections` | `Members.Read` | This group's elections, newest first, each with the caller's own vote |
| POST | `/groups/{id}/elections` | `VolunteerGroups.Lead` + president or an administrator of the group, at any time | `{roleId, …windows}`. Set an election up for one of the group's roles, filling its seats. Starts `Draft`. **409** for Volunteer, or a role with an election already running |
| GET | `/elections/{id}` | `Members.Read` | One election with its ballot and tally — never hidden, unlike Celebrities of Samaaj. `seats`, `myVoteCandidateIds`, `winnerMemberIds` |
| PATCH | `/elections/{id}` | `VolunteerGroups.Lead` + president or administrator | Correct the role or windows — how nominations that closed too early are reopened. `Draft`/`NominationsOpen` only |
| POST | `/elections/{id}/status` | `VolunteerGroups.Lead` + president or administrator | `NominationsOpen`, `VotingOpen` or `Closed`. Strictly forward; 409 moving to `VotingOpen` on an empty ballot |
| POST | `/elections/{id}/candidates` | `VolunteerGroups.Lead` + a member of the group | Nominate a fellow group member. A repeat nomination is a no-op reported as success. **409** nominating the president for any role but President |
| POST | `/elections/{id}/candidates/{candidateId}/decide` | `VolunteerGroups.Lead` + president or administrator | Approve onto the ballot, or remove before voting opens |
| POST | `/elections/{id}/votes` | `VolunteerGroups.Lead` + a member of the group | `{candidateIds}`: one final ballot of up to `seats` picks, all counted the same (**400** above that). Self-votes are refused; a second ballot is reported as `accepted: false` |
| POST | `/elections/{id}/publish` | `VolunteerGroups.Lead` + president or administrator | Only from `Closed`. Installs the top `seats` candidates in the role; anyone who held it and did not win goes back to Volunteer |

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
| POST | `/` | `Events.Publish` | Write an event down. It stays a draft. `localityId` optional; an Area Admin's event is always in their own locality (omitted: filled in; other: **403**), and every `Events.Publish` route below answers them **404** for an event elsewhere, whose drafts they also cannot see |
| GET | `/{id}` | `Members.Read` | One event. A draft answers 404 to a member |
| POST | `/{id}/publish` | `Events.Publish` | Tell the Samaaj about it |
| POST | `/{id}/cancel` | `Events.Publish` | `{reason}`, required — members who were going are told it |
| GET | `/{id}/attendees` | `Events.Publish` | Who is coming and who is waiting |
| POST | `/{id}/schedule` | `Events.Publish` (an Area Admin: own locality, **404** otherwise) | Add a programme `{date, programName, startTime, endTime?}` - `date` "yyyy-MM-dd" on one of the event's own days (**400** otherwise, with a day's slack for time zones), times "HH:mm:ss", end after start. **409** on a cancelled event. Answers the whole event |
| PUT | `/{id}/schedule/{itemId}` | `Events.Publish` | Correct one programme. Answers the whole event |
| DELETE | `/{id}/schedule/{itemId}` | `Events.Publish` | Take one off. **404** when it is not there |
| POST | `/{id}/photo` | `Events.Publish` (own locality) | The event's cover photo, multipart, JPEG/PNG/WebP up to 2 MB. **409** on a cancelled event. Answers the whole event, with `photoUrl` |
| GET | `/{id}/photo` | `Members.Read` | The bytes, `Cache-Control: private`. A draft's photo answers 404 to anyone who cannot see the draft |
| DELETE | `/{id}/photo` | `Events.Publish` | Idempotent. Answers the whole event |
| POST | `/{id}/registration` | `Members.Read` | RSVP, or join the waitlist when full. One call for both; the response says which and, for the waitlist, the position |
| DELETE | `/{id}/registration` | `Members.Read` | Give up a place or leave the queue. Promotes whoever waited longest |

## social-issues-service — `/v1/social-issues`

> Module-gated on `social-issues` — its own key, not `community`. Switching it
> off leaves the timeline and volunteer groups untouched.

Reviewed by the Samaaj Panch (panch-service) once a Samaaj has elected one;
until then `SocialIssues.Approve` holders review as a fallback. See "Who
reviews" in `services/social-issues-service/CLAUDE.md`.

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/` | `Members.Read` | Issues the Samaaj can see (in front of the Panch, or decided), plus this member's own whatever their status. `?category=` filters |
| POST | `/` | `Members.Read` | Raise one. `submitNow=false` saves a draft only the author sees. `localityId` optional, the master-data locality it concerns (also on `PUT /{id}`) |
| GET | `/approval-queue` | `Members.Read`, then Panch member (or `SocialIssues.Approve`, read-only once a Panch exists) | Submitted, under review, or in front of the Panch, oldest first. An Area Admin's `SocialIssues.Approve` covers only their own locality's issues, here and on every move; a Panch seat covers all |
| GET | `/review-access` | `Members.Read` | `{canReview, canSeeQueue, panelSeated, isPanelMember}` — so a portal never asks for a queue it may not have |
| GET | `/{id}` | `Members.Read` | One issue with its full status history |
| PUT | `/{id}` | `Members.Read` + author | Correct one the Panch does not have yet |
| POST | `/{id}/status` | `Members.Read`, then per transition | `{status, reason}` — one endpoint for every move. Reject and ChangesRequested need a reason; the legal moves for this caller are on the issue as `availableTransitions` |
| POST | `/{id}/decision` | `Members.Read`, then Panch member | `{decision, monitoringGroupId}` — records the Panch's decision and closes the issue; `monitoringGroupId` is optional |

> The draft named `SubmitIssueCommand`, `DecideIssueCommand`,
> `PublishIssueCommand` and `CloseIssueCommand` as four separate operations.
> They ship as `POST /{id}/status` plus `POST /{id}/decision`, because the
> transition table decides legality and separate handlers for every move would
> be copies of the same tenant, author and permission checks — see "The
> workflow" in `services/social-issues-service/CLAUDE.md`.

## celebrity-voting-service — `/v1/celebrity-voting`

| Method | Path | Permission | Purpose |
|---|---|---|---|
| GET | `/campaigns` | `Members.Read` | This Samaaj's campaigns, with the caller's own vote on each |
| POST | `/campaigns` | `CelebrityVoting.Configure` | Create a campaign (starts as a draft) |
| PATCH | `/campaigns/{id}` | `CelebrityVoting.Configure` | Correct a campaign's details (same rules as create). **409** from `VotingOpen` on |
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
| GET | `/pathshalas` | `Members.Read` or `Pathshala.Teach` | This Samaaj's Pathshalas, with class and teacher counts. `?staffed=true` narrows to the ones the caller works at; always so for a caller without `Members.Read` - a teacher from outside the Samaaj |
| GET | `/attendance-summary` | `Pathshala.Manage` | Present/absent/excused across every class this Samaaj runs, for the admin Reports & Analytics screen. **403** to an Area Admin; every other `Pathshala.Manage` route below answers them **404** for a Pathshala outside their locality or with no address |
| POST | `/pathshalas` | SuperAdmin **role** + `Pathshala.Manage` | Create the master record |
| GET | `/pathshalas/{id}` | `Members.Read` or `Pathshala.Teach` | Sessions and classes. `?asStaff=true` is 404 unless the caller works here; always so for a caller without `Members.Read` |
| PATCH | `/pathshalas/{id}` | `Pathshala.Manage` | Correct the name, address and contact person, replacing all three. An Area Admin's blank locality is filled with theirs; naming another is **403** |
| POST | `/pathshalas/{id}/sessions` | `Pathshala.Manage` | Open an academic session; it becomes current |
| PATCH | `/pathshalas/{id}/sessions/{sessionId}` | `Pathshala.Manage` | `{label, startDate, endDate}` - corrects a session in place. Which session is current does not change. 409 on a label another session has |
| DELETE | `/pathshalas/{id}` | `Pathshala.Manage` | Stop the Pathshala operating |
| POST | `/pathshalas/{id}/classes` | `Pathshala.Manage` | Add a class to a session |
| POST | `/classes/{id}/schedule` | `Pathshala.Manage` | Add a weekly slot; overlaps refused |
| POST | `/classes/{id}/teachers` | `Pathshala.Manage` | `{teacherId, assign}` - assign or remove one of the Pathshala's teacher records |
| GET | `/pathshalas/{id}/teachers` | `Members.Read` or `Pathshala.Teach` + staff of it | Teachers with contact details. A teacher need not be a member; `userId` is their login, if any |
| POST | `/pathshalas/{id}/teachers` | `Pathshala.Manage` | `{fullName, phone, email, qualification, userId}`. **409** if that login is already a teacher here |
| PATCH | `/pathshalas/{id}/teachers/{teacherId}` | `Pathshala.Manage` | Correct details or the linked login |
| DELETE | `/pathshalas/{id}/teachers/{teacherId}` | `Pathshala.Manage` | Remove; takes them off every class |
| GET | `/pathshalas/{id}/students` | `Members.Read` or `Pathshala.Teach` + staff | Placed children (a teacher: their classes only), and waiting requests (administrators only), with the contact name and phone the parent gave |
| GET | `/pathshalas/{id}/progress` | `Members.Read` or `Pathshala.Teach` + staff | Every placed child's attendance and exam average, as percentages |
| GET | `/pathshalas/{id}/attendance` | `Members.Read` or `Pathshala.Teach` + staff | Per class: days marked and the tally |
| GET | `/pathshalas/{id}/exams` | `Members.Read` or `Pathshala.Teach` + staff | Every exam, with results recorded and the average |
| GET | `/pathshalas/{id}/donations` | `Pathshala.Manage` | The ledger, newest first, with `totalAmount` (received) and `outstandingAmount` (announced, not yet paid); deleted entries are in neither |
| POST | `/pathshalas/{id}/donations` | `Pathshala.Manage` | `{donorName, donorMemberId, amount (paise), donatedOn, mode, purpose, paid}` - paid (the default) issues the next receipt number; `paid: false` records an announcement with no mode and no receipt |
| PATCH | `/donations/{id}` | `Pathshala.Manage` | `{donorName, amount, donatedOn, mode, paidOn, purpose}` - corrects an entry; a paid one needs `mode` and `paidOn`, a pledge ignores both. 409 once deleted |
| POST | `/donations/{id}/paid` | `Pathshala.Manage` | `{mode, paidOn}` - an announcement was paid; issues the next receipt number. 409 once paid or deleted |
| POST | `/donations/{id}/void` | `Pathshala.Manage` | `{reason}` (required) - the admin panel's Delete for a paid entry: off the ledger and its totals, the row kept so a receipt number is never reissued. 409 on an announcement |
| DELETE | `/donations/{id}` | `Pathshala.Manage` | The admin panel's Delete for an announcement: erases it, answering its id. 409 once it has a receipt |
| GET | `/pathshalas/{id}/announcements` | `Members.Read` or `Pathshala.Teach` + staff | What it has sent |
| POST | `/pathshalas/{id}/announcements` | `Members.Read` or `Pathshala.Teach` + a teacher of it or somebody who runs it | `{title, body}` to the parents of every child with a place or a request; each gets an in-app notification. **409** with nobody to send to |
| GET | `/announcements` | `Members.Read` | Announcements from every Pathshala where the caller has a child with a place or a request |
| GET | `/classes/{id}/roll` | `Members.Read` + teaches this class | Who is on the roll |
| POST | `/classes/{id}/attendance` | `Pathshala.Attendance.Write` + teaches this class | Mark the whole register for one date |
| GET | `/classes/{id}/register?date=` | `Members.Read` + teaches this class | The register as it stands for that date; empty if unmarked |
| POST | `/classes/{id}/exams` | `Pathshala.Exams.Write` + teaches this class | Set an exam |
| GET | `/classes/{id}/exams` | `Pathshala.Exams.Write` + teaches this class | This class's exams, each with its recorded marks |
| POST | `/exams/{id}/results` | `Pathshala.Exams.Write` + teaches this class | Record or correct one mark |
| POST | `/pathshalas/{id}/enrollments` | `Members.Read` | `{childProfileId, contactName, contactPhone}` - ask for a place, giving the Pathshala a way to reach the parent |
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
| GET | `/occasions` | `Members.Read` | Occasions, newest first. `?localityId=` and `?cityId=` narrow to one place |
| POST | `/occasions` | `Boli.Manage` | Create occasion. Optional `localityId` - null runs it Samaaj-wide - and optional `cityId` and `venue` (up to 300) |
| PATCH | `/occasions/{id}` | `Boli.Manage` | Correct title, description, date, locality, city and venue. 409 once the occasion is closed |
| GET | `/occasions/{id}` | `Members.Read` | Types and the Boli under it |
| POST | `/occasions/{id}/boli-types` | `Boli.Manage` | Define Boli type. One name per occasion |
| POST | `/occasions/{id}/status` | `Boli.Manage` | Activate or close. Never backwards |
| POST | `/occasions/{id}/boli` | `Boli.Manage` | Open a Boli for bidding |
| GET | `/boli/active` | `Members.Read` | Every Boli taking bids right now, each with its occasion's title and place. `?localityId=` and `?cityId=` narrow to one place's occasions |
| GET | `/boli/{id}` | `Members.Read` | One Boli, its highest and the minimum next bid |
| POST | `/boli/{id}/bids` | `Members.Read` | Place bid. Being outbid answers **200** with `accepted: false` |
| GET | `/boli/{id}/bids` | `Members.Read` | Bid history: amounts and times, never who bid |
| GET | `/boli/{id}/bids/ledger` | `Boli.Manage` | Every bid with the `memberId` who placed it and when, highest first. The organiser's record; members never see it |
| POST | `/boli/{id}/close` | `Boli.Manage` | Close bidding. Idempotent |
| POST | `/boli/{id}/result` | `Boli.Manage` | Record result from the highest bid. Winner is not a parameter |
| GET | `/boli/{id}/result` | `Members.Read` | 404 until recorded; names no winner until published |
| POST | `/boli/{id}/result/publish` | `Boli.PublishResults` | Publish. Idempotent, and irreversible here |
| GET | `/results` | `Members.Read` | Everything announced, newest first, each with `winningMemberId`, its `occasionId` and the occasion's `localityId`/`cityId` |
| GET | `/results/pending` | `Boli.PublishResults` | Recorded and not yet announced, oldest first. Carries the amount and `recordedBy`, never a winner |

Amounts are integers in paise. A Boli is money, and a floating-point
field accumulates error that shows up as a winning bid a rupee off what
somebody actually offered.

`localityId` is a loose reference into master-data-service's `Locality`,
with no referential integrity across the service boundary, and no
authorization effect in this phase - `Boli.Manage`/`Boli.PublishResults`
stay Samaaj-wide permissions regardless of which locality an occasion names.
Null (the default) is a permanent, legitimate state, not a migration
transient - a Samaaj too small to split its Boli by locality never needs to
set it.

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

## master-data-service — `/v1/master-data`

Every read below is anonymous — a registration form's dropdowns need these
before the person filling them in has an account. See root `CLAUDE.md` §6 and
`services/master-data-service/CLAUDE.md`.

| Method | Path | Permission | Purpose |
|---|---|---|---|
| GET | `/lookup-items?category=` | anonymous | One category's list (`Country`, `ProfessionType`, `Gender`, `MaritalStatus`, `Qualification`), active first by sort order |
| GET | `/lookup-items/{id}` | anonymous | One lookup item |
| POST | `/lookup-items` | `MasterData.Manage` | Add a row to one of the four flat lists. Refuses a duplicate `(category, code)` |
| PUT | `/lookup-items/{id}` | `MasterData.Manage` | Rename/reorder. Code and category are fixed once created |
| POST | `/lookup-items/{id}/deactivate` | `MasterData.Manage` | Soft delete |
| GET | `/cities?countryId=` | anonymous | Cities, optionally narrowed to one country |
| GET | `/cities/{id}` | anonymous | One city |
| POST | `/cities` | `MasterData.Manage` | Add a city under a country |
| PUT | `/cities/{id}` | `MasterData.Manage` | Rename |
| POST | `/cities/{id}/deactivate` | `MasterData.Manage` | Soft delete |
| GET | `/localities?cityId=` | anonymous | Localities ("locations master"), optionally narrowed to one city |
| GET | `/localities/{id}` | anonymous | One locality |
| POST | `/localities` | `MasterData.Manage` | Add a locality under a city |
| PUT | `/localities/{id}` | `MasterData.Manage` | Rename |
| POST | `/localities/{id}/deactivate` | `MasterData.Manage` | Soft delete |
| GET | `/gautras` | anonymous | The calling Samaaj's own Gautra list, resolved from `X-Tenant-Id` |
| GET | `/gautras/{id}` | anonymous | One Gautra |
| POST | `/gautras` | `Gautra.Manage` | Add a Gautra to the caller's own Samaaj |
| PUT | `/gautras/{id}` | `Gautra.Manage` | Rename, within the caller's own Samaaj |
| POST | `/gautras/{id}/deactivate` | `Gautra.Manage` | Soft delete, within the caller's own Samaaj |

Every write is a soft delete — other services hold these rows' ids as plain
foreign keys across a service boundary, with no referential integrity to
enforce them.

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

## matrimony-service — `/v1/matrimony`

Module-gated on `matrimony`, off by default. Roles below are the same
self-scoped list member-family-service's own profile-update command
uses (`Member, FamilyHead, VolunteerGroupPresident, PathshalaTeacher,
PathshalaStudent, ContentModerator, BoliManager, SamaajAdmin, SuperAdmin`)
for every write, and `Members.Read` for every read — no permission key of
its own.

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/consent-notice` | `Members.Read` | What a member is shown before opting in |
| GET | `/biodata/me` | `Members.Read` | The caller's own biodata, active or not. **204**, not 200 with a null body, if never opted in |
| GET | `/eligibility/me` | `Members.Read` | `{ status }`: `Eligible` (My Profile says Unmarried or Divorced), `NotEligible`, or `Unknown` (no marital status set) |
| POST | `/biodata/me/opt-in` | role list above | Opt in, or rejoin after a withdrawal. 400 without `consentGiven`; **409** `Matrimony.NotEligible` unless eligible |
| PATCH | `/biodata/me` | role list above | Edit an already-opted-in profile. **409** `Matrimony.NotEligible` unless eligible |
| DELETE | `/biodata/me/consent` | role list above | Withdraw — deactivates, does not erase. Proposal history is kept |
| GET | `/biodata/search` | `Members.Read` | Active candidates in the caller's own Samaaj, filtered; excludes the caller; capped at 50 |
| GET | `/biodata/{memberId}` | `Members.Read` | One candidate's full biodata. 404 if inactive, not eligible, or in a different Samaaj. Search leaves out the not-eligible too |
| POST | `/proposals` | role list above | Send a proposal. 409 if a Pending/Accepted one already exists between the same two members |
| GET | `/proposals/sent` | `Members.Read` | The caller's own sent proposals, with the other party resolved |
| GET | `/proposals/received` | `Members.Read` | The caller's own received proposals, with the other party resolved |
| POST | `/proposals/{id}/respond` | role list above, recipient only | Accept or decline. 404 for anyone else |
| POST | `/proposals/{id}/withdraw` | role list above, sender only | Withdraw while still Pending. 404 for anyone else |

No photo or contact endpoints — biodata reuses the member's existing photo
route and never carries Mobile or Email. Search is within the caller's own
Samaaj only; there is no cross-tenant variant.

## businesses-service — `/v1/businesses`

Module-gated on `businesses`, off by default. Roles below are the same
self-scoped list member-family-service's own profile-update command
uses, for every write, and `Members.Read` for every read — no permission
key of its own.

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/mine` | `Members.Read` | The caller's own businesses, active or not |
| POST | `/mine` | role list above | Register a business. Visible in the directory immediately — no moderation |
| PATCH | `/mine/{businessId}` | role list above, owner only | Edit a business the caller owns. 404 for anyone else |
| POST | `/mine/{businessId}/status` | role list above, owner only | Deactivate or reactivate. Reversible |
| POST | `/mine/{businessId}/products` | role list above, owner only | Add a product |
| PATCH | `/mine/{businessId}/products/{productId}` | role list above, owner only | Edit a product's name, description or price |
| POST | `/mine/{businessId}/products/{productId}/availability` | role list above, owner only | Take a product off sale, or put it back. Never deletes |
| POST | `/mine/{businessId}/photo` | role list above, owner only | Upload the business's photo (multipart, one file; JPEG/PNG/WebP read from the bytes, 2 MB). Replaces any. Answers the business |
| DELETE | `/mine/{businessId}/photo` | role list above, owner only | Take the photo down. Twice is success |
| POST | `/mine/{businessId}/products/{productId}/photo` | role list above, owner only | Upload a product's image, the same way |
| DELETE | `/mine/{businessId}/products/{productId}/photo` | role list above, owner only | Take a product's image down. Twice is success |
| GET | `/{businessId}/photo` | `Members.Read` | The photo bytes, for whoever may open the business. `Cache-Control: private` and an ETag |
| GET | `/{businessId}/products/{productId}/photo` | `Members.Read` | A product's image - an off-sale product's only to its owner |
| GET | `/` | `Members.Read` | The directory: active businesses in the caller's own Samaaj, filtered by category/location/term, capped at 50 |
| GET | `/{businessId}` | `Members.Read` | One business's public detail. Every product to its owner, only available ones to anyone else. 404 if inactive-and-not-owner or in a different Samaaj |
| POST | `/orders` | role list above | Place an order. 409 for an unavailable product, 400 for ordering your own business |
| GET | `/orders/mine` | `Members.Read` | Orders the caller has placed |
| GET | `/orders/received` | `Members.Read` | Orders against any business the caller owns |
| POST | `/orders/{id}/respond` | role list above, business owner only | Accept or decline a pending order. 404 for anyone else |
| POST | `/orders/{id}/fulfill` | role list above, business owner only | Mark an accepted order fulfilled |
| POST | `/orders/{id}/cancel` | role list above, buyer or owner | The buyer cancels while Pending; the owner cancels while Accepted |

A business and each product carry `photoUrl` - the GET above, with
`?v={imageId}` so a replaced image is a new URL - and a business carries
`addressLine` beside its `cityId`/`localityId` (2026-10-24).
Directory search and every order are within the caller's own Samaaj only;
there is no cross-tenant variant. An order's total is computed
server-side from each product's price at placement time and never
trusted from the client.

## competition-service — `/v1/competitions`

Module-gated on `competitions`, off by default. New permission
`Competition.Manage`, granted to `SamaajAdmin`, for every organiser call;
`Members.Read` for registering, withdrawing, judging and every read — no
role list of its own, unlike businesses-service, because this service
does need a genuine organiser-vs-participant split.

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/` | `Members.Read` | This Samaaj's competitions, each with the caller's own registration status. `?localityId=` narrows to one locality |
| POST | `/` | `Competition.Manage` | Set a competition up. Starts `Draft`. Optional `localityId`, `venue` (300), `cityId`, `competitionTypeId`, and the format: `roundCount` (0-20; 0 is one judging), `maxMarks` (1-1000), `auditionRequired`, with `auditionDate` and `finalistCount` (1-500) required when it is true. Left out: one judging out of 100, no audition |
| PATCH | `/{id}` | `Competition.Manage` | Correct the details and the format. Draft or taking registrations only; 409 after. Format fields left out stay as they were |
| GET | `/{id}` | `Members.Read` | Rounds, registration count, judges, and the currently-registered member ids an organiser picks entries from - and the member portal's Participants tab |
| POST | `/{id}/photo` | `Competition.Manage` | Upload the cover photo (multipart, one file; JPEG/PNG/WebP read from the bytes, 2 MB). Replaces any; any stage. Answers the competition |
| DELETE | `/{id}/photo` | `Competition.Manage` | Take the photo down. Twice is success |
| GET | `/{id}/photo` | `Members.Read` | The photo bytes. `Cache-Control: private` and an ETag. A competition carries `photoUrl`, with `?v={photoId}` |
| POST | `/{id}/status` | `Competition.Manage` | Strictly forward: Draft → RegistrationOpen → RegistrationClosed → InProgress → Closed. InProgress makes the rounds from the format and enters everybody registered - into the audition if there is one, otherwise every round |
| POST | `/{id}/registrations` | `Members.Read` | Register the caller. Only while registration is open. A repeat registration is success with `registered: false` |
| DELETE | `/{id}/registrations/mine` | `Members.Read` | The caller's own withdrawal. Registering again later reactivates the same row |
| POST | `/{id}/judges` | `Competition.Manage` | Assign a member as judge, or remove one — one call for both directions. `panel`: `Main` (the default), `Audition` or `Both` - 409 for anything but Main without an audition; assigning again on another panel moves the judge |
| POST | `/{id}/rounds` | `Competition.Manage` | Open a round by hand. Before judging starts, rounds opened this way stand in for the format's main rounds |
| GET | `/{id}/rounds/{roundId}` | `Members.Read` | A round's entries. Marks are visible only to the organiser or an assigned judge |
| POST | `/{id}/rounds/{roundId}/close` | `Competition.Manage` | Close a round. No new entries or scores after this. Closing the audition enters its finalists - the top `finalistCount` by average, ties at the cut-off included, nobody unmarked - in every main round; 409 when nobody has been marked |
| POST | `/{id}/rounds/{roundId}/entries` | `Competition.Manage` | Put one registered participant into the round - somebody added late. 409 for a duplicate or an unregistered participant |
| POST | `/{id}/entries/{entryId}/scores` | `Members.Read`, a judge on the panel that judges the round | Record or correct this judge's mark. An upsert while the round is open, unlike a vote. 400 above the competition's `maxMarks`; 403 for the other panel's round; 409 on the judge's own entry |
| GET | `/{id}/judging/mine` | `Members.Read`, a judge of it (403 otherwise) | The judge's own sheet: each round their panel judges, every participant but themselves, and the mark and comment they gave |
| GET | `/{id}/scoresheet` | `Competition.Manage` | Every judge's mark on every entry, round by round, each average, who went through the audition, and the standings now |
| POST | `/{id}/result` | `Competition.Manage` | Publish the final standings and freeze them. Only once every main round is closed; publishing again returns the same |
| GET | `/{id}/result` | `Members.Read` | The published standings. 404 until published. `winners` (rank 1-3, ties included) for everybody; `standings` (every place) for the organiser only |
| POST | `/{id}/rounds/{roundId}/results` | `Competition.Manage` | Compute the ranking and freeze it. Only from a closed round; publishing again returns the same frozen result |
| GET | `/{id}/rounds/{roundId}/results` | `Members.Read` | The published ranking. 404 until it has one |

Standalone from events-service by design — a competition is not an Event.
Individual judge marks are never returned to a caller who is neither the
organiser nor an assigned judge, before or after publication; a judge sees
only their own, and members see the winners and their totals. A total is a
participant's main-round averages added up; equal totals share a place.

## panch-service — `/v1/panch`

Module-gated on `panch`, off by default. New permission `Panch.Manage`,
granted to `SamaajAdmin`, for every organiser call; `Members.Read` for
nominating, voting and every read.

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/panel` | `Members.Read` | The seated Panch (member ids, seated-at) and `viewerIsMember` |
| DELETE | `/panel/{memberId}` | `Panch.Manage` | Unseat one member. The seat stays empty until the next election. Removing someone not seated is success |
| GET | `/elections` | `Members.Read` | This Samaaj's elections, newest first, each with the caller's own ballot choices |
| POST | `/elections` | `Panch.Manage` | Set an election up. Starts `Draft`; nominations must close before voting opens |
| GET | `/elections/{id}` | `Members.Read` | Ballot always; tally only when this caller may see it |
| PATCH | `/elections/{id}` | `Panch.Manage` | Correct the title, description, both windows and results visibility - `Draft` or `NominationsOpen` only. Same body and window rules as creating one; extending nominations is pushing their close out here |
| DELETE | `/elections/{id}` | `Panch.Manage` | `204`. Deletes the election and its nominations - `Draft` or `NominationsOpen` only; once voting opens it holds ballots and is closed instead (`409`) |
| POST | `/elections/{id}/extend-voting` | `Panch.Manage` | `{votingEndAt}`. Keeps voting open longer while it is open - later than the current close only (`409` otherwise) |
| POST | `/elections/{id}/status` | `Panch.Manage` | Strictly forward: `NominationsOpen`, `VotingOpen`, `Closed`. Publishing is its own call |
| POST | `/elections/{id}/candidates` | `Members.Read` | Put a member forward while nominations are open. A repeat is success |
| POST | `/elections/{id}/candidates/{candidateId}/decide` | `Panch.Manage` | Approve onto the ballot, or remove before voting opens |
| POST | `/elections/{id}/ballots` | `Members.Read` | Cast the caller's one ballot: up to ten distinct approved candidates, never themselves. Final; a second ballot returns `accepted: false` with the first's choices |
| POST | `/elections/{id}/results` | `Panch.Manage` | Only from `Closed`. Freezes the ranking and replaces the seated Panch with the ten most-backed candidates (each with at least one vote) |
| GET | `/elections/{id}/results` | `Members.Read` | The published ranking and who was seated. 404 until published |

Member ids on the wire are strings, validated and parsed in the command,
so a malformed id is a 400 rather than an unhandled binding failure.

## trust-service — `/v1/trust`

Module-gated on `trust`, off by default. New permission `Trust.Manage`,
granted to `SamaajAdmin`, for every write except nominating and voting;
`Members.Read` for every read, and for nominating/voting further gated by
being an active member of the Trust the election belongs to.

Trust + roster CRUD, elections for trustee positions, and the Trust's money:
donations, expenses, and a collections ledger that a published Boli result in
the Trust's locality opens against its winner. Amounts are integer paise.

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/trusts` | `Members.Read` | This Samaaj's Trusts, newest first. `?localityId=` narrows to one locality |
| POST | `/trusts` | `Trust.Manage` | Set a Trust up for a locality. Exactly one per (Samaaj, Locality); a second attempt is a 409 |
| PATCH | `/trusts/{id}` | `Trust.Manage` | Correct the name. The locality is not editable - it is what a published Boli is matched on |
| GET | `/trusts/{id}` | `Members.Read` | One Trust |
| POST | `/trusts/{id}/status` | `Trust.Manage` | Active or Inactive. Setting the status it already has is success and changes nothing |
| GET | `/trusts/{id}/members` | `Members.Read` | The Trust's roster, active members first, each with `roleId` and its title as `positionTitle` |
| POST | `/trusts/{id}/members` | `Trust.Manage` | Add a member: `{ memberId, roleId? }`, Trustee by default. 409 when the role is full. Adding one already active is a no-op reported as success; adding one previously removed reactivates their old row |
| PUT | `/trusts/{id}/members/{trustMemberId}/role` | `Trust.Manage` | `{ roleId }` - move an active trustee into a role. 409 on a removed member, and when the role is full |
| GET | `/trusts/{id}/roles` | `Members.Read` | The Trust's roles, Trustee first: `title`, `kind`, `seats` (null for Trustee), `holders`, `isElected` |
| POST | `/trusts/{id}/roles` | `Trust.Manage` | `{ title, seats }` (1-50). 409 on a title the Trust already has |
| PUT | `/trusts/{id}/roles/{roleId}` | `Trust.Manage` | Rename or resize. 409 for Trustee, and below the current holders |
| DELETE | `/trusts/{id}/roles/{roleId}` | `Trust.Manage` | 409 for Trustee, while anybody holds it, and while an election for it is unpublished |
| DELETE | `/trusts/{id}/members/{trustMemberId}` | `Trust.Manage` | Take a member off the roster. Removing one already removed is success and changes nothing |
| GET | `/trusts/{trustId}/elections` | `Members.Read` | This Trust's elections, newest first, with the caller's own ballot on each (`myVoteCandidateIds`) |
| POST | `/trusts/{trustId}/elections` | `Trust.Manage` | `{ roleId, nomination/voting windows }` - an election for a role, filling its `seats`. 409 for Trustee and while another for the role is unpublished. Starts `Draft` |
| GET | `/elections/{id}` | `Members.Read` | One election with its ballot and tally - never hidden |
| POST | `/elections/{id}/status` | `Trust.Manage` | `NominationsOpen`, `VotingOpen` or `Closed`. Strictly forward; 409 moving to `VotingOpen` on an empty ballot |
| POST | `/elections/{id}/candidates` | `Members.Read` + active Trust member | Nominate a fellow active member. A repeat nomination is a no-op reported as success |
| POST | `/elections/{id}/candidates/{candidateId}/decide` | `Trust.Manage` | Approve onto the ballot, or remove before voting opens |
| POST | `/elections/{id}/votes` | `Members.Read` + active Trust member | `{ candidateIds }` - this member's one ballot, 1..`seats` distinct candidates. Self-votes refused (409); a second ballot is reported as `accepted: false`, carrying the first - see the service's own CLAUDE.md |
| POST | `/elections/{id}/publish` | `Trust.Manage` | Only from `Closed`. The top `seats` by tally win (`winnerMemberIds`, ranked) and are seated in the role; prior holders who did not win return to Trustee. Publishing twice changes nothing |
| GET | `/trusts/{id}/donations` | `Trust.Manage` | Donations, newest first. `?status=Pledged\|Collected` |
| POST | `/trusts/{id}/donations` | `Trust.Manage` | Record a pledge: a member id or a donor name, optional phone, amount, purpose, mode (`Cash`/`Cheque`/`Upi`/`BankTransfer`). No receipt number yet; it opens an amount due, chased like any other |
| POST | `/donations/{donationId}/collect` | `Trust.Manage` | Mark it collected, issuing the Trust's next receipt number. 409 if already collected |
| GET | `/trusts/{id}/expenses` | `Trust.Manage` | Expenses, newest first. `?status=Recorded\|Approved` |
| POST | `/trusts/{id}/expenses` | `Trust.Manage` | Record an expense: category, description, amount, optional vendor |
| POST | `/expenses/{expenseId}/approve` | `Trust.Manage` | Approve it. Approving one already approved is 200 and changes nothing |
| GET | `/trusts/{id}/collections` | `Trust.Manage` | Amounts due to the Trust, newest first, each with who owes it. `?status=Due\|Collected` |
| POST | `/collections/{collectionId}/payment` | `Trust.Manage` | Record that it was paid, and the mode (free text, 50 characters). 409 if already paid |
| POST | `/collections/{collectionId}/remind` | `Trust.Manage` | An in-app reminder to the member who owes it. At most once a day; 409 once paid or for a donor with no account |
| GET | `/collections/mine` | `Members.Read` | What the caller still owes to any Trust in their Samaaj, oldest first. The payer is read off the token |

`LocalityId` and member ids on the wire are strings, validated and parsed in
the command, so a malformed id is a 400 rather than an unhandled binding
failure - the same convention panch-service's own member ids use.
`LocalityId` is a loose reference into master-data-service's `Locality`, with
no referential integrity across the service boundary.
