using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;

namespace Sangam.Businesses.Application.IntegrationEvents.Commands.EraseBusinessesData;

/// <summary>
/// Erases everything this service holds about a member whose account has
/// been erased (DPDP section 12). Every business the member owned is
/// removed outright - unlike Matrimony's `Proposal`, a business row carries
/// no stake for any other party, only the `Order`s referencing it by plain
/// id do - and every order the member placed is kept but de-identified
/// (its note stripped) so the business owner's own order history is not
/// silently broken.
/// </summary>
[InternalRequest]
public sealed record EraseBusinessesDataCommand(IntegrationEventEnvelope Event) : ICommand<EraseBusinessesDataResult>;

public sealed record EraseBusinessesDataResult(int BusinessesErased, int OrdersDeIdentified);

public sealed class EraseBusinessesDataCommandHandler(
    IBusinessRepository businesses,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    ILogger<EraseBusinessesDataCommandHandler> logger)
    : IRequestHandler<EraseBusinessesDataCommand, Result<EraseBusinessesDataResult>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<EraseBusinessesDataResult>> Handle(
        EraseBusinessesDataCommand command, CancellationToken cancellationToken)
    {
        var payload = Parse(command.Event);

        if (payload is null)
        {
            logger.LogWarning(
                "Ignoring {MessageId}: UserErased payload could not be read", command.Event.MessageId);

            return Result.Success(new EraseBusinessesDataResult(0, 0));
        }

        var owned = await businesses.ListForConsumerAsync(payload.UserId, cancellationToken);

        foreach (var business in owned)
        {
            businesses.Remove(business);
        }

        var deIdentified = 0;

        foreach (var order in await orders.ListPlacedForConsumerAsync(payload.UserId, cancellationToken))
        {
            order.DeIdentifyBuyer();
            deIdentified++;
        }

        if (owned.Count == 0 && deIdentified == 0)
        {
            // Never listed a business here, or already gone. At-least-once
            // delivery makes a repeat of this normal.
            return Result.Success(new EraseBusinessesDataResult(0, 0));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Erased businesses data for member {UserId}: businesses removed={BusinessCount}, orders de-identified={OrderCount}",
            payload.UserId, owned.Count, deIdentified);

        return Result.Success(new EraseBusinessesDataResult(owned.Count, deIdentified));
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
