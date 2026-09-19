using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Families.Commands.SetFamilyHead;

/// <summary>
/// Names the household's head - conventionally its eldest member - or clears
/// it. Only the family admin may, which the handler checks.
/// </summary>
/// <remarks>
/// The member is a <c>string</c> on the wire and parsed only in the handler:
/// a value that fails to parse as a <c>Guid</c> would crash the JSON model
/// binder with an unhandled 400 before validation ran, the same trap this
/// platform's other body-bound ids already avoid. Null or empty clears.
/// </remarks>
[RequiresRoles(Roles.Member, Roles.FamilyHead, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record SetFamilyHeadCommand(string? MemberProfileId) : ICommand<FamilyResponse>;
