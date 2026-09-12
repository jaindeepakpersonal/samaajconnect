using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;

namespace Sangam.Matrimony.Application.IntegrationEvents.Commands.EraseMatrimonyData;

/// <summary>
/// Erases everything this service holds about a member whose account has
/// been erased (DPDP section 12) - the biodata profile fully, and both
/// sides of any proposal they were part of, de-identified rather than
/// deleted so the *other* party's own sent/received history is not silently
/// broken (the same "keep the row, strip the person" shape
/// member-family-service's own `ChildProfile` erasure already uses).
/// </summary>
[InternalRequest]
public sealed record EraseMatrimonyDataCommand(IntegrationEventEnvelope Event) : ICommand<EraseMatrimonyDataResult>;

public sealed record EraseMatrimonyDataResult(bool Erased, int ProposalsDeIdentified);

public sealed class EraseMatrimonyDataCommandHandler(
    IBiodataRepository biodata,
    IProposalRepository proposals,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<EraseMatrimonyDataCommandHandler> logger)
    : IRequestHandler<EraseMatrimonyDataCommand, Result<EraseMatrimonyDataResult>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<EraseMatrimonyDataResult>> Handle(
        EraseMatrimonyDataCommand command, CancellationToken cancellationToken)
    {
        var payload = Parse(command.Event);

        if (payload is null)
        {
            logger.LogWarning(
                "Ignoring {MessageId}: UserErased payload could not be read", command.Event.MessageId);

            return Result.Success(new EraseMatrimonyDataResult(false, 0));
        }

        var now = clock.UtcNow;
        var erasedAnything = false;

        var profile = await biodata.GetForConsumerAsync(payload.UserId, cancellationToken);

        if (profile is not null)
        {
            profile.Erase(now);
            erasedAnything = true;
        }

        var deIdentified = 0;

        foreach (var proposal in await proposals.ListForConsumerAsync(payload.UserId, cancellationToken))
        {
            proposal.DeIdentify(now);
            deIdentified++;
        }

        if (!erasedAnything && deIdentified == 0)
        {
            // Already gone, or never opted in here. At-least-once delivery
            // makes a repeat of this normal.
            return Result.Success(new EraseMatrimonyDataResult(false, 0));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Erased matrimony data for member {UserId}: biodata={Erased}, proposals de-identified={Count}",
            payload.UserId, erasedAnything, deIdentified);

        return Result.Success(new EraseMatrimonyDataResult(erasedAnything, deIdentified));
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
    /// Mirrors identity-tenant-service's UserErasedDomainEvent. A local copy
    /// on purpose: consuming another service's type would couple the two
    /// deployments together.
    /// </summary>
    private sealed record UserErasedPayload(Guid UserId, Guid TenantId);
}
