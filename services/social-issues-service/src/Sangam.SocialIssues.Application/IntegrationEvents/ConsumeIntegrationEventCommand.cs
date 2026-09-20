using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.SocialIssues.Application.Abstractions;
using Sangam.SocialIssues.Application.Common;
using Sangam.SocialIssues.Application.Security;

namespace Sangam.SocialIssues.Application.IntegrationEvents;

/// <summary>
/// One event off the bus.
/// </summary>
/// <remarks>
/// This service reacts to a single thing: a member erasing their account.
///
/// <b>It should have subscribed on the day it shipped and did not.</b>
/// DPDP-COMPLIANCE.md states that rule plainly, and six services broke it —
/// found by the security-checklist pass on 2026-09-01. Social issues is one of
/// the two where it mattered most: unlike a registration or a vote, an issue
/// carries free text its submitter wrote, which identifies them whatever
/// happens to the member id sitting beside it - and a reviewer decision hangs
/// off it, which is somebody else's record and stays.
///
/// What erasure does here is <see cref="Domain.Issues.SocialIssue.ErasePersonalDataOf"/>;
/// the reasoning about why the row survives is on that method.
/// </remarks>
[InternalRequest]
public sealed record ConsumeIntegrationEventCommand(IntegrationEventEnvelope Envelope)
    : ICommand<int>;

public sealed class ConsumeIntegrationEventCommandHandler(
    IIssueRepository issues,
    IPanelRepository panel,
    IUnitOfWork unitOfWork,
    ILogger<ConsumeIntegrationEventCommandHandler> logger)
    : IRequestHandler<ConsumeIntegrationEventCommand, Result<int>>
{
    public const string UserErasedTopic = "identity.user.erased.v1";

    /// <summary>The Samaaj Panch changed. Carries the whole roster.</summary>
    public const string PanelChangedTopic = "panch.panel.changed.v1";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<int>> Handle(
        ConsumeIntegrationEventCommand command, CancellationToken cancellationToken)
    {
        var envelope = command.Envelope;

        if (envelope.Topic == PanelChangedTopic)
        {
            return await ApplyPanelChangedAsync(envelope, cancellationToken);
        }

        if (envelope.Topic != UserErasedTopic)
        {
            // Subscribed by explicit topic list, so this is unreachable in
            // practice. Success rather than failure all the same: refusing
            // would stall the partition over a message nothing here was ever
            // going to act on.
            return Result.Success(0);
        }

        UserErasedPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<UserErasedPayload>(envelope.Payload, PayloadOptions);
        }
        catch (JsonException exception)
        {
            // Retrying will not fix a payload that does not parse.
            logger.LogError(
                exception, "Could not read {MessageId} from {Topic}", envelope.MessageId, envelope.Topic);

            return Result.Success(0);
        }

        var tenantId = payload?.TenantId is { } fromPayload && fromPayload != Guid.Empty
            ? fromPayload
            : envelope.TenantId;

        if (payload is null || payload.UserId == Guid.Empty || tenantId == Guid.Empty)
        {
            logger.LogWarning(
                "{MessageId} from {Topic} named no member or no Samaaj",
                envelope.MessageId,
                envelope.Topic);

            return Result.Success(0);
        }

        // The tenant is passed explicitly. A consumer resolves none, so the
        // global query filter would compare against Guid.Empty and match
        // nothing at all - an erasure that reports success and erases nothing.
        var touched = await issues.ListTouchedByMemberAsync(
            tenantId, payload.UserId, cancellationToken);

        var changed = touched.Count(issue => issue.ErasePersonalDataOf(payload.UserId));

        if (changed == 0)
        {
            // Either they never posted, or this is a redelivery. Both are
            // ordinary: delivery is at least once.
            return Result.Success(0);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The count and the Samaaj, never the text. This log line is about an
        // erasure; putting any of what was erased in it would defeat it.
        logger.LogInformation(
            "Erased personal data from {Count} social issues for a member of Samaaj {TenantId}",
            changed,
            tenantId);

        return Result.Success(changed);
    }

    /// <summary>
    /// Makes this Samaaj's copy of the Panch match the roster the event carries.
    /// </summary>
    /// <remarks>
    /// The event carries the whole roster, not a delta, so this <i>replaces</i>:
    /// applying the same snapshot twice leaves the same rows, which is what makes
    /// at-least-once delivery harmless here. A Samaaj's events share a partition
    /// key, so they arrive in order.
    /// </remarks>
    private async Task<Result<int>> ApplyPanelChangedAsync(
        IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        PanelChangedPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<PanelChangedPayload>(envelope.Payload, PayloadOptions);
        }
        catch (JsonException exception)
        {
            // Retrying will not fix a payload that does not parse.
            logger.LogError(
                exception, "Could not read {MessageId} from {Topic}", envelope.MessageId, envelope.Topic);

            return Result.Success(0);
        }

        var tenantId = payload?.TenantId is { } fromPayload && fromPayload != Guid.Empty
            ? fromPayload
            : envelope.TenantId;

        if (payload is null || payload.MemberIds is null || tenantId == Guid.Empty)
        {
            logger.LogWarning(
                "{MessageId} from {Topic} carried no roster or named no Samaaj",
                envelope.MessageId,
                envelope.Topic);

            return Result.Success(0);
        }

        if (!await panel.ReplaceAsync(tenantId, payload.MemberIds, cancellationToken))
        {
            // A redelivery of a roster already applied.
            return Result.Success(0);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Samaaj {TenantId} now has {Count} members on its Panch", tenantId, payload.MemberIds.Count);

        return Result.Success(payload.MemberIds.Count);
    }
}

/// <summary>
/// The payload of <c>panch.panel.changed.v1</c>: the whole roster as member ids.
/// </summary>
public sealed record PanelChangedPayload(Guid TenantId, IReadOnlyList<Guid>? MemberIds);

/// <summary>
/// The payload of <c>identity.user.erased.v1</c>. Two ids and a timestamp —
/// the publishing service deliberately puts nothing else on it, because
/// audit-notification-service records every payload verbatim.
/// </summary>
public sealed record UserErasedPayload(Guid UserId, Guid TenantId, DateTimeOffset OccurredAt);
