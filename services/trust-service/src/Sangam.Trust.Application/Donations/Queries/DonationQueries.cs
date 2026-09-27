using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Donations;

namespace Sangam.Trust.Application.Donations.Queries;

/// <summary>One Trust's donations, optionally narrowed to one status, newest first.</summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record ListDonationsQuery(Guid TrustId, DonationStatus? Status)
    : IQuery<IReadOnlyList<DonationResponse>>;

public sealed class ListDonationsQueryHandler(IDonationRepository donations)
    : IRequestHandler<ListDonationsQuery, Result<IReadOnlyList<DonationResponse>>>
{
    public async Task<Result<IReadOnlyList<DonationResponse>>> Handle(
        ListDonationsQuery query, CancellationToken cancellationToken)
    {
        var found = await donations.ListByTrustAsync(query.TrustId, query.Status, cancellationToken);

        return Result.Success<IReadOnlyList<DonationResponse>>([.. found.Select(d => d.ToResponse())]);
    }
}
