using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.IntegrationEvents.Commands.CreateProfileForNewUser;

public sealed class CreateProfileForNewUserCommandHandler(
    IMemberProfileRepository profiles,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<CreateProfileForNewUserCommandHandler> logger)
    : IRequestHandler<CreateProfileForNewUserCommand, Result<CreateProfileForNewUserResult>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<CreateProfileForNewUserResult>> Handle(
        CreateProfileForNewUserCommand command,
        CancellationToken cancellationToken)
    {
        var payload = Parse(command.Event);

        if (payload is null)
        {
            // Succeeding on an unusable payload rather than failing forever:
            // the consumer would otherwise retry it to exhaustion and stall
            // every registration behind it. The Warning below is the signal.
            logger.LogWarning(
                "Ignoring {MessageId}: UserRegistered payload could not be read",
                command.Event.MessageId);

            return Result.Success(new CreateProfileForNewUserResult(false, null));
        }

        // The profile shares the user's id, so "have I already made one?" is a
        // primary-key lookup rather than a dedupe table.
        if (await profiles.ExistsAsync(payload.UserId, cancellationToken))
        {
            return Result.Success(new CreateProfileForNewUserResult(false, payload.UserId));
        }

        var profile = MemberProfile.FromRegistration(
            payload.UserId,
            payload.TenantId,
            payload.FullName,
            payload.MobileOrEmail,
            clock.UtcNow);

        profiles.Add(profile);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created profile {MemberId} in Samaaj {TenantId} from registration",
            profile.Id,
            profile.TenantId);

        return Result.Success(new CreateProfileForNewUserResult(true, profile.Id));
    }

    private static UserRegisteredPayload? Parse(IntegrationEventEnvelope envelope)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<UserRegisteredPayload>(envelope.Payload, JsonOptions);

            return payload is null
                || payload.UserId == Guid.Empty
                || payload.TenantId == Guid.Empty
                || string.IsNullOrWhiteSpace(payload.FullName)
                    ? null
                    : payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Mirrors identity-tenant-service's UserRegisteredDomainEvent. A local
    /// copy on purpose: consuming another service's type would couple the two
    /// deployments together, which is most of what having separate services was
    /// meant to avoid.
    /// </summary>
    private sealed record UserRegisteredPayload(
        Guid UserId,
        Guid TenantId,
        string MobileOrEmail,
        string FullName);
}
