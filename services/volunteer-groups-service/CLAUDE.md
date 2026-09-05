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

## Commands

| Command | Policy | Status |
|---|---|---|
| `CreateGroupCommand` | `VolunteerGroups.Manage` | built |
| `ChangeGroupStatusCommand` | `VolunteerGroups.Manage` | built |
| `ApplyToGroupCommand` | `Members.Read` | built |
| `DecideApplicationCommand` | `VolunteerGroups.Lead` + is this group's president | built |
| `AssignRolePositionCommand` | `VolunteerGroups.Lead` + is this group's president | built |
| `RemoveGroupMemberCommand` | `VolunteerGroups.Lead` + is this group's president | built |
| `ChangeGroupPresidentCommand` | `VolunteerGroups.Manage` | built |

## Queries

| Query | Policy | Status |
|---|---|---|
| `ListGroupsQuery` | `Members.Read` | built |
| `GetGroupQuery` | `Members.Read` | built |
| `GetApplicationsQuery` | `VolunteerGroups.Lead` + is this group's president | built |

## Events published

| Event | Topic | Raised by |
|---|---|---|
| `GroupCreatedDomainEvent` | `volunteer-groups.group.created.v1` | `VolunteerGroup.Create` |
| `GroupApplicationSubmittedDomainEvent` | `volunteer-groups.application.submitted.v1` | `VolunteerGroup.Apply` |
| `GroupApplicationDecidedDomainEvent` | `volunteer-groups.application.decided.v1` | `VolunteerGroup.DecideApplication` |
| `GroupRolePositionAssignedDomainEvent` | `volunteer-groups.role-position.assigned.v1` | `VolunteerGroup.AssignRolePosition` |
| `GroupMemberRemovedDomainEvent` | `volunteer-groups.member.removed.v1` | `VolunteerGroup.RemoveMember` |
| `GroupPresidentChangedDomainEvent` | `volunteer-groups.president.changed.v1` | `VolunteerGroup.ChangePresident` |
| `GroupStatusChangedDomainEvent` | `volunteer-groups.group.status-changed.v1` | `VolunteerGroup.ChangeStatus` |

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
| PATCH | `/v1/volunteer-groups/groups/{id}/status` | `VolunteerGroups.Manage` |
| POST | `/v1/volunteer-groups/groups/{id}/applications` | `Members.Read` |
| GET | `/v1/volunteer-groups/groups/{id}/applications` | `VolunteerGroups.Lead` + president |
| POST | `/v1/volunteer-groups/groups/{id}/applications/{applicationId}/decide` | `VolunteerGroups.Lead` + president |
| PUT | `/v1/volunteer-groups/groups/{id}/members/{memberId}/position` | `VolunteerGroups.Lead` + president |
| DELETE | `/v1/volunteer-groups/groups/{id}/members/{memberId}` | `VolunteerGroups.Lead` + president |
| PATCH | `/v1/volunteer-groups/groups/{id}/president` | `VolunteerGroups.Manage` |
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

## Decisions worth knowing before you change this service

**Creating a group is the Samaaj's decision; who joins it is the president's.**
That asymmetry is the whole service. A Samaaj admin who is not a given group's
president cannot decide its applications, and gets the same answer a stranger
would.

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
- `Sangam.VolunteerGroups.IntegrationTests` — Testcontainers Postgres, no Kafka.
  The tenant query filter is applied by the DbContext rather than any handler,
  and the outbox guarantee is transactional; neither can be shown against a
  substituted repository. There is a named regression test for the permission
  split — `A_president_who_is_only_an_ordinary_member_can_still_run_their_group`.

```
dotnet test services/volunteer-groups-service/Sangam.VolunteerGroups.sln
```

`scripts/smoke-through-gateway.sh` covers the whole path through the gateway,
including a president who is an ordinary member deciding a real application.
