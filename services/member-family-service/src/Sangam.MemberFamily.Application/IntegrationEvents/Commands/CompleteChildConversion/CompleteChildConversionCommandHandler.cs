using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Domain.Children;

namespace Sangam.MemberFamily.Application.IntegrationEvents.Commands.CompleteChildConversion;

public sealed class CompleteChildConversionCommandHandler(
    IChildRepository children,
    IUnitOfWork unitOfWork,
    ILogger<CompleteChildConversionCommandHandler> logger)
    : IRequestHandler<CompleteChildConversionCommand, Result<CompleteChildConversionResult>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<CompleteChildConversionResult>> Handle(
        CompleteChildConversionCommand command,
        CancellationToken cancellationToken)
    {
        var payload = Parse(command.Event);

        if (payload is null)
        {
            logger.LogWarning(
                "Ignoring {MessageId}: ChildConversionCompleted payload could not be read",
                command.Event.MessageId);

            return Result.Success(new CompleteChildConversionResult(false, null));
        }

        var child = await children.GetForConsumerAsync(payload.ChildProfileId, cancellationToken);

        if (child is null)
        {
            logger.LogWarning(
                "Ignoring {MessageId}: child {ChildProfileId} no longer exists",
                command.Event.MessageId,
                payload.ChildProfileId);

            return Result.Success(new CompleteChildConversionResult(false, null));
        }

        if (child.Status == ChildStatus.Converted)
        {
            // A redelivery. Normal, given at-least-once.
            return Result.Success(new CompleteChildConversionResult(false, child.Id));
        }

        child.MarkConverted(payload.UserId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Child {ChildProfileId} converted to member account {UserId}",
            child.Id,
            payload.UserId);

        return Result.Success(new CompleteChildConversionResult(true, child.Id));
    }

    private static ChildConversionCompletedPayload? Parse(IntegrationEventEnvelope envelope)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<ChildConversionCompletedPayload>(
                envelope.Payload, JsonOptions);

            return payload is null
                || payload.UserId == Guid.Empty
                || payload.ChildProfileId == Guid.Empty
                    ? null
                    : payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Mirrors identity-tenant-service's UserActivatedFromChildDomainEvent. A
    /// local copy on purpose: consuming another service's type would couple the
    /// two deployments together.
    /// </summary>
    private sealed record ChildConversionCompletedPayload(
        Guid UserId,
        Guid TenantId,
        Guid ChildProfileId);
}
