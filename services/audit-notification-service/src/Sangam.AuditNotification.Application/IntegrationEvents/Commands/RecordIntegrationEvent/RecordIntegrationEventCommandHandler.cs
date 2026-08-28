using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Common;
using Sangam.AuditNotification.Domain.AuditLogs;
using Sangam.AuditNotification.Domain.Notifications;

namespace Sangam.AuditNotification.Application.IntegrationEvents.Commands.RecordIntegrationEvent;

public sealed class RecordIntegrationEventCommandHandler(
    IAuditLogRepository auditLogs,
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<RecordIntegrationEventCommandHandler> logger)
    : IRequestHandler<RecordIntegrationEventCommand, Result<RecordIntegrationEventResult>>
{
    public async Task<Result<RecordIntegrationEventResult>> Handle(
        RecordIntegrationEventCommand command,
        CancellationToken cancellationToken)
    {
        var envelope = command.Event;

        if (await auditLogs.AlreadyRecordedAsync(envelope.MessageId, cancellationToken))
        {
            // Expected, not exceptional: the publisher guarantees at-least-once.
            logger.LogDebug(
                "Skipping already-recorded message {MessageId} from {Topic}",
                envelope.MessageId,
                envelope.Topic);

            return Result.Success(new RecordIntegrationEventResult(AlreadyRecorded: true, NotificationRaised: false));
        }

        var descriptor = KnownEvents.Describe(envelope.Topic);
        var payload = ParsePayload(envelope);

        auditLogs.Add(AuditLog.FromEvent(
            envelope.TenantId,
            envelope.MessageId,
            envelope.Topic,
            envelope.EventType,
            descriptor.Action,
            envelope.Payload,
            envelope.OccurredAt,
            clock.UtcNow,
            actorUserId: ReadGuid(payload, descriptor.ActorIdProperty),
            entityName: descriptor.EntityName,
            entityId: ReadString(payload, descriptor.EntityIdProperty)));

        var notificationRaised = await TryRaiseNotificationAsync(
            envelope, descriptor, payload, cancellationToken);

        // One SaveChanges, so the audit row and any notification it produced
        // land together or not at all.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RecordIntegrationEventResult(false, notificationRaised));
    }

    private async Task<bool> TryRaiseNotificationAsync(
        IntegrationEventEnvelope envelope,
        EventDescriptor descriptor,
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        if (descriptor.Notification is null || payload is not { } element)
        {
            return false;
        }

        if (descriptor.Notification(element) is not { } spec)
        {
            return false;
        }

        if (await notifications.AlreadyRaisedAsync(envelope.MessageId, cancellationToken))
        {
            return false;
        }

        notifications.Add(Notification.Create(
            envelope.TenantId,
            spec.RecipientUserId,
            spec.Title,
            spec.Body,
            NotificationChannel.InApp,
            envelope.MessageId,
            clock.UtcNow));

        return true;
    }

    /// <summary>
    /// A payload that will not parse is still audited - the raw text is kept in
    /// AfterState - it just cannot contribute an actor, entity id or
    /// notification. Refusing the whole event would lose the record of it.
    /// </summary>
    private JsonElement? ParsePayload(IntegrationEventEnvelope envelope)
    {
        try
        {
            return JsonDocument.Parse(envelope.Payload).RootElement.Clone();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Message {MessageId} from {Topic} has an unparseable payload; auditing it verbatim",
                envelope.MessageId,
                envelope.Topic);

            return null;
        }
    }

    private static Guid? ReadGuid(JsonElement? payload, string? property) =>
        property is not null
        && payload is { } element
        && element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.TryGetGuid(out var guid)
            ? guid
            : null;

    private static string? ReadString(JsonElement? payload, string? property) =>
        property is not null
        && payload is { } element
        && element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
            ? value.ToString()
            : null;
}
