using MediatR;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Consents;

namespace Sangam.IdentityTenant.Application.Consents.Queries.GetConsentNotice;

/// <summary>
/// The notice a visitor must be shown before consenting. Anonymous because
/// DPDP section 5 requires the notice at or before the point of consent, and
/// that point is registration - before any account exists.
/// </summary>
[AllowAnonymousRequest]
public sealed record GetConsentNoticeQuery : IQuery<ConsentNoticeResponse>;

public sealed class GetConsentNoticeQueryHandler
    : IRequestHandler<GetConsentNoticeQuery, Result<ConsentNoticeResponse>>
{
    public Task<Result<ConsentNoticeResponse>> Handle(
        GetConsentNoticeQuery query,
        CancellationToken cancellationToken)
    {
        var items = ConsentNotice.Items
            .Select(item => new ConsentNoticeItemResponse(
                item.Purpose.ToString(), item.Title, item.Description, item.Required))
            .ToList();

        return Task.FromResult(Result.Success(
            new ConsentNoticeResponse(ConsentNotice.CurrentVersion, items)));
    }
}
