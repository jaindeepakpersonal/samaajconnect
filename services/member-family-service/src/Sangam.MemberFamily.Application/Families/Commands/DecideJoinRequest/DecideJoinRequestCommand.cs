using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Families.Commands.DecideJoinRequest;

/// <summary>
/// Accepts or rejects someone's request to join. Only the head of that family
/// may decide, which the handler checks - the role gate here cannot know which
/// family is being decided about.
/// </summary>
[RequiresRoles(Roles.Member, Roles.FamilyHead, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record DecideJoinRequestCommand(Guid FamilyId, Guid RequestId, bool Accept)
    : ICommand<FamilyResponse>;
