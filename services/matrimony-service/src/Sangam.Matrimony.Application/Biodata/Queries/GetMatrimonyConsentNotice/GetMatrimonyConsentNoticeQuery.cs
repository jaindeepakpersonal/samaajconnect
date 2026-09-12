using MediatR;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;
using Sangam.Matrimony.Domain.Biodata;

namespace Sangam.Matrimony.Application.Biodata.Queries.GetMatrimonyConsentNotice;

/// <summary>What a member is shown before opting in - mirrors `GetChildDataNoticeQuery` exactly.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record GetMatrimonyConsentNoticeQuery : IQuery<MatrimonyConsentNoticeResponse>;

public sealed class GetMatrimonyConsentNoticeQueryHandler
    : IRequestHandler<GetMatrimonyConsentNoticeQuery, Result<MatrimonyConsentNoticeResponse>>
{
    public Task<Result<MatrimonyConsentNoticeResponse>> Handle(
        GetMatrimonyConsentNoticeQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new MatrimonyConsentNoticeResponse(
            MatrimonyConsentNotice.CurrentVersion,
            MatrimonyConsentNotice.Summary,
            MatrimonyConsentNotice.Attestation)));
}
