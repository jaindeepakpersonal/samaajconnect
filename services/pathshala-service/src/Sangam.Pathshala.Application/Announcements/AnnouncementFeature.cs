using FluentValidation;
using MediatR;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Common;
using Sangam.Pathshala.Application.Security;
using Sangam.Pathshala.Domain.Announcements;
using Sangam.Pathshala.Domain.Enrolments;

namespace Sangam.Pathshala.Application.Announcements;

// A Pathshala's messages to its parents. The text is stored and read here; each
// parent is told there is one to read by an in-app notification that
// audit-notification-service raises from an ids-only event - see
// PathshalaAnnouncement's own remarks.

public sealed record AnnouncementResponse(
    Guid Id,
    Guid PathshalaId,
    string PathshalaName,
    string Title,
    string Body,
    Guid SentBy,
    DateTimeOffset SentAt,
    int RecipientCount);

// ---- Send --------------------------------------------------------------------

/// <summary>
/// Sends a message to the parents of every child with a place or a request at
/// the Pathshala.
/// </summary>
/// <remarks>
/// <c>Members.Read</c> is only the outer gate - every administrator and teacher
/// account holds it. The data check is the real one: a teacher of this
/// Pathshala, or somebody who runs it (an Area Admin for their own locality's).
/// Anybody else is answered "not found".
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead, PermissionKeys.PathshalaTeach)]
public sealed record SendAnnouncementCommand(Guid PathshalaId, string Title, string Body)
    : ICommand<AnnouncementResponse>;

public sealed class SendAnnouncementCommandValidator : AbstractValidator<SendAnnouncementCommand>
{
    public SendAnnouncementCommandValidator()
    {
        RuleFor(x => x.PathshalaId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
    }
}

public sealed class SendAnnouncementCommandHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,
    IAnnouncementRepository announcements,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope,
    IDateTimeProvider clock)
    : IRequestHandler<SendAnnouncementCommand, Result<AnnouncementResponse>>
{
    public async Task<Result<AnnouncementResponse>> Handle(
        SendAnnouncementCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } senderId)
        {
            return Result.Failure<AnnouncementResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var pathshala = await pathshalas.GetByIdAsync(command.PathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId)
            || PathshalaAccess.StaffScopeOf(currentUser, areaScope, pathshala!) is null)
        {
            return Result.Failure<AnnouncementResponse>(PathshalaAccess.NoSuchPathshala);
        }

        // The parent who asked for each place, and the student themselves once
        // conversion has given them an account - the same people who may read
        // that child's records.
        var current = await enrolments.ListForPathshalaAsync(pathshala!.Id, status: null, cancellationToken);
        var recipients = current
            .Where(e => e.Status is EnrolmentStatus.Active or EnrolmentStatus.Requested)
            .SelectMany(e => e.StudentUserId is { } student
                ? new[] { e.RequestedByMemberId, student }
                : [e.RequestedByMemberId])
            .Distinct()
            .ToList();

        if (recipients.Count == 0)
        {
            return Result.Failure<AnnouncementResponse>(Error.Conflict(
                "Announcement.NoParents",
                "No child has a place or a request at this Pathshala yet, so there is nobody to send it to."));
        }

        var announcement = PathshalaAnnouncement.Send(
            pathshala.TenantId, pathshala.Id, command.Title, command.Body, recipients, senderId, clock.UtcNow);

        announcements.Add(announcement);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(announcement.ToResponse(pathshala.Name));
    }
}

// ---- Read, as staff ----------------------------------------------------------

/// <summary>What this Pathshala has sent, newest first - for its administrators and teachers.</summary>
[RequiresPermission(PermissionKeys.MembersRead, PermissionKeys.PathshalaTeach)]
public sealed record ListAnnouncementsQuery(Guid PathshalaId) : IQuery<IReadOnlyList<AnnouncementResponse>>;

public sealed class ListAnnouncementsQueryHandler(
    IPathshalaRepository pathshalas,
    IAnnouncementRepository announcements,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope)
    : IRequestHandler<ListAnnouncementsQuery, Result<IReadOnlyList<AnnouncementResponse>>>
{
    public async Task<Result<IReadOnlyList<AnnouncementResponse>>> Handle(
        ListAnnouncementsQuery query, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByIdAsync(query.PathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId)
            || PathshalaAccess.StaffScopeOf(currentUser, areaScope, pathshala!) is null)
        {
            return Result.Failure<IReadOnlyList<AnnouncementResponse>>(PathshalaAccess.NoSuchPathshala);
        }

        var sent = await announcements.ListForPathshalasAsync([pathshala!.Id], cancellationToken);

        return Result.Success<IReadOnlyList<AnnouncementResponse>>(
            [.. sent.Select(a => a.ToResponse(pathshala.Name))]);
    }
}

// ---- Read, as a parent -------------------------------------------------------

/// <summary>
/// Announcements from every Pathshala where this member has a child with a place
/// or a request - or is the student. Newest first.
/// </summary>
/// <remarks>
/// Decided against the enrolments, not a stored recipient list: a parent who
/// asks for a place today sees what that Pathshala already said, which is what
/// they would want to have been told.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListMyAnnouncementsQuery : IQuery<IReadOnlyList<AnnouncementResponse>>;

public sealed class ListMyAnnouncementsQueryHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,
    IAnnouncementRepository announcements,
    ICurrentUser currentUser)
    : IRequestHandler<ListMyAnnouncementsQuery, Result<IReadOnlyList<AnnouncementResponse>>>
{
    public async Task<Result<IReadOnlyList<AnnouncementResponse>>> Handle(
        ListMyAnnouncementsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<AnnouncementResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var mine = await enrolments.ListForMemberAsync(memberId, cancellationToken);
        var pathshalaIds = mine
            .Where(e => e.Status is EnrolmentStatus.Active or EnrolmentStatus.Requested)
            .Select(e => e.PathshalaId)
            .Distinct()
            .ToList();

        if (pathshalaIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<AnnouncementResponse>>([]);
        }

        var names = (await pathshalas.ListAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
        var sent = await announcements.ListForPathshalasAsync(pathshalaIds, cancellationToken);

        return Result.Success<IReadOnlyList<AnnouncementResponse>>(
            [.. sent.Select(a => a.ToResponse(names.GetValueOrDefault(a.PathshalaId) ?? "Pathshala"))]);
    }
}

internal static class AnnouncementMappings
{
    public static AnnouncementResponse ToResponse(this PathshalaAnnouncement announcement, string pathshalaName) =>
        new(announcement.Id,
            announcement.PathshalaId,
            pathshalaName,
            announcement.Title,
            announcement.Body,
            announcement.SentBy,
            announcement.SentAt,
            announcement.RecipientCount);
}
