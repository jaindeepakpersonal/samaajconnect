using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.IntegrationEvents.Commands.CreateAccountForConvertedChild;

/// <summary>
/// Creates the account behind an adult-child conversion a Samaaj admin has
/// approved. Raised by this service's Kafka consumer, never from an endpoint.
/// </summary>
[InternalRequest]
public sealed record CreateAccountForConvertedChildCommand(IntegrationEventEnvelope Event)
    : ICommand<CreateAccountForConvertedChildResult>;

public sealed record CreateAccountForConvertedChildResult(bool Created, Guid? UserId);
