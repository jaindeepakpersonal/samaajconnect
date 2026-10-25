using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Domain.Children;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.IntegrationEvents.Commands.CompleteChildConversion;

public sealed class CompleteChildConversionCommandHandler(
    IChildRepository children,
    IMemberProfileRepository profiles,
    IFamilyRepository families,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<CompleteChildConversionCommandHandler> logger)
    : IRequestHandler<CompleteChildConversionCommand, Result<CompleteChildConversionResult>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<CompleteChildConversionResult>> Handle(
        CompleteChildConversionCommand command,
        CancellationToken cancellationToken)
    {
        var payload = Parse(command.Event);

        if (payload is null)
        {
            logger.LogWarning(
                "Ignoring {MessageId}: ChildConversionCompleted payload could not be read",
                command.Event.MessageId);

            return Result.Success(new CompleteChildConversionResult(false, false, null));
        }

        var child = await children.GetForConsumerAsync(payload.ChildProfileId, cancellationToken);

        if (child is null)
        {
            logger.LogWarning(
                "Ignoring {MessageId}: child {ChildProfileId} no longer exists",
                command.Event.MessageId,
                payload.ChildProfileId);

            return Result.Success(new CompleteChildConversionResult(false, false, null));
        }

        // The lookup bypassed the tenant filter, so the event's tenant is the
        // only thing tying this child to the account. A mismatch is not a
        // conversion this service should complete, whoever sent it.
        if (child.TenantId != payload.TenantId)
        {
            logger.LogWarning(
                "Ignoring {MessageId}: child {ChildProfileId} is not in Samaaj {TenantId}",
                command.Event.MessageId,
                child.Id,
                payload.TenantId);

            return Result.Success(new CompleteChildConversionResult(false, false, null));
        }

        if (child.Status == ChildStatus.Converted && child.ConvertedMemberId != payload.UserId)
        {
            // Linked once, and never re-linked to some other account.
            return Result.Success(new CompleteChildConversionResult(false, false, child.Id));
        }

        var completed = false;

        if (child.Status != ChildStatus.Converted)
        {
            child.MarkConverted(payload.UserId);
            completed = true;
        }

        // Checked separately from the status rather than only on first
        // completion, so a redelivery also gives a profile to a child converted
        // before this consumer created one. The primary key is the user id, so
        // it is never created twice.
        var profileCreated = false;

        if (!await profiles.ExistsAsync(payload.UserId, cancellationToken))
        {
            await CreateProfileAsync(child, payload, cancellationToken);
            profileCreated = true;
        }

        if (!completed && !profileCreated)
        {
            // A redelivery. Normal, given at-least-once.
            return Result.Success(new CompleteChildConversionResult(false, false, child.Id));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Child {ChildProfileId} converted to member account {UserId} (profile created: {ProfileCreated})",
            child.Id,
            payload.UserId,
            profileCreated);

        return Result.Success(new CompleteChildConversionResult(completed, profileCreated, child.Id));
    }

    /// <summary>
    /// The new member's profile, and their place in the household they were a
    /// child of.
    /// </summary>
    /// <remarks>
    /// Joining happens only alongside creating the profile, never on its own.
    /// Once the profile exists the member has had the chance to leave their
    /// household, and a late redelivery quietly putting them back would undo a
    /// decision of theirs.
    /// </remarks>
    private async Task CreateProfileAsync(
        ChildProfile child,
        ChildConversionCompletedPayload payload,
        CancellationToken cancellationToken)
    {
        var family = await families.GetByIdForConsumerAsync(child.FamilyId, cancellationToken);

        if (family is not null && family.TenantId != child.TenantId)
        {
            family = null;
        }

        var address = ProfileAddress.Empty;

        if (family is not null
            && await profiles.GetForConsumerAsync(family.FamilyAdminMemberId, cancellationToken) is { } admin
            && admin.TenantId == child.TenantId)
        {
            address = admin.PermanentAddress;
        }

        var now = clock.UtcNow;

        profiles.Add(MemberProfile.FromConvertedChild(
            payload.UserId,
            child.TenantId,
            child.FullName,
            child.DateOfBirth,
            child.Gender,
            payload.MobileOrEmail,
            address,
            now));

        // A member belongs to one household, so somebody already in one -
        // which a brand-new account should never be - is left where they are.
        if (family is not null
            && await families.GetForConsumerAsync(payload.UserId, cancellationToken) is null)
        {
            family.AdmitConvertedChild(payload.UserId, now);
        }
    }

    private static ChildConversionCompletedPayload? Parse(IntegrationEventEnvelope envelope)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<ChildConversionCompletedPayload>(
                envelope.Payload, JsonOptions);

            return payload is null
                || payload.UserId == Guid.Empty
                || payload.TenantId == Guid.Empty
                || payload.ChildProfileId == Guid.Empty
                    ? null
                    : payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Mirrors identity-tenant-service's UserActivatedFromChildDomainEvent. A
    /// local copy on purpose: consuming another service's type would couple the
    /// two deployments together.
    /// </summary>
    private sealed record ChildConversionCompletedPayload(
        Guid UserId,
        Guid TenantId,
        Guid ChildProfileId,
        string? MobileOrEmail);
}
