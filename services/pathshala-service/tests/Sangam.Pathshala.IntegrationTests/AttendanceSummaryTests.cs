using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Security;
using Sangam.Pathshala.Domain.Enrolments;
using Sangam.Pathshala.Infrastructure.Persistence;
using Xunit;

namespace Sangam.Pathshala.IntegrationTests;

/// <summary>
/// The admin Reports &amp; Analytics screen's tenant-wide attendance figure -
/// the same tally <c>TallyAttendanceAsync</c> computes per enrolment, summed
/// across every class in every Pathshala the tenant runs instead.
/// </summary>
public sealed class AttendanceSummaryTests(PathshalaApiFactory factory)
    : IClassFixture<PathshalaApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task MarkAsync(Guid tenantId, AttendanceStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PathshalaDbContext>();

        db.Attendance.Add(new AttendanceEntry(
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 3, 1),
            status,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();
    }

    private HttpClient AdminClient() =>
        factory.CreateClientAs(
            Guid.NewGuid(), TenantId, [Roles.SamaajAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.PathshalaManage]);

    [Fact]
    public async Task Tallies_present_absent_and_excused_across_every_class_in_the_tenant()
    {
        // Two different classes (different Guids each call) in the same
        // tenant - the point is that the tally is not scoped to one class or
        // one enrolment, unlike TallyAttendanceAsync.
        await MarkAsync(TenantId, AttendanceStatus.Present);
        await MarkAsync(TenantId, AttendanceStatus.Present);
        await MarkAsync(TenantId, AttendanceStatus.Absent);
        await MarkAsync(TenantId, AttendanceStatus.Excused);

        // A different Samaaj entirely - must not be counted here.
        await MarkAsync(OtherTenantId, AttendanceStatus.Present);

        var response = await AdminClient().GetAsync("/v1/pathshala/attendance-summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tally = await response.Content.ReadFromJsonAsync<AttendanceTally>();

        tally!.Present.Should().Be(2);
        tally.Absent.Should().Be(1);
        tally.Excused.Should().Be(1);
        tally.Percentage.Should().Be(67, "2 present of 3 present-or-absent, excused not counted against it");
    }

    [Fact]
    public async Task Reports_no_percentage_when_nothing_has_been_marked_yet()
    {
        var response = await AdminClient().GetAsync("/v1/pathshala/attendance-summary");

        var tally = await response.Content.ReadFromJsonAsync<AttendanceTally>();

        tally!.Recorded.Should().Be(0);
        tally.Percentage.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_a_plain_member()
    {
        var member = factory.CreateClientAs(
            Guid.NewGuid(), TenantId, [Roles.Member], [PermissionKeys.MembersRead]);

        var response = await member.GetAsync("/v1/pathshala/attendance-summary");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
