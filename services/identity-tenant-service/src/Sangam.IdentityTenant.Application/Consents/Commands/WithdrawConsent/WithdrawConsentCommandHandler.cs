using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Consents;

namespace Sangam.IdentityTenant.Application.Consents.Commands.WithdrawConsent;

public sealed class WithdrawConsentCommandHandler(
    IConsentRepository consents,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<WithdrawConsentCommand, Result<IReadOnlyList<ConsentStateResponse>>>
{
    public async Task<Result<IReadOnlyList<ConsentStateResponse>>> Handle(
        WithdrawConsentCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<IReadOnlyList<ConsentStateResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        if (!Enum.TryParse<ConsentPurpose>(command.Purpose, ignoreCase: true, out var purpose))
        {
            return Result.Failure<IReadOnlyList<ConsentStateResponse>>(
                Error.NotFound("Consent.UnknownPurpose", "No such consent purpose."));
        }

        if (ConsentPurposes.Required.Contains(purpose))
        {
            // Withdrawing this would leave an account with no basis to exist.
            // The right answer is erasure, which is a different request.
            return Result.Failure<IReadOnlyList<ConsentStateResponse>>(Error.Conflict(
                "Consent.Required",
                "This consent is what your membership rests on. To withdraw it, ask your "
                + "Samaaj administrator to erase your account."));
        }

        var user = await users.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<IReadOnlyList<ConsentStateResponse>>(
                Error.NotFound("User.NotFound", "This account no longer exists."));
        }

        consents.Add(ConsentRecord.Withdraw(
            user.TenantId, user.Id, purpose, "MemberPortal", clock.UtcNow));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var history = await consents.ListForUserAsync(user.Id, cancellationToken);

        return Result.Success(ConsentState.From(history));
    }
}
