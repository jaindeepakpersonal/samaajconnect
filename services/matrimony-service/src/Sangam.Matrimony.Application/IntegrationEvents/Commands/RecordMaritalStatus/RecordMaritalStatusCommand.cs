using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;
using Sangam.Matrimony.Domain.Biodata;

namespace Sangam.Matrimony.Application.IntegrationEvents.Commands.RecordMaritalStatus;

/// <summary>
/// Keeps this service's copy of whether a member may hold a biodata, from
/// member-family-service's `members.marital-status.changed.v1` (2026-10-24).
/// </summary>
/// <remarks>
/// A member who stops being eligible keeps their biodata row - it is theirs, and
/// a proposal the other party holds still names it - but search, opening it and
/// editing it all refuse while they are not eligible, and come back the moment
/// they are. An unusable payload is skipped with a warning, never retried to
/// exhaustion, the erasure consumer's precedent.
/// </remarks>
[InternalRequest]
public sealed record RecordMaritalStatusCommand(IntegrationEventEnvelope Event) : ICommand<bool>;

public sealed class RecordMaritalStatusCommandHandler(
    IEligibilityRepository eligibility,
    IUnitOfWork unitOfWork,
    ILogger<RecordMaritalStatusCommandHandler> logger)
    : IRequestHandler<RecordMaritalStatusCommand, Result<bool>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<bool>> Handle(RecordMaritalStatusCommand command, CancellationToken cancellationToken)
    {
        var payload = Parse(command.Event);

        if (payload is null)
        {
            logger.LogWarning(
                "Ignoring {MessageId}: marital status payload could not be read", command.Event.MessageId);

            return Result.Success(false);
        }

        var existing = await eligibility.GetForConsumerAsync(payload.TenantId, payload.MemberId, cancellationToken);

        if (existing is null)
        {
            eligibility.Add(MatrimonyEligibility.Record(
                payload.MemberId, payload.TenantId, payload.EligibleForMatrimony, payload.OccurredAt));
        }
        else if (!existing.Apply(payload.EligibleForMatrimony, payload.OccurredAt))
        {
            // A redelivery, or a change older than the one held.
            return Result.Success(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }

    private static MaritalStatusPayload? Parse(IntegrationEventEnvelope envelope)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<MaritalStatusPayload>(envelope.Payload, JsonOptions);

            return payload is null || payload.MemberId == Guid.Empty || payload.TenantId == Guid.Empty ? null : payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record MaritalStatusPayload(
        Guid MemberId, Guid TenantId, bool EligibleForMatrimony, DateTimeOffset OccurredAt);
}
