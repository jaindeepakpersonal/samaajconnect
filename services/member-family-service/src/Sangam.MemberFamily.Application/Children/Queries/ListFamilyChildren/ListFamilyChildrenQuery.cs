using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Children.Queries.ListFamilyChildren;

/// <summary>The children in the caller's own household.</summary>
[RequiresRoles(Roles.Member, Roles.FamilyHead, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record ListFamilyChildrenQuery : IQuery<IReadOnlyList<ChildResponse>>;
