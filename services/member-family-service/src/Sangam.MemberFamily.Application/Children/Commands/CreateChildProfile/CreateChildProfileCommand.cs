using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Children.Commands.CreateChildProfile;

/// <summary>
/// Adds a child to the caller's household. The handler checks the caller heads
/// that family - the role gate cannot know which family is meant.
/// </summary>
[RequiresRoles(Roles.Member, Roles.FamilyHead, Roles.SamaajAdmin, Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.FamilyWrite)]
public sealed record CreateChildProfileCommand(
    string FullName,
    DateOnly DateOfBirth,
    string? Gender,
    string? PhotoUrl) : ICommand<ChildResponse>;
