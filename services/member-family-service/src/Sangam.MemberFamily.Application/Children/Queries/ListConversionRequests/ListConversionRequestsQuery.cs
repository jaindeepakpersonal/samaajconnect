using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Children.Queries.ListConversionRequests;

/// <summary>The Samaaj's queue of conversion requests awaiting a decision.</summary>
[RequiresRoles(Roles.SamaajAdmin, Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.FamilyApproveConversion)]
public sealed record ListConversionRequestsQuery : IQuery<IReadOnlyList<ConversionRequestResponse>>;
