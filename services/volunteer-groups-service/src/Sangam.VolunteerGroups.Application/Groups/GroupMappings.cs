using Sangam.VolunteerGroups.Domain.Groups;

namespace Sangam.VolunteerGroups.Application.Groups;

/// <summary>
/// The one place a group becomes a response, so there is one place to check
/// that nothing leaks out of it.
/// </summary>
internal static class GroupMappings
{
    public static GroupResponse ToResponse(
        this VolunteerGroup group, Guid viewerId, bool viewerIsGroupAdmin = false) => new(
        group.Id,
        group.Name,
        group.Description,
        group.FocusArea,
        group.LocalityId,
        group.PresidentMemberId,
        group.Status.ToString(),
        group.Members.Count,

        // Only whoever can decide them is told how many people are waiting. To
        // anyone else it is a fact about other members' pending requests.
        group.CanLead(viewerId, viewerIsGroupAdmin)
            ? group.Applications.Count(a => a.Status == ApplicationStatus.Pending)
            : 0,
        group.Applications
            .FirstOrDefault(a => a.MemberId == viewerId)?.Status.ToString(),
        group.HasMember(viewerId),
        group.IsPresident(viewerId),
        group.CanLead(viewerId, viewerIsGroupAdmin),
        group.CreatedAt);

    public static GroupDetailResponse ToDetail(
        this VolunteerGroup group, Guid viewerId, bool viewerIsGroupAdmin = false) => new(
        group.ToResponse(viewerId, viewerIsGroupAdmin),
        [.. group.Members
            .OrderBy(m => m.JoinedAt)
            .Select(m => new GroupMemberResponse(m.MemberId, m.RolePosition, m.JoinedAt))]);

    public static GroupApplicationResponse ToResponse(this GroupApplication application) => new(
        application.Id,
        application.MemberId,
        application.Note,
        application.Status.ToString(),
        application.DecidedBy,
        application.DecidedAt,
        application.CreatedAt);
}
