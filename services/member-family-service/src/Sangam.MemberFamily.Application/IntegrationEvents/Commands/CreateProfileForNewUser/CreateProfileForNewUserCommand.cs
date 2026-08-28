using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.IntegrationEvents.Commands.CreateProfileForNewUser;

/// <summary>
/// Creates the profile that follows a registration in identity-tenant-service.
/// </summary>
/// <remarks>
/// Raised by this service's Kafka consumer on `identity.user.registered.v1`,
/// never from an endpoint - hence [InternalRequest]. This is the platform's
/// first genuine cross-service flow: registering creates an account in one
/// service and a profile in another, with no synchronous call between them.
/// </remarks>
[InternalRequest]
public sealed record CreateProfileForNewUserCommand(IntegrationEventEnvelope Event)
    : ICommand<CreateProfileForNewUserResult>;

public sealed record CreateProfileForNewUserResult(bool Created, Guid? MemberId);
