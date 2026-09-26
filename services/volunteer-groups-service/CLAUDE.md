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
| `GroupMember` | built | Somebody who is in the group, with an optional position. |
| `GroupElection` | built | Aggregate root. An election for one position in one group - nominate, approve, vote, close, publish. See "Elections" below. |
| `ElectionCandidate` | built | Owned by `GroupElection`. Nominated, then Approved onto the ballot. |
| `Vote` | built | One member's one vote in one election. Written directly, outside the aggregate - see "The guarantee" below. |

## Commands

| Command | Policy | Status |
|---|---|---|
| `CreateGroupCommand` | `VolunteerGroups.Manage` | built |
| `UpdateGroupCommand` | `VolunteerGroups.Manage` | built. Corrects the name, description or focus area - not the president, membership or status, each its own command |
| `ChangeGroupStatusCommand` | `VolunteerGroups.Manage` | built |
| `ApplyToGroupCommand` | `Members.Read` | built |
| `DecideApplicationCommand` | `VolunteerGroups.Lead` + is this group's president | built |
| `AssignRolePositionCommand` | `VolunteerGroups.Lead` + is this group's president | built |
| `RemoveGroupMemberCommand` | `VolunteerGroups.Lead` + is this group's president | built |
| `ChangeGroupPresidentCommand` | `VolunteerGroups.Manage` | built |
| `OpenElectionCommand` | `VolunteerGroups.Lead` + is this group's president | built |
| `MoveElectionCommand` | `VolunteerGroups.Lead` + is this group's president | built |
| `UpdateElectionCommand` | `VolunteerGroups.Lead` + is this group's president | built. Corrects the position title or windows - Draft/NominationsOpen only |
| `NominateCandidateCommand` | `VolunteerGroups.Lead` + caller is a member of the group | built |
| `DecideCandidateCommand` | `VolunteerGroups.Lead` + is this group's president | built |
| `CastVoteCommand` | `VolunteerGroups.Lead` + caller is a member of the group | built |
| `PublishElectionResultsCommand` | `VolunteerGroups.Lead` + is this group's president | built |

## Queries

| Query | Policy | Status |
|---|---|---|
| `ListGroupsQuery` | `Members.Read` | built |
| `GetGroupQuery` | `Members.Read` | built |
| `GetApplicationsQuery` | `VolunteerGroups.Lead` + is this group's president | built |
| `ListElectionsQuery` | `Members.Read` | built |
| `GetElectionQuery` | `Members.Read` | built |

## Events published

| Event | Topic | Raised by |
|---|---|---|
| `GroupCreatedDomainEvent` | `volunteer-groups.group.created.v1` | `VolunteerGroup.Create` |
| `GroupApplicationSubmittedDomainEvent` | `volunteer-groups.application.submitted.v1` | `VolunteerGroup.Apply` |
| `GroupApplicationDecidedDomainEvent` | `volunteer-groups.application.decided.v1` | `VolunteerGroup.DecideApplication` |
| `GroupRolePositionAssignedDomainEvent` | `volunteer-groups.role-position.assigned.v1` | `VolunteerGroup.AssignRolePosition`, `VolunteerGroup.ElectPosition` |
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
| POST | `/v1/volunteer-groups/groups/{id}/applications` | `Members.Read` |
| GET | `/v1/volunteer-groups/groups/{id}/applications` | `VolunteerGroups.Lead` + president |
| POST | `/v1/volunteer-groups/groups/{id}/applications/{applicationId}/decide` | `VolunteerGroups.Lead` + president |
| PUT | `/v1/volunteer-groups/groups/{id}/members/{memberId}/position` | `VolunteerGroups.Lead` + president |
| DELETE | `/v1/volunteer-groups/groups/{id}/members/{memberId}` | `VolunteerGroups.Lead` + president |
| PATCH | `/v1/volunteer-groups/groups/{id}/president` | `VolunteerGroups.Manage` |
| GET | `/v1/volunteer-groups/groups/{id}/elections` | `Members.Read` |
| POST | `/v1/volunteer-groups/groups/{id}/elections` | `VolunteerGroups.Lead` + president |
| GET | `/v1/volunteer-groups/elections/{id}` | `Members.Read` |
| PATCH | `/v1/volunteer-groups/elections/{id}` | `VolunteerGroups.Lead` + president |
| POST | `/v1/volunteer-groups/elections/{id}/status` | `VolunteerGroups.Lead` + president |
| POST | `/v1/volunteer-groups/elections/{id}/candidates` | `VolunteerGroups.Lead` + group member |
| POST | `/v1/volunteer-groups/elections/{id}/candidates/{candidateId}/decide` | `VolunteerGroups.Lead` + president |
| POST | `/v1/volunteer-groups/elections/{id}/votes` | `VolunteerGroups.Lead` + group member |
| POST | `/v1/volunteer-groups/elections/{id}/publish` | `VolunteerGroups.Lead` + president |
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

## Elections

Added 2026-09-12. A group position - Secretary, Coordinator - used to be
whatever free text the president typed at accept-time or afterwards. It still
can be, through `AssignRolePositionCommand`, but the intended path now is an
election: the president opens one for a named position, group members
nominate each other, the president approves nominations onto the ballot,
voting opens, and publishing installs the winner.

**This copies celebrity-voting-service's Campaign -> Candidate -> Vote ->
published-result shape, deliberately not by sharing code with it.**
celebrity-voting-service runs one platform-wide award with a ranked top-N
result; `GroupElection` runs one group's one position with exactly one
winner. Different enough shapes that coupling them would cost more than the
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
`VotingCampaign`: a `Vote` is written directly, and the unique index on
`(ElectionId, VoterMemberId)` - not a check-then-insert in a handler, and not
a distributed lock - is what actually prevents a double vote. See
celebrity-voting-service's own `CLAUDE.md` "The guarantee" section; the
mechanism here is the identical one, copied rather than reinvented, at a
smaller scale that does not make the mistake any less of a mistake.

**Self-votes are refused**, matching celebrity-voting-service's own rule -
confirmed with the user rather than assumed, since a real election
reasonably could go either way on this.

**Publishing is what installs the winner, not merely what announces
them.** Confirmed with the user before this was built: `PublishElection
ResultsCommand` freezes the winner on `GroupElection` and then calls
`VolunteerGroup.ElectPosition`, which sets the winner's `RolePosition` to the
election's own `PositionTitle` and clears that exact title from anyone else
in the group who held it. Two aggregates, one transaction, one handler - the
same shape `businesses-service`'s own `Order` handler already uses when it
touches a `Business`.

**There is no `ElectionResult` aggregate, unlike celebrity-voting-service's
`CampaignResult`.** A `GroupElection` has exactly one winner, not a ranked
list, so the frozen outcome is just `WinnerMemberId`/`PublishedAt` on the
election itself - there is nothing here a separate table would buy.

**The tie-break is the identical mechanism celebrity-voting-service's
`RankBy` uses**: `WinnerBy` orders by tally descending, then by earliest
nomination - held by two things agreeing, the ordering and LINQ's own stable
sort, the same reasoning that service's own `CLAUDE.md` gives for testing it
with candidates created deliberately out of order.

**Accepting a join request no longer carries a position at all.**
`DecideApplicationCommand` used to take an optional `RolePosition` and set it
on the new member in the same call - a unilateral president decision with no
request, no nomination, no vote. That parameter is gone; a new member has no
position until an election gives them one, or the president assigns one
directly through `AssignRolePositionCommand`, which is untouched and remains
the manual override path this plan deliberately left alone.

**Nominations that closed too early are reopened by editing, not by a
separate "reopen" command.** `UpdateElectionCommand` (`PATCH
/elections/{id}`) corrects the position title and all four windows, allowed
in `Draft`/`NominationsOpen` and refused from `VotingOpen` on - the identical
rule and reasoning celebrity-voting-service's own campaign-editing command
uses. An election left at `NominationsOpen` past its own `NominationEndAt`
never advanced `Status`; `AcceptsNominations` is already false with nothing
to walk back, so pushing the date out again is the whole fix. Nothing here
moves `Status` itself, so this is not a backdoor around "strictly forward" -
an election already at `VotingOpen` or later is refused before the write.

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
elects one. **Electing the position titled "President" now installs an actual
president** - `VolunteerGroup.ElectPosition` hands off to `ChangePresident`.
Before, only the label moved, so a winner would have read "President" while
`IsPresident` stayed false for them and the previous president kept every
power.

**So that election can happen at all, an admin has a narrow, temporary
authority - and only while there is no president.** A president-less group has
no members, so nobody can be nominated or vote, and only a president could
accept a joiner or open an election: without something, "elect a president"
would be a path nobody could start. `GroupAuthority` (Application/Groups)
lets a `SamaajAdmin`/`SuperAdmin` decide join requests, read the queue, and
open, advance and publish the *President* election - and nothing else.
Assigning positions, removing members and any other election keep waiting for
a president, and the moment one exists the admin's powers stop, because the
check reads the group's state each time rather than granting a standing role.
`GroupResponse.ICanLead` is that answer computed once on the server, so the
portals never re-derive it from a role. Nominating and voting were already open
to every member.


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

**`RolePosition` is free text and is not a platform role.** What someone is
called inside a Seva group grants nothing anywhere and should not need a
deployment to add. The roles in `AuthorizationCatalog` are what actually gate
and are a closed list for exactly that reason.

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
follows `AssignRolePositionCommand`'s own template exactly - `VolunteerGroups
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

**Removing raises the event `AssignRolePosition`'s sibling already did.**
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
  ballot, the tie-break, and publishing; `VolunteerGroupTests` covers
  `ElectPosition` installing a winner and clearing a same-titled predecessor.
- `Sangam.VolunteerGroups.IntegrationTests` — Testcontainers Postgres, no Kafka.
  The tenant query filter is applied by the DbContext rather than any handler,
  and the outbox guarantee is transactional; neither can be shown against a
  substituted repository. There is a named regression test for the permission
  split — `A_president_who_is_only_an_ordinary_member_can_still_run_their_group`.
  `ElectionEndpointsTests` walks nominate -> approve -> vote -> close -> publish
  end to end through the endpoints and asserts the winner lands on the group's
  own roster; `VoteIndexTests` names the double-voting guarantee directly
  against `pg_indexes`, the same proof celebrity-voting-service's own test of
  the same name already runs.

```
dotnet test services/volunteer-groups-service/Sangam.VolunteerGroups.sln
```

`scripts/smoke-through-gateway.sh` covers the whole path through the gateway,
including a president who is an ordinary member deciding a real application.
