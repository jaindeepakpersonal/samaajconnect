# volunteer-groups-service

## Purpose

Volunteer groups within a Samaaj — a Seva group, a Yuva Mandal, an education
group — and the join flow between a member who wants in and the president who
decides.

Module-gated on `community`, the same key as timeline-service: switching that
module off takes both away.

## Entities

| Entity | Status | Notes |
|---|---|---|
| `VolunteerGroup` | built | Tenant-scoped. The join flow is the substance of it. |
| `GroupApplication` | built | A request and its outcome. Kept after the decision. |
| `GroupMember` | built | Somebody who is in the group, holding exactly one of its roles. |
| `GroupRole` | built | One of the group's volunteer roles and its seats. Volunteer and President are built in; the group defines the rest. See "Volunteer roles" below. |
| `GroupPhoto` | built | The group's banner photo, hosted here. Tenant-scoped; the type is sniffed from the bytes. See "The group photo" below. |
| `GroupElection` | built | Aggregate root. An election for one of the group's roles, filling its seats - nominate, approve, vote, close, publish. See "Elections" below. |
| `ElectionCandidate` | built | Owned by `GroupElection`. Nominated, then Approved onto the ballot. |
| `Vote` | built | One pick on one member's ballot; a ballot is up to the role's seats of them. Written directly, outside the aggregate - see "Elections" below. |

## Commands

| Command | Policy | Status |
|---|---|---|
| `CreateGroupCommand` | `VolunteerGroups.Manage` | built |
| `UpdateGroupCommand` | `VolunteerGroups.Manage` | built. Corrects the name, description or focus area - not the president, membership or status, each its own command |
| `ChangeGroupStatusCommand` | `VolunteerGroups.Manage` | built |
| `UploadGroupPhotoCommand` | `VolunteerGroups.Manage`, an Area Admin only in their locality | built. Replaces any photo in the same transaction |
| `RemoveGroupPhotoCommand` | `VolunteerGroups.Manage`, an Area Admin only in their locality | built. Idempotent |
| `ApplyToGroupCommand` | `Members.Read` | built |
| `DecideApplicationCommand` | `VolunteerGroups.Lead` + is this group's president | built |
| `DefineGroupRoleCommand` | `VolunteerGroups.Lead` + president or administrator of this group | built |
| `UpdateGroupRoleCommand` | `VolunteerGroups.Lead` + president or administrator of this group | built. Not Volunteer or President; never below its holders |
| `RemoveGroupRoleCommand` | `VolunteerGroups.Lead` + president or administrator of this group | built. Only a role nobody holds and no election is deciding |
| `AssignGroupRoleCommand` | `VolunteerGroups.Lead` + president or administrator of this group | built. The manual override beside an election; not the President role |
| `RemoveGroupMemberCommand` | `VolunteerGroups.Lead` + is this group's president | built |
| `ChangeGroupPresidentCommand` | `VolunteerGroups.Manage` | built |
| `OpenElectionCommand` | `VolunteerGroups.Lead` + president or administrator of this group | built |
| `MoveElectionCommand` | `VolunteerGroups.Lead` + president or administrator of this group | built |
| `UpdateElectionCommand` | `VolunteerGroups.Lead` + president or administrator of this group | built. Corrects the role or windows - Draft/NominationsOpen only |
| `NominateCandidateCommand` | `VolunteerGroups.Lead` + caller is a member of the group | built |
| `DecideCandidateCommand` | `VolunteerGroups.Lead` + president or administrator of this group | built |
| `CastVoteCommand` | `VolunteerGroups.Lead` + caller is a member of the group | built |
| `PublishElectionResultsCommand` | `VolunteerGroups.Lead` + president or administrator of this group | built |

## Queries

| Query | Policy | Status |
|---|---|---|
| `ListGroupsQuery` | `Members.Read` | built |
| `GetGroupQuery` | `Members.Read` | built |
| `GetGroupPhotoQuery` | `Members.Read` | built. Bytes, `Cache-Control: private` |
| `GetApplicationsQuery` | `VolunteerGroups.Lead` + is this group's president | built |
| `ListElectionsQuery` | `Members.Read` | built |
| `GetElectionQuery` | `Members.Read` | built |

## Events published

| Event | Topic | Raised by |
|---|---|---|
| `GroupCreatedDomainEvent` | `volunteer-groups.group.created.v1` | `VolunteerGroup.Create` |
| `GroupApplicationSubmittedDomainEvent` | `volunteer-groups.application.submitted.v1` | `VolunteerGroup.Apply` |
| `GroupApplicationDecidedDomainEvent` | `volunteer-groups.application.decided.v1` | `VolunteerGroup.DecideApplication` |
| `GroupRolePositionAssignedDomainEvent` | `volunteer-groups.role-position.assigned.v1` | `VolunteerGroup.AssignRole`, `VolunteerGroup.ElectRole`. Carries `RoleId`, and the role's title as `RolePosition` |
| `GroupRoleDefinedDomainEvent` | `volunteer-groups.role.defined.v1` | `VolunteerGroup.DefineRole` |
| `GroupRoleUpdatedDomainEvent` | `volunteer-groups.role.updated.v1` | `VolunteerGroup.UpdateRole` |
| `GroupRoleRemovedDomainEvent` | `volunteer-groups.role.removed.v1` | `VolunteerGroup.RemoveRole` |
| `GroupMemberRemovedDomainEvent` | `volunteer-groups.member.removed.v1` | `VolunteerGroup.RemoveMember` |
| `GroupPresidentChangedDomainEvent` | `volunteer-groups.president.changed.v1` | `VolunteerGroup.ChangePresident` |
| `GroupStatusChangedDomainEvent` | `volunteer-groups.group.status-changed.v1` | `VolunteerGroup.ChangeStatus` |
| `GroupUpdatedDomainEvent` | `volunteer-groups.group.updated.v1` | `VolunteerGroup.Update` |
| `GroupElectionStatusChangedDomainEvent` | `volunteer-groups.election.status-changed.v1` | `GroupElection.MoveTo` |
| `GroupElectionClosedDomainEvent` | `volunteer-groups.election.closed.v1` | `GroupElection.MoveTo` |
| `GroupElectionResultsPublishedDomainEvent` | `volunteer-groups.election.results-published.v1` | `GroupElection.Publish` |
| `GroupElectionUpdatedDomainEvent` | `volunteer-groups.election.updated.v1` | `GroupElection.Update` |

**Both `GroupMemberRemovedDomainEvent` and `GroupPresidentChangedDomainEvent` were
fiction until 2026-09-05.** This table said "Raised by `VolunteerGroup
.ChangePresident`" since the row was written, which was true of the method and
false of the platform: nothing ever called it. `VolunteerGroup.RemoveMember`
raised no event at all - it was never called either, so there was nothing to
raise it from. See "The president had no way to remove anyone, and a group
could never change hands" below.

## Events consumed

None. A leaf service, like timeline-service — which is why there is no consumer
here and no Kafka in its integration tests.

## API endpoints

| Method | Path | Roles |
|---|---|---|
| GET | `/v1/volunteer-groups/groups` | `Members.Read` |
| POST | `/v1/volunteer-groups/groups` | `VolunteerGroups.Manage` |
| GET | `/v1/volunteer-groups/groups/{id}` | `Members.Read` |
| PATCH | `/v1/volunteer-groups/groups/{id}` | `VolunteerGroups.Manage` |
| PATCH | `/v1/volunteer-groups/groups/{id}/status` | `VolunteerGroups.Manage` |
| POST | `/v1/volunteer-groups/groups/{id}/photo` | `VolunteerGroups.Manage` |
| GET | `/v1/volunteer-groups/groups/{id}/photo` | `Members.Read` |
| DELETE | `/v1/volunteer-groups/groups/{id}/photo` | `VolunteerGroups.Manage` |
| POST | `/v1/volunteer-groups/groups/{id}/applications` | `Members.Read` |
| GET | `/v1/volunteer-groups/groups/{id}/applications` | `VolunteerGroups.Lead` + president |
| POST | `/v1/volunteer-groups/groups/{id}/applications/{applicationId}/decide` | `VolunteerGroups.Lead` + president |
| PUT | `/v1/volunteer-groups/groups/{id}/members/{memberId}/role` | `VolunteerGroups.Lead` + president or administrator |
| POST | `/v1/volunteer-groups/groups/{id}/roles` | `VolunteerGroups.Lead` + president or administrator |
| PATCH | `/v1/volunteer-groups/groups/{id}/roles/{roleId}` | `VolunteerGroups.Lead` + president or administrator |
| DELETE | `/v1/volunteer-groups/groups/{id}/roles/{roleId}` | `VolunteerGroups.Lead` + president or administrator |
| DELETE | `/v1/volunteer-groups/groups/{id}/members/{memberId}` | `VolunteerGroups.Lead` + president |
| PATCH | `/v1/volunteer-groups/groups/{id}/president` | `VolunteerGroups.Manage` |
| GET | `/v1/volunteer-groups/groups/{id}/elections` | `Members.Read` |
| POST | `/v1/volunteer-groups/groups/{id}/elections` | `VolunteerGroups.Lead` + president or administrator |
| GET | `/v1/volunteer-groups/elections/{id}` | `Members.Read` |
| PATCH | `/v1/volunteer-groups/elections/{id}` | `VolunteerGroups.Lead` + president or administrator |
| POST | `/v1/volunteer-groups/elections/{id}/status` | `VolunteerGroups.Lead` + president or administrator |
| POST | `/v1/volunteer-groups/elections/{id}/candidates` | `VolunteerGroups.Lead` + group member |
| POST | `/v1/volunteer-groups/elections/{id}/candidates/{candidateId}/decide` | `VolunteerGroups.Lead` + president or administrator |
| POST | `/v1/volunteer-groups/elections/{id}/votes` | `VolunteerGroups.Lead` + group member. Body `candidateIds`: up to the role's seats |
| POST | `/v1/volunteer-groups/elections/{id}/publish` | `VolunteerGroups.Lead` + president or administrator |
| GET | `/health` | anonymous |

## Authorization: two permissions, and why

**`VolunteerGroups.Manage`** creates a group, names its president, and
deactivates it. A Samaaj admin's decision: a group is part of how a Samaaj
organises itself.

**`VolunteerGroups.Lead`** runs a group you are the president *of*: its queue,
its decisions, its positions. **Every member holds it**, and that is the point.

The split exists because the first arrangement was wrong and the smoke test
caught it. Gating the president-side operations on `VolunteerGroups.Manage`
meant a president could not run their own group unless they were also a Samaaj
admin — and a member cannot make themselves a president, so there was no way to
reach those endpoints at all as the design intended.

This is the **third** time this shape has bitten:

- `Family.Write` was held only by `FamilyHead`, which nothing grants. Fixed by
  granting it to `Member`.
- `VolunteerGroups.Manage` was held only by `SamaajAdmin` and
  `VolunteerGroupPresident`, and nothing grants the latter either.
- Both times the symptom was an endpoint nobody could reach, and both times the
  fix was the same: **a permission held only by an ungranted role is a
  permission nobody has.** When adding one, ask which *granted* role carries it.

The permission is only ever the outer gate. Being *this* group's president is
the inner one, checked against the data in each handler — a stronger check than
a role claim, and the same shape as member-family-service's family head.

## Volunteer roles

Added 2026-09-26, replacing a free-text position. **Every member holds exactly
one of their group's roles.** Two are built into every group and can be neither
renamed, resized nor removed: **Volunteer** - what everybody holds on joining
and until elected to something else, with no seat limit and no election - and
**President**, one seat. Anything else - Secretary, Treasurer, three
Coordinators - the group defines as an Elected role with its own seat count.

**Who defines, assigns and elects them: the president, and the group's
administrators at any time** (`GroupAuthority.CanRunElections`). Before, an
admin could run only the President election and only while a group had none,
which left every other role in a president-less group with no way to be filled
- the user asked for administrators to run all of them. Deciding join requests
is unchanged: still the president's, with the admin's narrow stand-in below.

**The President role moves only with the presidency**, in both directions: the
president cannot be assigned or elected into another role, and nobody is
assigned President - that is `ChangeGroupPresidentCommand` or a President
election, both of which move `PresidentMemberId` and the role together.
Otherwise the title and the power would disagree about who runs the group.
Nominating the president for another role is refused before anybody votes.

**Seats are a limit, not a target.** Assigning into a full role is refused; a
role cannot shrink below its holders or be removed while anybody holds it, or
while an election is still deciding it. Titles are unique per group whatever
their case. The migration (`VolunteerRoles`) turned every distinct typed
position - members' and elections' - into an Elected role sized to however
many held it, put the real president in President and everyone else in
Volunteer; `VolunteerRolesMigrationTests` runs it against legacy rows.

## Elections

Added 2026-09-12. An election is opened for one of the group's roles (never
Volunteer), group members nominate each other, the president or an
administrator approves nominations onto the ballot, voting opens, and
publishing installs the winners. **A role has one election running at a
time** - a second would let two ballots decide the same seats.

**An election fills as many seats as its role has, frozen when it is set up.**
`GroupElection.Seats` is copied from the role, so resizing the role mid-vote
does not change a ballot anybody has seen. Each voter picks up to that many
candidates, every pick counting the same, as **one final ballot** - like a
Panch ballot, coming back for another pick is a second ballot and is refused.
The top N by tally win (ties to the earlier nomination); a ballot shorter than
the seats elects everybody on it and leaves the rest empty. Publishing sends
whoever held the role and did not win back to Volunteer.

**This copies celebrity-voting-service's Campaign -> Candidate -> Vote ->
published-result shape, deliberately not by sharing code with it.**
celebrity-voting-service runs one platform-wide award with a ranked top-N
result; `GroupElection` runs one group's one role for its few seats.
Different enough shapes that coupling them would cost more than the
duplication saves - the same "copy the template, don't couple the services"
choice this repo already made when `businesses-service`'s `Order` took its
state machine from this service's own `GroupApplication`.

**The electorate is the group, not the Samaaj.** Celebrity-voting's ballot is
open to any Samaaj member; here, only a current `GroupMember` of the group an
election belongs to may be nominated, may nominate, or may vote.
`NominateCandidateCommand` and `CastVoteCommand` both load the owning
`VolunteerGroup` and check `HasMember` before touching the election itself.

**Votes are deliberately not part of the `GroupElection` aggregate**, for the
identical reason celebrity-voting-service's own votes are not part of
`VotingCampaign`: a `Vote` is written directly, and database indexes - not a
check-then-insert in a handler, and not a distributed lock - are what actually
prevent a double vote. See celebrity-voting-service's own `CLAUDE.md` "The
guarantee" section.

**A multi-seat ballot kept that guarantee by numbering its picks.** Each
`Vote` carries a `Choice` from 1, a ballot is written in one transaction, and
two unique indexes hold it: `(ElectionId, VoterMemberId, Choice)` - every
ballot starts at 1, so a second one collides however it was built - and
`(ElectionId, VoterMemberId, CandidateId)`, so nobody picks one candidate
twice. The handler refuses more picks than seats; it is the only thing that
knows the seats, and that check is not a race. A single-seat election is
exactly the old one-vote-per-voter index. panch-service stores a ballot as one
row with a list instead; this kept the tally a GROUP BY over the table it
already was.

**Self-votes are refused**, matching celebrity-voting-service's own rule -
confirmed with the user rather than assumed, since a real election
reasonably could go either way on this.

**Publishing is what installs the winners, not merely what announces
them.** Confirmed with the user before this was built: `PublishElection
ResultsCommand` calls `VolunteerGroup.ElectRole` - which moves the winners into
the role and everyone else who held it back to Volunteer, or refuses and
changes nothing - and only then freezes `WinnerMemberIds` on `GroupElection`.
Two aggregates, one transaction, one handler - the same shape
`businesses-service`'s own `Order` handler already uses when it touches a
`Business`.

**There is no `ElectionResult` aggregate, unlike celebrity-voting-service's
`CampaignResult`.** A `GroupElection` has a handful of winners at most, so the
frozen outcome is `WinnerMemberIds` (a `uuid[]`, in ranked order) and
`PublishedAt` on the election itself - there is nothing a separate table
would buy.

**The tie-break is the identical mechanism celebrity-voting-service's
`RankBy` uses**: `WinnersBy` orders by tally descending, then by earliest
nomination - held by two things agreeing, the ordering and LINQ's own stable
sort, the same reasoning that service's own `CLAUDE.md` gives for testing it
with candidates created deliberately out of order.

**Accepting a join request carries no role.** A new member is a Volunteer
until an election gives them another, or the president or an administrator
assigns one directly through `AssignGroupRoleCommand`, the manual override.

**Nominations that closed too early are reopened by editing, not by a
separate "reopen" command.** `UpdateElectionCommand` (`PATCH
/elections/{id}`) corrects the role and all four windows, allowed
in `Draft`/`NominationsOpen` and refused from `VotingOpen` on - the identical
rule and reasoning celebrity-voting-service's own campaign-editing command
uses. An election left at `NominationsOpen` past its own `NominationEndAt`
never advanced `Status`; `AcceptsNominations` is already false with nothing
to walk back, so pushing the date out again is the whole fix. Nothing here
moves `Status` itself, so this is not a backdoor around "strictly forward" -
an election already at `VotingOpen` or later is refused before the write.

## The group photo

Added 2026-10-24: a group has one photo, shown as the banner across the top of
its page in the member portal and set from its page in the admin panel.

**The platform hosts it**, the same way member-family-service hosts member
photos and identity-tenant-service Samaaj logos, and for their reason: a
URL somebody typed would have every member who opens the page fetch the
picture from whatever host it names. The bytes live in `group_photos` in this
service's own database, inside the tenant filter and the backup dump.

**`ImageContent` is the third verbatim copy** (namespace aside) -
`scripts/security-invariants.sh` holds all three identical. JPEG, PNG and WebP
only, read from the bytes; the upload's declared type is never consulted, and
SVG stays out because it can carry script and would be served from the
platform's own origin.

**Setting it is editing the group**, so it is gated exactly like
`UpdateGroupCommand` - `VolunteerGroups.Manage`, an Area Admin only for a group
in their own locality, everyone else told there is no such group. Not the
president: a group's name and description are the Samaaj's to correct, and
its public face is the same kind of thing.

**Reading it needs a signed-in member of the Samaaj**, the same people who can
open the group (`GetGroupQuery`), and it is served `Cache-Control: private`.
Unlike a logo it is often a photograph of people. `GroupResponse.PhotoUrl` is
the relative path, null when there is none; both portals load it through
`AuthedImageDirective` so the token travels with the request.

Replacing a photo deletes the old row in the same transaction -
`VolunteerGroup.SetPhoto` hands back the id it replaced rather than deleting
anything itself. Setting or removing one raises
`volunteer-groups.group.updated.v1`, like any other edit to the group.
`GroupPhotoTests` holds all of it.

## Decisions worth knowing before you change this service

**A group may be created without a president, added 2026-09-19.** Until then a
president was required in three places (the command, the aggregate, a NOT NULL
column), on the reasoning that a group with nobody able to decide its
applications is one whose join requests go nowhere. That reasoning was right
about the consequence and wrong about the remedy: a Super Admin often sets a
group up before anyone has been chosen to lead it. `PresidentMemberId` is
nullable now and a president-less group starts with no members at all.

It gets a president two ways, and both are real: a Samaaj admin picks an
existing member through `ChangeGroupPresidentCommand` (which already worked on
"the group has a different president" and now also on "has none"), or the group
elects one. **Electing the President role installs an actual president** -
`VolunteerGroup.ElectRole` hands off to `ChangePresident`.
Before, only the label moved, so a winner would have read "President" while
`IsPresident` stayed false for them and the previous president kept every
power.

**So that election can happen at all, an admin has a narrow, temporary
authority - and only while there is no president.** A president-less group has
no members, so nobody can be nominated or vote, and only a president could
accept a joiner or open an election: without something, "elect a president"
would be a path nobody could start. `GroupAuthority` (Application/Groups)
lets a `SamaajAdmin`/`SuperAdmin` decide join requests and read the queue.
Removing members keeps waiting for a president, and the moment one exists that
power stops, because the check reads the group's state each time rather than
granting a standing role. `GroupResponse.ICanLead` is that answer computed once
on the server, so the portals never re-derive it from a role. Roles and
elections were narrowed the same way until 2026-09-26 and are not now - see
"Volunteer roles" above; `ICanRunElections` is theirs. Nominating and voting
were always open to every member.


**A group may serve one locality, and that decides which Area Admin
administers it - never who may join it.** `VolunteerGroup.LocalityId` is
optional (null: the whole Samaaj) and set on create and edit. An Area Admin
(identity-tenant-service's `AreaAdmin`, the `locality_id` token claim read
into `IAreaScope`) holds `VolunteerGroups.Manage` for their own locality's
groups only: a group they set up lands there, one elsewhere or with no
locality answers 404 to status, president and edit, and they cannot move one
out. The president-less-group powers in `GroupAuthority` follow the same line
- `Administers(user, area, group)` replaced a role-only `IsGroupAdmin()`, so
`iCanLead` and every election and join-request check ask about *this* group.
Listing, applying, nominating and voting are untouched: an Area Admin is a
member of the whole Samaaj like anybody else.

**Creating a group is the Samaaj's decision; who joins it is the president's.**
That asymmetry is the whole service. A Samaaj admin who is not a given group's
president cannot decide its applications, and gets the same answer a stranger
would.

**A group's own name, description and focus area could be set once and never
corrected, until `UpdateGroupCommand` (`PATCH /groups/{id}`) closed that gap.**
Same policy as creating one, `VolunteerGroups.Manage` - a typo in a group's name
is the Samaaj's business to fix, not the president's, the same split
`ChangeGroupPresidentCommand` already draws between running a group and
deciding who runs it. It touches none of those three, each already its own
command, and refuses a rename onto a name another group in the same Samaaj
already holds, the identical check `CreateGroupCommand` makes at creation -
`NameExistsAsync` grew an `excludingGroupId` so a group keeping its own name is
not mistaken for colliding with itself.

**Refusals from the presidency check answer "not found", not "forbidden".**
Whether a group has applications waiting is itself the president's business, so
"you may not see this queue" would leak that there is one.

**A group cannot exist without a president.** `Create` throws without one, and
the validator refuses the request before it gets there: a group with no
president has nobody to decide its applications, so every request to join would
queue forever with no way to tell that is what was happening.

**A rejected applicant may apply again.** Circumstances and minds both change,
and a permanent bar from one refusal is a heavier consequence than a president
was choosing at the time. The old row is replaced rather than kept alongside, so
the queue shows one live request. A member with a *pending* request cannot
apply twice.

**Applications are kept after the decision.** "Were they ever accepted, and by
whom?" is a question a president will be asked and needs an answer to that does
not depend on somebody remembering.

**The application note never leaves the group.** It is what a member wrote about
themselves, for the president who has to read it — not for
audit-notification-service, which stores payloads verbatim in an append-only
table.

**A group role is not a platform role.** What someone is called inside a Seva
group grants nothing anywhere else, and a group defines its own without a
deployment. The roles in `AuthorizationCatalog` are what actually gate and are
a closed list for exactly that reason. (It was free text on the member until
2026-09-26; see "Volunteer roles".)

**The president had no way to remove anyone, and a group could never change
hands, until 2026-09-05.** `VolunteerGroup.RemoveMember` and `.ChangePresident`
were both complete, both unit-tested at the domain level, and both called from
nowhere - not from a handler, not from an endpoint, not even from another
domain method. A president could accept an application and give somebody a
position; there was no way to undo either, and a group's only president was
whoever created it, for the entire life of the group.

Found the way the last two cycles found the same shape elsewhere in the
platform: grepping every service for a public domain method that changes real
state and has no caller outside its own file. `RemoveGroupMemberCommand`
follows the assign-a-position command's own template exactly (now `AssignGroupRoleCommand`) - `VolunteerGroups
.Lead`, refused as "not found" to a non-president for the reason stated above.
`ChangeGroupPresidentCommand` follows `ChangeGroupStatusCommand`'s -
`VolunteerGroups.Manage`, because who runs a group is a Samaaj admin's decision
about how the Samaaj organises itself, not the outgoing president's about their
own replacement.

**The president cannot be removed from their own group, and handing over leaves
the outgoing president in it.** A group whose president is not a member of it
has nobody able to decide its applications; and removing the outgoing president
would cost the group its most experienced volunteer as a side effect of an
administrative change. Both refusals are enforced twice now - once in the
command handler, with its own message, and once again inside the aggregate
itself, which is what still refuses correctly even if a future handler forgets
to ask first.

**Removing raises the event assigning a role's sibling already did.**
`GroupApplicationDecidedDomainEvent` names who accepted a member; nothing named
who put one out, because nothing could. `GroupMemberRemovedDomainEvent` carries
`RemovedBy` for the same reason - "who let them in, and who put them out?" is
one question asked about the same group.

**Deactivating keeps the members.** A deactivated group is still visible and
still has its history; it simply takes no new applications. Deleting it would
erase the record of who volunteered for what, which is the part worth keeping.

**Only the president is told how many applications are waiting.** To anyone else
that is a fact about other members' pending requests.

**Domain-assigned keys are `ValueGeneratedNever`.** All three. See
timeline-service's note; this repo has hit that default three times.

## Dependencies

- **Postgres** `samaajconnect_volunteer_groups` — `ConnectionStrings__Default`
- **Kafka** — `Kafka__BootstrapServers`, producer only
- **Jwt** — `Jwt__SigningKey`, validation only. This service never mints tokens.
- No Redis dependency.

## Testing

- `Sangam.VolunteerGroups.UnitTests` — the aggregate. The join flow, the
  presidency rules and the status transitions are all pure decisions.
  `GroupElectionTests` covers the election's own forward-only sequence, the
  ballot, the tie-break, multi-seat winners, and publishing;
  `VolunteerGroupTests` covers the built-in roles, seats, the presidency rule,
  and `ElectRole` installing winners and sending the rest back to Volunteer.
- `Sangam.VolunteerGroups.IntegrationTests` — Testcontainers Postgres, no Kafka.
  The tenant query filter is applied by the DbContext rather than any handler,
  and the outbox guarantee is transactional; neither can be shown against a
  substituted repository. There is a named regression test for the permission
  split — `A_president_who_is_only_an_ordinary_member_can_still_run_their_group`.
  `ElectionEndpointsTests` walks nominate -> approve -> vote -> close -> publish
  end to end through the endpoints and asserts the winner lands on the group's
  own roster, including a two-seat role; `VoteIndexTests` names the
  double-voting guarantee directly against `pg_indexes`, the same proof
  celebrity-voting-service's own test of the same name already runs;
  `VolunteerRolesMigrationTests` migrates legacy rows through `VolunteerRoles`.

```
dotnet test services/volunteer-groups-service/Sangam.VolunteerGroups.sln
```

`scripts/smoke-through-gateway.sh` covers the whole path through the gateway,
including a president who is an ordinary member deciding a real application.
