using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Families.Queries.GetMyFamily;

/// <summary>The caller's household, if they have one.</summary>
[RequiresRoles(Roles.Member, Roles.FamilyHead, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record GetMyFamilyQuery : IQuery<FamilyResponse>;
