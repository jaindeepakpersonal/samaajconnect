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
| PATCH | `/tenants/{id}/status` | SuperAdmin | Activate/deactivate/archive |
| GET | `/tenants` | SuperAdmin | List all tenants |
| GET | `/tenants/{slug}` | Anonymous | Resolve slug → tenant (registration picker) |
| GET | `/tenants/by-id/{id}` | Anonymous | Resolve id → tenant (used by the gateway on every request) |
| GET | `/tenants/directory` | Anonymous | Active Samaaj a visitor can register into |
| PUT | `/tenants/{id}/grievance-contact` | SuperAdmin, SamaajAdmin | Name who members complain to (DPDP s.13) |
| POST | `/register` | Anonymous | Member registration into one Samaaj |
| POST | `/token/refresh` | Anonymous | Exchange a refresh token for a new access token and the next refresh token. Single-use: presenting one twice ends the whole session |
| POST | `/login` | Anonymous | Common login → returns tenant-scoped JWT |
| GET | `/activations/pending` | SamaajAdmin | Accounts awaiting activation |
| POST | `/activations/{userId}/code` | SamaajAdmin | Mint a one-time activation code (returned once) |
| POST | `/activations/redeem` | Anonymous | Redeem a code and set a first password |
| GET | `/consent-notice` | Anonymous | The consent notice and its version (DPDP s.5) |
| POST | `/me/consents/{purpose}/withdraw` | Authenticated | Withdraw one consent (DPDP s.6(4)) |
| GET | `/me/data-export` | Authenticated | What this service holds about you (DPDP s.11) |
| POST | `/me/erase` | Authenticated | Erase this account and, by event, everything the platform holds (DPDP s.12) |
| GET | `/tenants` | `SuperAdmin` + `Tenant.Manage` | Every Samaaj, any status; `?status=` and `?search=` narrow it |
| GET | `/tenants/modules` | Anonymous | The closed list of module keys, with labels, for the toggles |
| PUT | `/tenants/{id}/modules` | `SuperAdmin` + `Tenant.Manage` | Replace the whole set of modules a Samaaj runs |
| GET | `/roles` | Authenticated | The role and permission matrix the backend enforces. Read-only |
| GET | `/admins` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | This Samaaj's administrators and their roles |
| POST | `/admins` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | Invite an administrator; returns a one-time activation code |
| PUT | `/admins/{userId}/roles/{role}` | `SuperAdmin`, `SamaajAdmin` + `AdminUsers.Manage` | Grant or revoke one role, body `{"granted":bool}` |
| POST | `/logout` | Anonymous (the refresh token is the credential) | End this session; `everywhere: true` ends every session for the account |
| GET | `/me` | Authenticated | Current user + roles + tenant |

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
| GET | `/posts/moderation-queue` | `Timeline.Moderate` | Posts awaiting review, and approved posts members have reported |
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
| GET | `/members/{id}` | Member, SamaajAdmin | Member profile (privacy-filtered) |
| PATCH | `/members/{id}` | Member (self), SamaajAdmin | Update profile |
| POST | `/families` | Member | Create family, become head |
| POST | `/families/join-requests` | Member | Request to join via family code |
| POST | `/families/join-requests/{id}/decide` | FamilyHead | Accept/reject |
| GET | `/children` | Member | Children in your own household |
| POST | `/children` | FamilyHead | Add child profile |
| GET | `/children/data-notice` | Member | What a parent is shown before adding a child (DPDP s.9) |
| GET | `/members/me/data-export` | Authenticated | Profile, family and children (DPDP s.11) |
| POST | `/children/{id}/conversion` | FamilyHead | Start adult-child conversion |
| GET | `/children/conversion-requests` | SamaajAdmin | Requests awaiting a decision |
| POST | `/children/conversion-requests/{id}/decide` | SamaajAdmin | Approve or reject (admin-approved, decided 2026-08-28) |

## volunteer-groups-service — `/v1/volunteer-groups`

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/groups` | Member | List tenant groups |
| POST | `/groups` | SamaajAdmin | Create group |
| POST | `/groups/{id}/applications` | Member | Apply to join |
| GET | `/groups/{id}/applications` | VolunteerGroupPresident | Review queue |
| POST | `/groups/{id}/applications/{appId}/decide` | VolunteerGroupPresident | Accept/reject + assign role |

## events-service — `/v1/events`

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/events` | Member | Upcoming/past tenant events |
| POST | `/events` | SamaajAdmin, VolunteerGroupPresident | Create event |
| POST | `/events/{id}/publish` | SamaajAdmin | Publish |
| POST | `/events/{id}/register` | Member | RSVP (or waitlist if at capacity) |
| GET | `/events/{id}/attendees` | SamaajAdmin, organizer | Attendee list |

## social-issues-service — `/v1/social-issues`

| Method | Path | Roles | Purpose |
|---|---|---|---|
| POST | `/issues` | Member | Submit issue |
| GET | `/issues/approval-queue` | SamaajAdmin, ContentModerator | Pending review |
| POST | `/issues/{id}/decide` | SamaajAdmin, ContentModerator | Approve/reject/request changes |
| POST | `/issues/{id}/publish` | SamaajAdmin | Publish approved issue |
| GET | `/issues/published` | Member | Public list |
| GET | `/issues/{id}/history` | SamaajAdmin | Status/audit history |

## celebrity-voting-service — `/v1/celebrity-voting`

| Method | Path | Roles | Purpose |
|---|---|---|---|
| POST | `/campaigns` | SamaajAdmin | Create campaign |
| POST | `/campaigns/{id}/candidates` | Member, SamaajAdmin | Nominate |
| GET | `/campaigns/{id}/candidates` | Member | List candidates |
| POST | `/campaigns/{id}/votes` | Member | Cast vote (idempotent per voter) |
| POST | `/campaigns/{id}/close` | SamaajAdmin | Close voting |
| POST | `/campaigns/{id}/publish` | SamaajAdmin | Generate + publish Top 10 |
| GET | `/campaigns/{id}/results` | Member | View published results |

## pathshala-service — `/v1/pathshala`

| Method | Path | Roles | Purpose |
|---|---|---|---|
| POST | `/pathshalas` | SuperAdmin | Create Pathshala master record |
| GET | `/pathshalas` | FamilyHead, SamaajAdmin | List available Pathshalas |
| POST | `/pathshalas/{id}/enrollments` | FamilyHead | Enroll eligible child |
| GET | `/enrollments/{id}/my-class` | PathshalaStudent | My Class view |
| GET | `/enrollments/{id}/attendance` | PathshalaStudent | My Attendance |
| POST | `/classes/{id}/attendance` | PathshalaTeacher | Mark attendance |
| GET | `/enrollments/{id}/exams` | PathshalaStudent | My Exams |
| POST | `/exams/{id}/results` | PathshalaTeacher | Record exam result |
| GET | `/enrollments/{id}/progress` | PathshalaStudent, FamilyHead | My Progress |

## boli-service — `/v1/boli`

| Method | Path | Roles | Purpose |
|---|---|---|---|
| POST | `/occasions` | BoliManager | Create occasion |
| POST | `/occasions/{id}/boli-types` | BoliManager | Define Boli type |
| POST | `/occasions/{id}/boli` | BoliManager | Open a Boli for bidding |
| POST | `/boli/{id}/bids` | Member | Place bid |
| GET | `/boli/{id}/bids` | Member, BoliManager | Bid history |
| POST | `/boli/{id}/close` | BoliManager | Close bidding |
| POST | `/boli/{id}/result` | BoliManager | Record result |
| POST | `/boli/{id}/result/publish` | BoliManager | Publish (locks result) |

## audit-notification-service — `/v1/audit`, `/v1/notifications`

| Method | Path | Roles | Purpose |
|---|---|---|---|
| GET | `/audit/logs` | SuperAdmin, SamaajAdmin | Query audit log (filterable) |
| GET | `/audit/me/data-export` | Authenticated | My notifications and my actions (DPDP s.11) |
| GET | `/notifications` | Authenticated | My notifications |
| POST | `/notifications/{id}/read` | Authenticated | Mark read |
| POST | `/notifications/broadcast` | SamaajAdmin, SuperAdmin | Send tenant/platform announcement |
