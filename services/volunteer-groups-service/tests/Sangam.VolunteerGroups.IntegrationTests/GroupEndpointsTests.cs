using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Groups;
using Xunit;

namespace Sangam.VolunteerGroups.IntegrationTests;

/// <summary>
/// Volunteer groups through their endpoints, against a real database.
/// </summary>
/// <remarks>
/// The tenant query filter is applied by the DbContext rather than by any
/// handler, and the outbox guarantee is transactional. Neither can be shown
/// against a substituted repository.
/// </remarks>
public sealed class GroupEndpointsTests(VolunteerGroupsApiFactory factory)
    : IClassFixture<VolunteerGroupsApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid PresidentId = Guid.NewGuid();
    private static readonly Guid ApplicantId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// An ordinary member. Holds VolunteerGroups.Lead, as every member does -
    /// which grants nothing until they are actually a group's president.
    /// </summary>
    private HttpClient Member(Guid? userId = null, Guid? tenantId = null) =>
        factory.CreateClientAs(
            userId ?? ApplicantId,
            tenantId ?? TenantId,
            [Roles.Member],
            [PermissionKeys.MembersRead, PermissionKeys.VolunteerGroupsLead]);

    /// <summary>
    /// A Samaaj admin: creates groups and deactivates them. Whether they run a
    /// *given* group is data, and they are not this one's president unless the
    /// test makes them one.
    /// </summary>
    private HttpClient Manager(Guid? userId = null, Guid? tenantId = null) =>
        factory.CreateClientAs(
            userId ?? PresidentId,
            tenantId ?? TenantId,
            [Roles.SamaajAdmin],
            [
                PermissionKeys.MembersRead,
                PermissionKeys.VolunteerGroupsManage,
                PermissionKeys.VolunteerGroupsLead,
            ]);

    private static object NewGroup(string name = "Seva Group", Guid? president = null) => new
    {
        name,
        description = "Food drives and blood donation camps.",
        focusArea = "Social Service",
        presidentMemberId = president ?? PresidentId,
    };

    private async Task<Guid> CreateGroupAsync(string name = "Seva Group", Guid? president = null)
    {
        var response = await Manager().PostAsJsonAsync(
            "/v1/volunteer-groups/groups", NewGroup(name, president));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> ApplyAsync(HttpClient client, Guid groupId) =>
        client.PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{groupId}/applications",
            new { note = "Happy to help at weekends." });

    private async Task<Guid> PendingApplicationIdAsync(Guid groupId)
    {
        var queue = await Manager().GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{groupId}/applications");

        return queue.EnumerateArray().First().GetProperty("id").GetGuid();
    }

    // ---- Creating ---------------------------------------------------------

    [Fact]
    public async Task Creating_a_group_persists_it_and_writes_one_outbox_row_in_the_same_transaction()
    {
        var id = await CreateGroupAsync();

        var persisted = await factory.WithDbContextAsync(db =>
            db.Groups.IgnoreQueryFilters().Include(g => g.Members).AsNoTracking()
                .SingleAsync(g => g.Id == id));

        persisted.TenantId.Should().Be(TenantId);
        persisted.Status.Should().Be(GroupStatus.Active);
        persisted.Members.Should().ContainSingle().Which.MemberId.Should().Be(PresidentId);

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().ToListAsync());

        outbox.Should().ContainSingle();
        outbox[0].Topic.Should().Be("volunteer-groups.group.created.v1");
    }

    [Fact]
    public async Task A_member_cannot_create_a_group()
    {
        (await Member().PostAsJsonAsync("/v1/volunteer-groups/groups", NewGroup()))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_group_with_no_president_is_refused()
    {
        var response = await Manager().PostAsJsonAsync("/v1/volunteer-groups/groups", new
        {
            name = "Orphan Group",
            description = (string?)null,
            focusArea = (string?)null,
            presidentMemberId = Guid.Empty,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Two_groups_cannot_share_a_name_in_one_Samaaj()
    {
        // Nobody can tell which one they applied to.
        await CreateGroupAsync();

        var second = await Manager().PostAsJsonAsync("/v1/volunteer-groups/groups", NewGroup());

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task But_two_Samaaj_can_each_have_one()
    {
        await CreateGroupAsync();

        var elsewhere = await Manager(userId: Guid.NewGuid(), tenantId: OtherTenantId)
            .PostAsJsonAsync("/v1/volunteer-groups/groups", NewGroup());

        elsewhere.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---- Listing ----------------------------------------------------------

    [Fact]
    public async Task The_list_does_not_cross_Samaaj()
    {
        // The global query filter, applied by the DbContext and not by any
        // handler - so only a real database shows it working.
        await CreateGroupAsync();

        var elsewhere = await Member(userId: Guid.NewGuid(), tenantId: OtherTenantId)
            .GetFromJsonAsync<JsonElement>("/v1/volunteer-groups/groups");

        elsewhere.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task A_row_tells_the_asking_member_where_they_stand()
    {
        // What the wireframe's "View / Apply" button needs to know.
        var id = await CreateGroupAsync();

        var before = await Member().GetFromJsonAsync<JsonElement>("/v1/volunteer-groups/groups");
        var beforeRow = before.EnumerateArray().Single();

        beforeRow.GetProperty("iAmAMember").GetBoolean().Should().BeFalse();
        beforeRow.GetProperty("myApplicationStatus").ValueKind.Should().Be(JsonValueKind.Null);

        await ApplyAsync(Member(), id);

        var after = await Member().GetFromJsonAsync<JsonElement>("/v1/volunteer-groups/groups");

        after.EnumerateArray().Single()
            .GetProperty("myApplicationStatus").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task Only_the_president_is_told_how_many_are_waiting()
    {
        // To anyone else it is a fact about other members' pending requests.
        var id = await CreateGroupAsync();
        await ApplyAsync(Member(), id);

        var asPresident = await Manager().GetFromJsonAsync<JsonElement>("/v1/volunteer-groups/groups");
        var asMember = await Member().GetFromJsonAsync<JsonElement>("/v1/volunteer-groups/groups");

        asPresident.EnumerateArray().Single()
            .GetProperty("pendingApplicationCount").GetInt32().Should().Be(1);
        asMember.EnumerateArray().Single()
            .GetProperty("pendingApplicationCount").GetInt32().Should().Be(0);
    }

    // ---- Applying ---------------------------------------------------------

    [Fact]
    public async Task Applying_writes_an_event_that_does_not_carry_the_note()
    {
        // It is what a member wrote about themselves, for the president who has
        // to read it - not for a service that stores payloads verbatim.
        var id = await CreateGroupAsync();

        (await ApplyAsync(Member(), id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking()
                .Where(m => m.Topic == "volunteer-groups.application.submitted.v1")
                .ToListAsync());

        outbox.Should().ContainSingle();
        outbox[0].Payload.Should().NotContain("weekends");
    }

    [Fact]
    public async Task Applying_twice_looks_exactly_like_applying_once()
    {
        var id = await CreateGroupAsync();

        await ApplyAsync(Member(), id);
        var second = await ApplyAsync(Member(), id);

        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var applications = await factory.WithDbContextAsync(db =>
            db.Applications.IgnoreQueryFilters().CountAsync(a => a.GroupId == id));

        applications.Should().Be(1);
    }

    [Fact]
    public async Task A_group_in_another_Samaaj_cannot_be_applied_to_even_with_its_id()
    {
        // The IDOR guard. Knowing the id is not access.
        var id = await CreateGroupAsync();

        var response = await ApplyAsync(
            Member(userId: Guid.NewGuid(), tenantId: OtherTenantId), id);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Deciding ---------------------------------------------------------

    [Fact]
    public async Task The_president_accepts_and_the_applicant_becomes_a_member()
    {
        var id = await CreateGroupAsync();
        await ApplyAsync(Member(), id);
        var applicationId = await PendingApplicationIdAsync(id);

        var response = await Manager().PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{id}/applications/{applicationId}/decide",
            new { accept = true, rolePosition = "Coordinator" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await Member().GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{id}");

        detail.GetProperty("group").GetProperty("iAmAMember").GetBoolean().Should().BeTrue();
        detail.GetProperty("members").EnumerateArray()
            .Select(m => m.GetProperty("rolePosition").GetString())
            .Should().Contain("Coordinator");
    }

    [Fact]
    public async Task Somebody_who_is_not_this_group_s_president_cannot_decide_its_applications()
    {
        // The permission is the outer gate; the presidency is the inner one, and
        // a Samaaj admin who does not run this group is stopped by it.
        //
        // "Not found", not "forbidden", and the same answer the queue gives: a
        // 403 would confirm that this group and this application both exist.
        var id = await CreateGroupAsync();
        await ApplyAsync(Member(), id);
        var applicationId = await PendingApplicationIdAsync(id);

        var response = await Manager(userId: Guid.NewGuid()).PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{id}/applications/{applicationId}/decide",
            new { accept = true, rolePosition = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deciding_the_same_application_twice_is_refused()
    {
        var id = await CreateGroupAsync();
        await ApplyAsync(Member(), id);
        var applicationId = await PendingApplicationIdAsync(id);

        var url = $"/v1/volunteer-groups/groups/{id}/applications/{applicationId}/decide";

        await Manager().PostAsJsonAsync(url, new { accept = true, rolePosition = (string?)null });

        var again = await Manager().PostAsJsonAsync(
            url, new { accept = false, rolePosition = (string?)null });

        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- The queue --------------------------------------------------------

    [Fact]
    public async Task The_queue_is_the_president_s_alone()
    {
        // Everyone holds VolunteerGroups.Lead, so everyone passes the outer
        // gate and is stopped by the inner one - which answers "not found"
        // rather than "forbidden", because whether a group has applications
        // waiting is itself the president's business. A member and a Samaaj
        // admin who does not run this group get the same answer, which is the
        // point.
        var id = await CreateGroupAsync();
        await ApplyAsync(Member(), id);

        (await Member().GetAsync($"/v1/volunteer-groups/groups/{id}/applications"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await Manager(userId: Guid.NewGuid()).GetAsync(
                $"/v1/volunteer-groups/groups/{id}/applications"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_president_who_is_only_an_ordinary_member_can_still_run_their_group()
    {
        // The bug this permission split exists to fix. A Samaaj admin names a
        // president; that president is usually not an admin, and gating the
        // president-side operations on VolunteerGroups.Manage meant they could
        // not decide their own group's applications.
        var president = Guid.NewGuid();
        var id = await CreateGroupAsync(president: president);

        await ApplyAsync(Member(), id);

        var asPresident = Member(userId: president);

        var queue = await asPresident.GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{id}/applications");

        var applicationId = queue.EnumerateArray().Single().GetProperty("id").GetGuid();

        var decided = await asPresident.PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{id}/applications/{applicationId}/decide",
            new { accept = true, rolePosition = "Coordinator" });

        decided.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_decided_application_leaves_the_queue()
    {
        var id = await CreateGroupAsync();
        await ApplyAsync(Member(), id);
        var applicationId = await PendingApplicationIdAsync(id);

        await Manager().PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{id}/applications/{applicationId}/decide",
            new { accept = false, rolePosition = (string?)null });

        var queue = await Manager().GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{id}/applications");

        queue.EnumerateArray().Should().BeEmpty();
    }

    // ---- Positions and status --------------------------------------------

    [Fact]
    public async Task A_president_assigns_a_position_and_a_member_cannot()
    {
        var id = await CreateGroupAsync();
        await ApplyAsync(Member(), id);
        var applicationId = await PendingApplicationIdAsync(id);

        await Manager().PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{id}/applications/{applicationId}/decide",
            new { accept = true, rolePosition = (string?)null });

        var url = $"/v1/volunteer-groups/groups/{id}/members/{ApplicantId}/position";

        (await Manager().PutAsJsonAsync(url, new { rolePosition = "Secretary" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await Member().PutAsJsonAsync(url, new { rolePosition = "President" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deactivating_a_group_stops_new_applications_and_keeps_its_members()
    {
        var id = await CreateGroupAsync();

        (await Manager().PatchAsJsonAsync(
                $"/v1/volunteer-groups/groups/{id}/status", new { status = "Inactive" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var applied = await ApplyAsync(Member(), id);
        var body = await applied.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("applied").GetBoolean().Should().BeFalse();

        var detail = await Member().GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{id}");

        detail.GetProperty("members").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_nonsense_status_is_refused()
    {
        var id = await CreateGroupAsync();

        var response = await Manager().PatchAsJsonAsync(
            $"/v1/volunteer-groups/groups/{id}/status", new { status = "Dormant" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Removing a member -------------------------------------------------
    //
    // VolunteerGroup.RemoveMember existed, was unit-tested at the domain level,
    // and was called from nowhere - a president could accept an application and
    // give somebody a position, with no way to undo either.

    private async Task<Guid> AcceptedMemberIdAsync(Guid groupId, Guid? memberId = null)
    {
        var member = memberId ?? ApplicantId;
        await ApplyAsync(Member(member), groupId);
        var applicationId = await PendingApplicationIdAsync(groupId);

        await Manager().PostAsJsonAsync(
            $"/v1/volunteer-groups/groups/{groupId}/applications/{applicationId}/decide",
            new { accept = true, rolePosition = (string?)null });

        return member;
    }

    [Fact]
    public async Task The_president_removes_a_member()
    {
        var id = await CreateGroupAsync();
        var memberId = await AcceptedMemberIdAsync(id);

        var response = await Manager().DeleteAsync(
            $"/v1/volunteer-groups/groups/{id}/members/{memberId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await Manager().GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{id}");

        detail.GetProperty("members").EnumerateArray()
            .Select(m => m.GetProperty("memberId").GetGuid())
            .Should().NotContain(memberId);
    }

    [Fact]
    public async Task It_does_not_erase_that_they_were_ever_accepted()
    {
        var id = await CreateGroupAsync();
        var memberId = await AcceptedMemberIdAsync(id);

        await Manager().DeleteAsync($"/v1/volunteer-groups/groups/{id}/members/{memberId}");

        var applications = await Manager().GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{id}/applications?pendingOnly=false");

        applications.EnumerateArray()
            .Select(a => a.GetProperty("memberId").GetGuid())
            .Should().Contain(memberId);
    }

    [Fact]
    public async Task The_president_cannot_be_removed_through_the_endpoint_either()
    {
        var id = await CreateGroupAsync();

        var response = await Manager().DeleteAsync(
            $"/v1/volunteer-groups/groups/{id}/members/{PresidentId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Somebody_who_is_not_this_group_s_president_cannot_remove_anybody()
    {
        var id = await CreateGroupAsync();
        var memberId = await AcceptedMemberIdAsync(id);

        var response = await Manager(userId: Guid.NewGuid()).DeleteAsync(
            $"/v1/volunteer-groups/groups/{id}/members/{memberId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Removing_somebody_not_in_the_group_says_so()
    {
        var id = await CreateGroupAsync();

        var response = await Manager().DeleteAsync(
            $"/v1/volunteer-groups/groups/{id}/members/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Removing_a_member_reaches_the_outbox_naming_who_removed_them()
    {
        var id = await CreateGroupAsync();
        var memberId = await AcceptedMemberIdAsync(id);

        await Manager().DeleteAsync($"/v1/volunteer-groups/groups/{id}/members/{memberId}");

        var message = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking()
                .Where(m => m.Topic == "volunteer-groups.member.removed.v1")
                .SingleOrDefaultAsync());

        message.Should().NotBeNull();
        message!.Payload.Should().Contain($"\"removedBy\": \"{PresidentId}\"");
    }

    // ---- Changing a group's president --------------------------------------
    //
    // VolunteerGroup.ChangePresident existed and was called from nowhere -
    // GroupPresidentChangedDomainEvent has sat in this service's own CLAUDE.md
    // "Raised by" column since it was written, naming a method nothing called.

    [Fact]
    public async Task A_Samaaj_admin_hands_the_group_to_a_different_president()
    {
        var id = await CreateGroupAsync();
        var successor = Guid.NewGuid();

        var response = await Manager().PatchAsJsonAsync(
            $"/v1/volunteer-groups/groups/{id}/president",
            new { newPresidentMemberId = successor });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await Manager(userId: successor).GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{id}");

        detail.GetProperty("group").GetProperty("presidentMemberId").GetGuid()
            .Should().Be(successor);
    }

    [Fact]
    public async Task The_outgoing_president_stays_in_the_group_as_an_ordinary_member()
    {
        var id = await CreateGroupAsync();

        await Manager().PatchAsJsonAsync(
            $"/v1/volunteer-groups/groups/{id}/president",
            new { newPresidentMemberId = Guid.NewGuid() });

        var detail = await Manager().GetFromJsonAsync<JsonElement>(
            $"/v1/volunteer-groups/groups/{id}");

        var outgoing = detail.GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("memberId").GetGuid() == PresidentId);

        outgoing.GetProperty("rolePosition").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_president_cannot_hand_the_group_to_somebody_themselves()
    {
        // VolunteerGroups.Manage is a Samaaj admin's permission, not the
        // president's own - the same split as deactivating a group.
        var id = await CreateGroupAsync();

        var response = await Member(PresidentId).PatchAsJsonAsync(
            $"/v1/volunteer-groups/groups/{id}/president",
            new { newPresidentMemberId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_group_in_another_Samaaj_cannot_have_its_president_changed()
    {
        var id = await CreateGroupAsync();

        var response = await Manager(tenantId: OtherTenantId).PatchAsJsonAsync(
            $"/v1/volunteer-groups/groups/{id}/president",
            new { newPresidentMemberId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
