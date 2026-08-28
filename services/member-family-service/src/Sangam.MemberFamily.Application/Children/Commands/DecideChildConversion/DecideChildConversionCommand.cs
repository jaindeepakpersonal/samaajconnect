using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Children.Commands.DecideChildConversion;

/// <summary>
/// Approves or rejects a conversion request. Admin-approved is the decision on
/// record (DEVELOPMENT_PLAN.md, 2026-08-28): creating a platform login is not
/// something a household does unilaterally.
/// </summary>
[RequiresRoles(Roles.SamaajAdmin, Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.FamilyApproveConversion)]
public sealed record DecideChildConversionCommand(Guid RequestId, bool Approve, string? Note)
    : ICommand<ConversionRequestResponse>;
