using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Common;
using Sangam.Pathshala.Application.Security;

namespace Sangam.Pathshala.Application.IntegrationEvents;

/// <summary>
/// One event off the bus.
/// </summary>
/// <remarks>
/// This service reacts to a single thing: a child profile becoming a real
/// account. Its enrolments then belong to somebody who can sign in, and until
/// they are linked the now-adult student cannot read their own Pathshala
/// history - which is the one promise the conversion flow makes about this
/// service.
///
/// <b>SERVICES.md names <c>ChildConversionApproved</c>, and that event cannot
/// do the job.</b> member-family-service publishes it when an admin approves
/// the conversion, before identity-tenant-service has created anything, so it
/// carries a child profile id and no user id - there is nothing to link to.
/// <c>identity.child-conversion.completed.v1</c> carries both, and is published
/// at the moment the link becomes true.
/// </remarks>
[InternalRequest]
public sealed record ConsumeIntegrationEventCommand(IntegrationEventEnvelope Envelope)
    : ICommand<int>;

public sealed class ConsumeIntegrationEventCommandHandler(
    IEnrolmentRepository enrolments,
    IUnitOfWork unitOfWork,
    ILogger<ConsumeIntegrationEventCommandHandler> logger)
    : IRequestHandler<ConsumeIntegrationEventCommand, Result<int>>
{
    /// <summary>The one topic this service subscribes to.</summary>
    public const string ConversionCompletedTopic = "identity.child-conversion.completed.v1";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<int>> Handle(
        ConsumeIntegrationEventCommand command, CancellationToken cancellationToken)
    {
        var envelope = command.Envelope;

        if (envelope.Topic != ConversionCompletedTopic)
        {
            // Subscribed by explicit topic list, so this is unreachable in
            // practice. Success rather than failure all the same: refusing
            // would stall the partition over a message nothing here was ever
            // going to act on.
            return Result.Success(0);
        }

        ConversionCompletedPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<ConversionCompletedPayload>(
                envelope.Payload, PayloadOptions);
        }
        catch (JsonException exception)
        {
            // Retrying will not fix a payload that does not parse. Logged with
            // the id, not the body: the body is about a child.
            logger.LogError(
                exception,
                "Could not read {MessageId} from {Topic}",
                envelope.MessageId,
                envelope.Topic);

            return Result.Success(0);
        }

        if (payload is null || payload.ChildProfileId == Guid.Empty || payload.UserId == Guid.Empty)
        {
            logger.LogWarning(
                "{MessageId} from {Topic} named no child or no account",
                envelope.MessageId,
                envelope.Topic);

            return Result.Success(0);
        }

        // Scoped by the tenant on the event rather than by the query filter.
        // The consumer has no request and so no resolved tenant, which makes the
        // filter compare every row against Guid.Empty - the link then silently
        // does nothing, which is exactly how this failed the first time it was
        // tested. See IEnrolmentRepository.ListForChildAsync.
        var found = await enrolments.ListForChildAsync(
            envelope.TenantId, payload.ChildProfileId, cancellationToken);
        var linked = 0;

        foreach (var enrolment in found)
        {
            if (enrolment.LinkTo(payload.UserId))
            {
                linked++;
            }
        }

        if (linked > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Linked {Count} Pathshala enrolment(s) to the account a converted child now holds",
                linked);
        }

        // Zero is success. Delivery is at least once, so the second copy of an
        // event finding nothing left to do is the ordinary case, and reporting
        // it as a failure would make the consumer retry it five times.
        return Result.Success(linked);
    }

    /// <summary>
    /// The fields this service reads off
    /// <c>identity.child-conversion.completed.v1</c>. Deliberately not the whole
    /// event: the identifier on it is a person's login and this service has no
    /// use for it.
    /// </summary>
    private sealed record ConversionCompletedPayload(Guid UserId, Guid ChildProfileId);
}
