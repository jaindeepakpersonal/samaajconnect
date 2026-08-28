using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Families.Commands.CreateFamily;

/// <summary>Creates a household with the caller as its head.</summary>
[RequiresRoles(Roles.Member, Roles.FamilyHead, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record CreateFamilyCommand : ICommand<FamilyResponse>;
