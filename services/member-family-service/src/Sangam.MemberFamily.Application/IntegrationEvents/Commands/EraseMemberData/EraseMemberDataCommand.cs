using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Domain.Media;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.IntegrationEvents.Commands.EraseMemberData;

/// <summary>
/// Erases everything this service holds about a member whose account has been
/// erased (DPDP section 12).
/// </summary>
[InternalRequest]
public sealed record EraseMemberDataCommand(IntegrationEventEnvelope Event)
    : ICommand<EraseMemberDataResult>;

public sealed record EraseMemberDataResult(bool Erased, int ChildrenErased);

public sealed class EraseMemberDataCommandHandler(
    IMemberProfileRepository profiles,
    IFamilyRepository families,
    IChildRepository children,
    IImageStore images,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<EraseMemberDataCommandHandler> logger)
    : IRequestHandler<EraseMemberDataCommand, Result<EraseMemberDataResult>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<EraseMemberDataResult>> Handle(
        EraseMemberDataCommand command,
        CancellationToken cancellationToken)
    {
        var payload = Parse(command.Event);

        if (payload is null)
        {
            logger.LogWarning(
                "Ignoring {MessageId}: UserErased payload could not be read",
                command.Event.MessageId);

            return Result.Success(new EraseMemberDataResult(false, 0));
        }

        var profile = await profiles.GetForConsumerAsync(payload.UserId, cancellationToken);

        if (profile is null)
        {
            // Already gone, or never existed here. Either way there is nothing
            // to do, and at-least-once delivery makes a repeat normal.
            return Result.Success(new EraseMemberDataResult(false, 0));
        }

        profile.Erase(clock.UtcNow);

        // The photograph, not only the reference to it. A picture of a person is
        // the most directly identifying thing this service holds, and a row of
        // bytes nothing points at is not erased - it is merely unreachable by
        // the paths that happen to exist today.
        //
        // RemoveAllForOwnerAsync rather than deleting the id the profile held:
        // that deletes only the one this service knew about, and "we removed
        // the one we knew about" is not what erasure means.
        await images.RemoveAllForOwnerAsync(
            profile.TenantId, ImageOwnerKind.Member, profile.Id, cancellationToken);

        var childrenErased = 0;
        var family = await families.GetForConsumerAsync(payload.UserId, cancellationToken);

        if (family is not null && family.IsHead(payload.UserId))
        {
            // Children this member headed go too. Their records were held on
            // this person's parental consent, and consent that no longer
            // exists cannot keep justifying the data it covered.
            foreach (var child in await children.ListForConsumerAsync(family.Id, cancellationToken))
            {
                child.Erase();

                await images.RemoveAllForOwnerAsync(
                    child.TenantId, ImageOwnerKind.Child, child.Id, cancellationToken);

                childrenErased++;
            }
        }

        // Who was in whose household is personal data about this member, so
        // the membership row goes in every case.
        //
        // The household itself stays, even when the erased member headed it.
        // Deleting it would take the remaining members' join with it and
        // orphan the child rows - other people's data restructured because
        // one person exercised their own right.
        family?.RemoveMember(payload.UserId);

        // And headship passes to the longest-standing member left.
        //
        // This file used to say re-heading "belongs in an admin command, not
        // here", and that was wrong. An admin command needs an administrator to
        // notice, and nothing tells them - so a household whose head erased
        // stayed frozen until somebody complained: no join request could be
        // decided, no child added, no conversion started, and the family code
        // was invisible because only a head is shown it. Four things broken for
        // everyone left, because one person exercised a right.
        //
        // Doing it here means the headless state never exists rather than
        // existing until repaired, and the decision is small enough to explain
        // in a sentence: the longest-standing member takes over.
        if (family?.SucceedHeadAfterRemoval(payload.UserId) is { } successor)
        {
            logger.LogInformation(
                "Household {FamilyId} is now headed by {MemberId} after its head erased",
                family.Id,
                successor);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Erased member {UserId} and {ChildCount} child record(s) on request",
            payload.UserId,
            childrenErased);

        return Result.Success(new EraseMemberDataResult(true, childrenErased));
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
