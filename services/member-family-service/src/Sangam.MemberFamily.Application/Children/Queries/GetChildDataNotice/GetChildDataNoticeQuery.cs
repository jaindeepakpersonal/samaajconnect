using MediatR;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;
using Sangam.MemberFamily.Domain.Children;

namespace Sangam.MemberFamily.Application.Children.Queries.GetChildDataNotice;

/// <summary>
/// What a parent must be shown before a child's record is created
/// (DPDP sections 5 and 9).
/// </summary>
[RequiresRoles(Roles.Member, Roles.FamilyHead, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record GetChildDataNoticeQuery : IQuery<ChildDataNoticeResponse>;

public sealed record ChildDataNoticeResponse(string Version, string Summary, string Attestation);

public sealed class GetChildDataNoticeQueryHandler
    : IRequestHandler<GetChildDataNoticeQuery, Result<ChildDataNoticeResponse>>
{
    public Task<Result<ChildDataNoticeResponse>> Handle(
        GetChildDataNoticeQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new ChildDataNoticeResponse(
            ChildDataNotice.CurrentVersion,
            ChildDataNotice.Summary,
            ChildDataNotice.Attestation)));
}
