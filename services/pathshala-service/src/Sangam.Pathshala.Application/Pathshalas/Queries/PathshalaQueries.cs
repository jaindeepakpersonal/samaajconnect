using MediatR;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Common;
using Sangam.Pathshala.Application.Security;
using Sangam.Pathshala.Domain.Enrolments;

namespace Sangam.Pathshala.Application.Pathshalas.Queries;

/// <summary>The Samaaj's Pathshalas, as the directory card shows them.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListPathshalasQuery : IQuery<IReadOnlyList<PathshalaResponse>>;

public sealed class ListPathshalasQueryHandler(IPathshalaRepository pathshalas)
    : IRequestHandler<ListPathshalasQuery, Result<IReadOnlyList<PathshalaResponse>>>
{
    public async Task<Result<IReadOnlyList<PathshalaResponse>>> Handle(
        ListPathshalasQuery query, CancellationToken cancellationToken)
    {
        var found = await pathshalas.ListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<PathshalaResponse>>(
            [.. found.Select(p => p.ToResponse())]);
    }
}

/// <summary>One Pathshala with its sessions and classes.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetPathshalaQuery(Guid PathshalaId) : IQuery<PathshalaDetailResponse>;

public sealed class GetPathshalaQueryHandler(
    IPathshalaRepository pathshalas, IEnrolmentRepository enrolments, ITenantContext tenantContext)
    : IRequestHandler<GetPathshalaQuery, Result<PathshalaDetailResponse>>
{
    public async Task<Result<PathshalaDetailResponse>> Handle(
        GetPathshalaQuery query, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByIdAsync(query.PathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<PathshalaDetailResponse>(PathshalaAccess.NoSuchPathshala);
        }

        // Head counts per class, not rosters: this is a screen any member can
        // open, and a list of the children in each class is not something it
        // needs in order to say "8 classes, 126 students".
        var placed = await enrolments.ListForPathshalaAsync(
            pathshala!.Id, EnrolmentStatus.Active, cancellationToken);

        var byClass = placed
            .Where(e => e.ClassId is not null)
            .GroupBy(e => e.ClassId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return Result.Success(pathshala.ToDetail(byClass));
    }
}

/// <summary>
/// The requests waiting for somebody at the Pathshala to place.
/// </summary>
/// <remarks>
/// The queue the enrolment flow depends on. Without a screen for it, requests
/// sit unanswered and enrolment looks broken to the parent who made one.
/// </remarks>
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record ListEnrolmentRequestsQuery(Guid PathshalaId)
    : IQuery<IReadOnlyList<EnrolmentResponse>>;

public sealed class ListEnrolmentRequestsQueryHandler(
    IPathshalaRepository pathshalas, IEnrolmentRepository enrolments, ITenantContext tenantContext)
    : IRequestHandler<ListEnrolmentRequestsQuery, Result<IReadOnlyList<EnrolmentResponse>>>
{
    public async Task<Result<IReadOnlyList<EnrolmentResponse>>> Handle(
        ListEnrolmentRequestsQuery query, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByIdAsync(query.PathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<IReadOnlyList<EnrolmentResponse>>(
                PathshalaAccess.NoSuchPathshala);
        }

        var waiting = await enrolments.ListForPathshalaAsync(
            pathshala!.Id, EnrolmentStatus.Requested, cancellationToken);

        return Result.Success<IReadOnlyList<EnrolmentResponse>>(
            [.. waiting.OrderBy(e => e.RequestedAt).Select(e => e.ToResponse())]);
    }
}

/// <summary>
/// The class roll: who a teacher is about to mark.
/// </summary>
/// <remarks>
/// Enrolment ids and child profile ids, which is what the register submission
/// needs. Names come from member-family-service, as everywhere else here.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetClassRollQuery(Guid ClassId) : IQuery<IReadOnlyList<EnrolmentResponse>>;

public sealed class GetClassRollQueryHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
    : IRequestHandler<GetClassRollQuery, Result<IReadOnlyList<EnrolmentResponse>>>
{
    public async Task<Result<IReadOnlyList<EnrolmentResponse>>> Handle(
        GetClassRollQuery query, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByClassIdAsync(query.ClassId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<IReadOnlyList<EnrolmentResponse>>(PathshalaAccess.NoSuchClass);
        }

        var pathshalaClass = pathshala!.FindClass(query.ClassId)!;

        // A roll is a list of somebody's children. Staff only - and the check is
        // against this class, not against holding a teacher permission
        // somewhere.
        if (!PathshalaAccess.CanTeach(
                currentUser, pathshalaClass, PermissionKeys.PathshalaAttendanceWrite))
        {
            return Result.Failure<IReadOnlyList<EnrolmentResponse>>(PathshalaAccess.NoSuchClass);
        }

        var roll = await enrolments.ListForClassAsync(query.ClassId, cancellationToken);
        var sessionLabel = pathshala.FindSession(pathshalaClass.SessionId)?.Label;

        return Result.Success<IReadOnlyList<EnrolmentResponse>>(
            [.. roll.Select(e => e.ToResponse(pathshalaClass.Name, sessionLabel))]);
    }
}

/// <summary>Every enrolment this member asked for, or holds themselves.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListMyEnrolmentsQuery : IQuery<IReadOnlyList<EnrolmentResponse>>;

public sealed class ListMyEnrolmentsQueryHandler(
    IPathshalaRepository pathshalas, IEnrolmentRepository enrolments, ICurrentUser currentUser)
    : IRequestHandler<ListMyEnrolmentsQuery, Result<IReadOnlyList<EnrolmentResponse>>>
{
    public async Task<Result<IReadOnlyList<EnrolmentResponse>>> Handle(
        ListMyEnrolmentsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<EnrolmentResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var mine = await enrolments.ListForMemberAsync(memberId, cancellationToken);
        var results = new List<EnrolmentResponse>(mine.Count);

        foreach (var enrolment in mine)
        {
            var pathshala = await pathshalas.GetByIdAsync(enrolment.PathshalaId, cancellationToken);

            var pathshalaClass = enrolment.ClassId is { } classId
                ? pathshala?.FindClass(classId)
                : null;

            results.Add(enrolment.ToResponse(
                pathshalaClass?.Name,
                enrolment.SessionId is { } sessionId
                    ? pathshala?.FindSession(sessionId)?.Label
                    : null));
        }

        return Result.Success<IReadOnlyList<EnrolmentResponse>>(results);
    }
}
