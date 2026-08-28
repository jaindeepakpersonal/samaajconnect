using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.IntegrationEvents.Commands.CreateAccountForConvertedChild;

public sealed class CreateAccountForConvertedChildCommandHandler(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<CreateAccountForConvertedChildCommandHandler> logger)
    : IRequestHandler<CreateAccountForConvertedChildCommand, Result<CreateAccountForConvertedChildResult>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<CreateAccountForConvertedChildResult>> Handle(
        CreateAccountForConvertedChildCommand command,
        CancellationToken cancellationToken)
    {
        var payload = Parse(command.Event);

        if (payload is null)
        {
            // Succeeding on an unusable payload rather than failing forever:
            // the consumer would otherwise retry it to exhaustion and stall
            // every approval behind it. The Warning is the signal.
            logger.LogWarning(
                "Ignoring {MessageId}: ChildConversionApproved payload could not be read",
                command.Event.MessageId);

            return Result.Success(new CreateAccountForConvertedChildResult(false, null));
        }

        // Idempotent on the child, not on the message id: a redelivery and a
        // genuine re-approval must both be no-ops once the account exists.
        if (await users.GetByConvertedChildAsync(payload.ChildProfileId, cancellationToken)
            is { } existing)
        {
            return Result.Success(new CreateAccountForConvertedChildResult(false, existing.Id));
        }

        var identifier = User.NormalizeIdentifier(payload.MobileOrEmail);

        if (await users.IdentifierExistsAsync(identifier, cancellationToken))
        {
            // The family chose an identifier someone already signs in with.
            // Nothing here can resolve that, and creating a second account on
            // it would break the "one identifier, one Samaaj" rule login
            // depends on - so it is logged for a human and dropped.
            logger.LogError(
                "Cannot create the account for child {ChildProfileId}: {Identifier} is already in use",
                payload.ChildProfileId,
                identifier);

            return Result.Success(new CreateAccountForConvertedChildResult(false, null));
        }

        var user = User.CreateFromChildConversion(
            payload.TenantId,
            identifier,
            payload.FullName,
            payload.ChildProfileId,
            AuthorizationCatalog.RoleIds.Member,
            clock.UtcNow);

        users.Add(user);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created account {UserId} for converted child {ChildProfileId}; awaiting activation",
            user.Id,
            payload.ChildProfileId);

        return Result.Success(new CreateAccountForConvertedChildResult(true, user.Id));
    }

    private static ChildConversionApprovedPayload? Parse(IntegrationEventEnvelope envelope)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<ChildConversionApprovedPayload>(
                envelope.Payload, JsonOptions);

            return payload is null
                || payload.TenantId == Guid.Empty
                || payload.ChildProfileId == Guid.Empty
                || string.IsNullOrWhiteSpace(payload.FullName)
                || string.IsNullOrWhiteSpace(payload.MobileOrEmail)
                    ? null
                    : payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Mirrors member-family-service's ChildConversionApprovedDomainEvent. A
    /// local copy on purpose: consuming another service's type would couple the
    /// two deployments together.
    /// </summary>
    private sealed record ChildConversionApprovedPayload(
        Guid RequestId,
        Guid TenantId,
        Guid ChildProfileId,
        string FullName,
        string MobileOrEmail);
}
