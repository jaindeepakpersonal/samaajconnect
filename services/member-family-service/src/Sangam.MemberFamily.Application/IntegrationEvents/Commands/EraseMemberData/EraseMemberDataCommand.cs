using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.IntegrationEvents.Commands.EraseMemberData;

/// <summary>
/// Erases everything this service holds about a member whose account has been
/// erased (DPDP section 12).
/// </summary>
[InternalRequest]
public sealed record EraseMemberDataCommand(IntegrationEventEnvelope Event)
    : ICommand<EraseMemberDataResult>;

public sealed record EraseMemberDataResult(bool Erased, int ChildrenErased);

public sealed class EraseMemberDataCommandHandler(
    IMemberProfileRepository profiles,
    IFamilyRepository families,
    IChildRepository children,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<EraseMemberDataCommandHandler> logger)
    : IRequestHandler<EraseMemberDataCommand, Result<EraseMemberDataResult>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<EraseMemberDataResult>> Handle(
        EraseMemberDataCommand command,
        CancellationToken cancellationToken)
    {
        var payload = Parse(command.Event);

        if (payload is null)
        {
            logger.LogWarning(
                "Ignoring {MessageId}: UserErased payload could not be read",
                command.Event.MessageId);

            return Result.Success(new EraseMemberDataResult(false, 0));
        }

        var profile = await profiles.GetForConsumerAsync(payload.UserId, cancellationToken);

        if (profile is null)
        {
            // Already gone, or never existed here. Either way there is nothing
            // to do, and at-least-once delivery makes a repeat normal.
            return Result.Success(new EraseMemberDataResult(false, 0));
        }

        profile.Erase(clock.UtcNow);

        var childrenErased = 0;
        var family = await families.GetForConsumerAsync(payload.UserId, cancellationToken);

        if (family is not null && family.IsHead(payload.UserId))
        {
            // Children this member headed go too. Their records were held on
            // this person's parental consent, and consent that no longer
            // exists cannot keep justifying the data it covered.
            foreach (var child in await children.ListForConsumerAsync(family.Id, cancellationToken))
            {
                child.Erase();
                childrenErased++;
            }
        }

        // Who was in whose household is personal data about this member, so
        // the membership row goes in every case.
        //
        // The household itself stays, even when the erased member headed it.
        // Deleting it would take the remaining members' join with it and
        // orphan the child rows - other people's data restructured because
        // one person exercised their own right. A household whose head
        // has erased can no longer decide a join request - re-heading one is a
        // gap, and belongs in an admin command, not here.
        family?.RemoveMember(payload.UserId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Erased member {UserId} and {ChildCount} child record(s) on request",
            payload.UserId,
            childrenErased);

        return Result.Success(new EraseMemberDataResult(true, childrenErased));
    }

    private static UserErasedPayload? Parse(IntegrationEventEnvelope envelope)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<UserErasedPayload>(envelope.Payload, JsonOptions);

            return payload is null || payload.UserId == Guid.Empty ? null : payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Mirrors identity-tenant-service's UserErasedDomainEvent. A local copy on
    /// purpose: consuming another service's type would couple the two
    /// deployments together.
    /// </summary>
    private sealed record UserErasedPayload(Guid UserId, Guid TenantId);
}
