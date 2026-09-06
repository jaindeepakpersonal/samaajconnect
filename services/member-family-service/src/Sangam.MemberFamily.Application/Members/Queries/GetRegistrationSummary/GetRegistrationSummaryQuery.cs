using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Members.Queries.GetRegistrationSummary;

/// <summary>
/// How registrations in this Samaaj are trending, for the admin dashboard's
/// Reports &amp; Analytics screen.
/// </summary>
/// <remarks>
/// The window is fixed at 30 days against the 30 days before that, rather than
/// a caller-supplied range: the wireframe draws one number ("+8.4% Registration
/// growth") with no control beside it, and a configurable window is a screen
/// nobody asked for standing behind a report nobody asked to configure.
/// </remarks>
[RequiresRoles(Roles.SamaajAdmin, Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetRegistrationSummaryQuery : IQuery<RegistrationSummaryResponse>;

/// <summary>
/// Registrations in the current 30-day window against the 30 days before it.
/// </summary>
public sealed record RegistrationSummaryResponse(int CurrentPeriodCount, int PreviousPeriodCount)
{
    /// <summary>
    /// Null when the previous period counted nothing. A percentage against
    /// zero is not a number, and zero would misreport growth from nothing as
    /// no growth at all - the same reasoning AttendanceTally's own Percentage
    /// already applies in pathshala-service.
    /// </summary>
    public double? GrowthPercentage =>
        PreviousPeriodCount == 0
            ? null
            : 100.0 * (CurrentPeriodCount - PreviousPeriodCount) / PreviousPeriodCount;
}

public sealed class GetRegistrationSummaryQueryHandler(
    IMemberProfileRepository profiles, IDateTimeProvider clock)
    : IRequestHandler<GetRegistrationSummaryQuery, Result<RegistrationSummaryResponse>>
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(30);

    public async Task<Result<RegistrationSummaryResponse>> Handle(
        GetRegistrationSummaryQuery query, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var currentStart = now - Window;
        var previousStart = currentStart - Window;

        var current = await profiles.CountCreatedBetweenAsync(currentStart, now, cancellationToken);
        var previous = await profiles.CountCreatedBetweenAsync(
            previousStart, currentStart, cancellationToken);

        return Result.Success(new RegistrationSummaryResponse(current, previous));
    }
}
