using Sangam.AuditNotification.Application.Common;
using Sangam.AuditNotification.Application.Security;

namespace Sangam.AuditNotification.Application.IntegrationEvents.Commands.RecordIntegrationEvent;

/// <summary>
/// Records one consumed event: always an audit row, and a notification when the
/// event is one a member should hear about.
/// </summary>
/// <remarks>
/// Raised by this service's Kafka consumer, never routed from an endpoint, so
/// it carries [InternalRequest] rather than [AllowAnonymousRequest].
/// </remarks>
[InternalRequest]
public sealed record RecordIntegrationEventCommand(IntegrationEventEnvelope Event)
    : ICommand<RecordIntegrationEventResult>;

/// <summary>
/// <paramref name="AlreadyRecorded"/> is true when this event had already been
/// handled. That is a normal outcome, not an error: delivery is at-least-once.
/// </summary>
public sealed record RecordIntegrationEventResult(
    bool AlreadyRecorded,
    bool NotificationRaised);
