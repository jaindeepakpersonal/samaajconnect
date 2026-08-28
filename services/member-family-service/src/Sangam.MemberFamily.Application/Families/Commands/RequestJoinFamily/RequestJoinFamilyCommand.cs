using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Families.Commands.RequestJoinFamily;

/// <summary>Asks to join a household, quoting the code its head shared.</summary>
[RequiresRoles(Roles.Member, Roles.FamilyHead, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record RequestJoinFamilyCommand(string FamilyCode, string Relationship)
    : ICommand<FamilyResponse>;
