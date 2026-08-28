using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;

namespace Sangam.MemberFamily.Application.Children.Commands.DecideChildConversion;

public sealed class DecideChildConversionCommandHandler(
    IChildConversionRepository conversions,
    IChildRepository children,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<DecideChildConversionCommand, Result<ConversionRequestResponse>>
{
    public async Task<Result<ConversionRequestResponse>> Handle(
        DecideChildConversionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } adminId)
        {
            return Result.Failure<ConversionRequestResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var request = await conversions.GetByIdAsync(command.RequestId, cancellationToken);

        if (request is null)
        {
            return Result.Failure<ConversionRequestResponse>(
                Error.NotFound("Conversion.NotFound", "No such conversion request."));
        }

        // IDOR guard on the write path.
        if (tenantContext.TenantId is { } tenantId && request.TenantId != tenantId)
        {
            return Result.Failure<ConversionRequestResponse>(
                Error.NotFound("Conversion.NotFound", "No such conversion request."));
        }

        var child = await children.GetByIdAsync(request.ChildProfileId, cancellationToken);

        if (child is null)
        {
            return Result.Failure<ConversionRequestResponse>(
                Error.NotFound("Child.NotFound", "The child this request refers to no longer exists."));
        }

        var decided = command.Approve
            ? request.Approve(adminId, command.Note, clock.UtcNow, child.FullName)
            : request.Reject(adminId, command.Note, clock.UtcNow);

        if (!decided)
        {
            return Result.Failure<ConversionRequestResponse>(Error.Conflict(
                "Conversion.AlreadyDecided", "This request has already been decided."));
        }

        // The child is *not* marked Converted here. The login does not exist
        // until identity-tenant-service has consumed the approval event and
        // created it; saying otherwise would leave a child record claiming an
        // account nobody can sign in to.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(request.ToResponse(child.FullName));
    }
}
