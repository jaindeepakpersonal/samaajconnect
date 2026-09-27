using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Collections;

namespace Sangam.Trust.Application.IntegrationEvents;

/// <summary>
/// One event off the bus: a published Boli result, opening a Due
/// <see cref="Domain.Collections.Collection"/> against the winning member when
/// (and only when) that Boli's locality has a Trust.
/// </summary>
/// <remarks>
/// <b>A no-op is the ordinary outcome here, not an error.</b> Most Boli occasions
/// carry no <c>LocalityId</c> at all - a Samaaj too small to split by locality
/// keeps running one Samaaj-wide Boli - and even a locality-tagged one may have
/// no Trust set up for it yet. Neither is a failure this consumer should retry
/// or complain about; it simply has nothing to open, the same stance
/// pathshala-service's own conversion consumer takes on a payload it cannot use.
/// </remarks>
[InternalRequest]
public sealed record ConsumeIntegrationEventCommand(IntegrationEventEnvelope Envelope) : ICommand<int>;

public sealed class ConsumeIntegrationEventCommandHandler(
    ITrustRepository trusts,
    ICollectionRepository collections,
    IDateTimeProvider clock,
    ILogger<ConsumeIntegrationEventCommandHandler> logger)
    : IRequestHandler<ConsumeIntegrationEventCommand, Result<int>>
{
    /// <summary>The one topic this service subscribes to.</summary>
    public const string BoliResultPublishedTopic = "boli.result.published.v1";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<int>> Handle(ConsumeIntegrationEventCommand command, CancellationToken cancellationToken)
    {
        var envelope = command.Envelope;

        if (envelope.Topic != BoliResultPublishedTopic)
        {
            // Subscribed by explicit topic list, so this is unreachable in
            // practice. Success, not failure: refusing would stall the
            // partition over a message nothing here was ever going to act on.
            return Result.Success(0);
        }

        BoliResultPublishedPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<BoliResultPublishedPayload>(envelope.Payload, PayloadOptions);
        }
        catch (JsonException exception)
        {
            logger.LogError(
                exception, "Could not read {MessageId} from {Topic}", envelope.MessageId, envelope.Topic);

            return Result.Success(0);
        }

        if (payload is null || payload.BoliId == Guid.Empty || payload.WinningMemberId == Guid.Empty)
        {
            logger.LogWarning(
                "{MessageId} from {Topic} named no Boli or no winner", envelope.MessageId, envelope.Topic);

            return Result.Success(0);
        }

        if (payload.LocalityId is not { } localityId)
        {
            // A Samaaj-wide Boli - nothing here for a locality Trust to collect.
            logger.LogInformation(
                "{MessageId}: Boli {BoliId} carries no locality, nothing to collect against",
                envelope.MessageId, payload.BoliId);

            return Result.Success(0);
        }

        var trust = await trusts.FindByLocalityAsync(envelope.TenantId, localityId, cancellationToken);

        if (trust is null)
        {
            logger.LogInformation(
                "{MessageId}: locality {LocalityId} has no Trust yet, nothing to collect against",
                envelope.MessageId, localityId);

            return Result.Success(0);
        }

        var collection = Domain.Collections.Collection.Open(
            envelope.TenantId, trust.Id, CollectionSourceType.BoliResult, payload.BoliId, payload.WinningMemberId, payload.Amount,
            clock.UtcNow);

        // Insert-and-catch-the-unique-violation, exactly like
        // ITrustRepository.TryAddAsync: at-least-once delivery means this
        // message can arrive twice, and the unique index on (SourceType,
        // SourceId) is what makes a redelivery a no-op instead of a second
        // Collection for the same Boli.
        var opened = await collections.TryOpenAsync(collection, cancellationToken);

        if (opened)
        {
            logger.LogInformation(
                "Opened a due collection for Trust {TrustId} against Boli {BoliId}", trust.Id, payload.BoliId);
        }

        return Result.Success(opened ? 1 : 0);
    }

    /// <summary>
    /// The fields this service reads off <c>boli.result.published.v1</c>.
    /// Deliberately not the whole event - <c>OccasionId</c> and
    /// <c>WinningMemberId</c>'s own name are boli-service's business, not this
    /// one's, beyond what is needed to open a collection.
    /// </summary>
    private sealed record BoliResultPublishedPayload(
        Guid BoliId, Guid WinningMemberId, long Amount, Guid? LocalityId);
}
