using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Children.Commands.RequestChildConversion;

/// <summary>
/// Asks for a child who has turned 18 to be given a member account of their
/// own. A Samaaj admin decides; nothing is created here.
/// </summary>
[RequiresRoles(Roles.Member, Roles.FamilyHead, Roles.SamaajAdmin, Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.FamilyWrite)]
public sealed record RequestChildConversionCommand(Guid ChildProfileId, string MobileOrEmail)
    : ICommand<ConversionRequestResponse>;
