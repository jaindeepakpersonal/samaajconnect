using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Common;
using Sangam.AuditNotification.Application.Security;
using Sangam.AuditNotification.Domain.AuditLogs;

namespace Sangam.AuditNotification.Application.IntegrationEvents.Commands.ErasePersonalData;

/// <summary>
/// Erases this service's personal data about a member: notifications go, audit
/// rows are de-identified, and the erasure itself is recorded.
/// </summary>
/// <remarks>
/// This is where the platform's two hardest rules meet. DPDP sections 8(7) and
/// 12 require erasure; SECURITY-CHECKLIST.md requires audit rows to be
/// immutable, "no update/delete endpoint for AuditLog, ever".
///
/// The resolution, set out in docs/product/DPDP-COMPLIANCE.md: the <i>fact</i>
/// that an action happened survives, and the person disappears from it.
/// Notifications are deleted outright - they are messages to a person and
/// nothing else. Audit rows keep their action, entity and timestamps, and lose
/// the actor and any payload that named them.
///
/// This handler is the only code on the platform that changes or removes an
/// existing audit row. It is reachable only from the Kafka consumer, and only
/// for one topic; there is no endpoint.
///
/// Whether that reading of the section 8(7) retention exception is correct is
/// an open question for counsel. If the answer is that audit rows must go too,
/// the change is confined to this handler and IErasureRepository.
/// </remarks>
[InternalRequest]
public sealed record ErasePersonalDataCommand(IntegrationEventEnvelope Event)
    : ICommand<ErasePersonalDataResult>;

public sealed record ErasePersonalDataResult(
    bool AlreadyHandled,
    int NotificationsDeleted,
    int AuditRowsDeIdentified);

public sealed class ErasePersonalDataCommandHandler(
    IErasureRepository erasure,
    IAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<ErasePersonalDataCommandHandler> logger)
    : IRequestHandler<ErasePersonalDataCommand, Result<ErasePersonalDataResult>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<ErasePersonalDataResult>> Handle(
        ErasePersonalDataCommand command,
        CancellationToken cancellationToken)
    {
        var envelope = command.Event;
        var payload = Parse(envelope);

        if (payload is null)
        {
            // Nothing identifies who to erase, so there is nothing safe to do.
            // Recording it as an ordinary event would at least keep the fact.
            logger.LogError(
                "Erasure message {MessageId} carries no readable user id and was skipped",
                envelope.MessageId);

            return Result.Success(new ErasePersonalDataResult(false, 0, 0));
        }

        // Delivery is at-least-once. The delete and the de-identify are both
        // naturally idempotent - a second pass matches nothing - but the
        // completion row is not, so the same check gates the whole handler.
        if (await auditLogs.AlreadyRecordedAsync(envelope.MessageId, cancellationToken))
        {
            logger.LogDebug("Erasure message {MessageId} was already handled", envelope.MessageId);

            return Result.Success(new ErasePersonalDataResult(true, 0, 0));
        }

        var notificationsDeleted =
            await erasure.DeleteNotificationsForAsync(payload.UserId, cancellationToken);

        var rowsDeIdentified =
            await erasure.DeIdentifyAuditRowsForAsync(payload.UserId, cancellationToken);

        // Recorded after the de-identifying pass, so this row survives it. A
        // Samaaj has to be able to show it honoured an erasure request, and an
        // erasure with no record of having happened cannot be shown at all.
        //
        // The tombstone id in EntityId is what is left of the person: the
        // account it referred to no longer exists, and no other row on the
        // platform still carries it, so it maps to nobody.
        auditLogs.Add(AuditLog.FromEvent(
            payload.TenantId,
            envelope.MessageId,
            envelope.Topic,
            envelope.EventType,
            action: "Erased",
            payload: envelope.Payload,
            occurredAt: envelope.OccurredAt,
            recordedAt: clock.UtcNow,
            actorUserId: null,
            entityName: "User",
            entityId: payload.UserId.ToString()));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Counts only. A log line naming who was erased would preserve exactly
        // what the request was to remove, somewhere nobody thinks to redact.
        logger.LogWarning(
            "Erasure: deleted {NotificationCount} notification(s) and de-identified "
            + "{AuditCount} audit row(s)",
            notificationsDeleted,
            rowsDeIdentified);

        return Result.Success(
            new ErasePersonalDataResult(false, notificationsDeleted, rowsDeIdentified));
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
