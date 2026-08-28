using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Domain.Children;

namespace Sangam.MemberFamily.Application.Children.Commands.RequestChildConversion;

public sealed class RequestChildConversionCommandHandler(
    IChildRepository children,
    IChildConversionRepository conversions,
    IFamilyRepository families,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RequestChildConversionCommand, Result<ConversionRequestResponse>>
{
    public async Task<Result<ConversionRequestResponse>> Handle(
        RequestChildConversionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<ConversionRequestResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var child = await children.GetByIdAsync(command.ChildProfileId, cancellationToken);

        if (child is null)
        {
            return Result.Failure<ConversionRequestResponse>(
                Error.NotFound("Child.NotFound", "No such child in this Samaaj."));
        }

        // IDOR guard on the write path, re-checked rather than left to the
        // query filter (SECURITY-CHECKLIST.md).
        if (tenantContext.TenantId is { } tenantId && child.TenantId != tenantId)
        {
            return Result.Failure<ConversionRequestResponse>(
                Error.NotFound("Child.NotFound", "No such child in this Samaaj."));
        }

        var family = await families.GetForMemberAsync(memberId, cancellationToken);

        if (family is null || family.Id != child.FamilyId || !family.IsHead(memberId))
        {
            return Result.Failure<ConversionRequestResponse>(Error.Forbidden(
                "Child.NotYours", "Only the head of this child's family can request their conversion."));
        }

        if (child.Status == ChildStatus.Converted)
        {
            return Result.Failure<ConversionRequestResponse>(Error.Conflict(
                "Child.AlreadyConverted", "This child already has a member account."));
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        if (!child.IsEligibleForConversion(today))
        {
            return Result.Failure<ConversionRequestResponse>(Error.Conflict(
                "Child.NotEligible",
                $"A child can be converted once they turn {ChildProfile.AdultAge}."));
        }

        if (await conversions.GetPendingForChildAsync(child.Id, cancellationToken) is not null)
        {
            return Result.Failure<ConversionRequestResponse>(Error.Conflict(
                "Child.ConversionPending", "A conversion request for this child is already awaiting approval."));
        }

        var request = ChildConversionRequest.Raise(
            child.TenantId, child.Id, memberId, command.MobileOrEmail, clock.UtcNow);

        conversions.Add(request);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(request.ToResponse(child.FullName));
    }
}
